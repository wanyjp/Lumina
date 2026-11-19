using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Lumina.SystemAdapters.Clipboard;

/// <summary>
/// Windows 剪贴板监听：AddClipboardFormatListener 负责唤醒，独立 STA 线程读取内容，
/// 分类、隐私拦截、图片落盘与时间窗去重后写入仓储。注册失败时降级为 500ms 轮询。
/// </summary>
public sealed class WindowsClipboardMonitor : IClipboardMonitor, IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>回贴后的采集抑制窗口；覆盖一次写入可能引发的多条变更消息。</summary>
    private const int SuppressWindowMilliseconds = 800;

    /// <summary>P3-09：本进程可执行名，自身窗口默认不采集。</summary>
    private static readonly string OwnProcessName =
        Process.GetCurrentProcess().ProcessName + ".exe";

    private readonly IClipboardStore _store;
    private readonly ClipboardRetentionService _retention;
    private readonly IgnoreRuleService _ignoreRules;
    private readonly ISettingsStore _settings;
    private readonly ILogger<WindowsClipboardMonitor> _logger;
    private readonly ClipboardMessageWindow? _window;
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _gate = new();
    private readonly object _sourceGate = new();
    private readonly IDisposable _settingsSubscription;

    private ClipboardSource? _pendingSource;
    private Thread? _worker;
    private volatile bool _workerStop;
    private volatile bool _captureEnabled = true;
    private volatile bool _paused;
    private volatile bool _mergeDifferentSources = true;
    private bool _running;
    private bool _listenerRegistered;
    private bool _retentionApplied;
    private bool _disposed;
    private DateTimeOffset _suppressUntil = DateTimeOffset.MinValue;

    private string? _lastCapturedHash;
    private DateTimeOffset _lastCapturedAt = DateTimeOffset.MinValue;

    public WindowsClipboardMonitor(
        IClipboardStore store,
        ClipboardRetentionService retention,
        IgnoreRuleService ignoreRules,
        ISettingsStore settings,
        ILogger<WindowsClipboardMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(ignoreRules);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _retention = retention;
        _ignoreRules = ignoreRules;
        _settings = settings;
        _logger = logger;
        RefreshSettings();
        _settingsSubscription = settings.Changed.Subscribe(new SettingsObserver(ApplySetting));

        try
        {
            _window = new ClipboardMessageWindow(OnClipboardUpdate);
        }
        catch (Exception ex)
        {
            _window = null;
            _logger.LogWarning(ex, "剪贴板消息窗口创建失败，将使用轮询模式。");
        }
    }

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public event Action<ClipboardItem>? Captured;

    /// <summary>
    /// 短暂抑制采集（800ms）。一次回贴可能连续触发多条 WM_CLIPBOARDUPDATE，
    /// 单次忽略挡不住第二次，因此用时间窗整体抑制。
    /// </summary>
    public void SuppressCapture()
    {
        lock (_sourceGate)
        {
            _suppressUntil = DateTimeOffset.Now.AddMilliseconds(SuppressWindowMilliseconds);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsClipboardMonitor));
            }

            if (_running)
            {
                return;
            }

            RefreshSettings();
            _workerStop = false;
            _retentionApplied = false;
            _signal.Reset();

            if (_window is not null)
            {
                try
                {
                    _listenerRegistered = NativeMethods.AddClipboardFormatListener(_window.Handle);
                }
                catch (Exception ex)
                {
                    _listenerRegistered = false;
                    _logger.LogWarning(ex, "剪贴板监听注册异常，将使用轮询模式。");
                }
            }

            if (!_listenerRegistered)
            {
                _logger.LogWarning("剪贴板监听注册失败，已降级为 {Interval}ms 轮询。", (int)PollInterval.TotalMilliseconds);
            }

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Lumina.ClipboardCapture",
            };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
            _running = true;
        }

        _logger.LogInformation(
            "剪贴板监听已启动（事件={Listener}，采集={Enabled}，暂停={Paused}）。",
            _listenerRegistered ? "AddClipboardFormatListener" : "轮询",
            _captureEnabled,
            _paused);
    }

    public void Stop()
    {
        Thread? worker;
        lock (_gate)
        {
            if (!_running && _worker is null)
            {
                return;
            }

            _running = false;
            _workerStop = true;
            _signal.Set();
            worker = _worker;
            _worker = null;
        }

        if (worker is not null && worker != Thread.CurrentThread)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        UnregisterListener();
        _logger.LogInformation("剪贴板监听已停止。");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _settingsSubscription.Dispose();
        _window?.Destroy();
        _signal.Dispose();
    }

    private void WorkerLoop()
    {
        ApplyRetentionOnce();
        var lastSequence = NativeMethods.GetClipboardSequenceNumber();
        while (!_workerStop)
        {
            var signaled = _signal.WaitOne(PollInterval);

            if (!_listenerRegistered)
            {
                var sequence = NativeMethods.GetClipboardSequenceNumber();
                if (sequence == lastSequence)
                {
                    continue;
                }

                lastSequence = sequence;
            }
            else if (!signaled)
            {
                continue;
            }
            else
            {
                // 剪贴板写入方通常仍在持有锁，稍等再读，失败时由重试兜底。
                Thread.Sleep(60);
            }

            try
            {
                CaptureOnce();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "剪贴板采集循环出现异常，已跳过本次内容。");
            }
        }
    }

    private void ApplyRetentionOnce()
    {
        if (_retentionApplied)
        {
            return;
        }

        _retentionApplied = true;
        try
        {
            var evicted = _retention.Trim();
            var purged = _retention.PurgeSoftDeleted(DateTimeOffset.Now);
            if (evicted > 0 || purged > 0)
            {
                _logger.LogInformation(
                    "剪贴板启动清理完成：超出上限淘汰 {Evicted} 条，过期软删清理 {Purged} 条。",
                    evicted,
                    purged);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "剪贴板启动清理失败，已跳过本次清理。");
        }
    }

    private void CaptureOnce()
    {
        if (IsSuppressed())
        {
            _logger.LogDebug("剪贴板变更落在回贴抑制窗口内，已跳过。");
            return;
        }

        if (!_captureEnabled || _paused)
        {
            return;
        }

        var source = TakePendingSource();
        if (source is null)
        {
            // 来源不可读时保守拒绝：否则保险库/密码管理器窗口的忽略规则会全部失效。
            _logger.LogWarning("剪贴板来源窗口不可读，已保守跳过本次采集。");
            return;
        }

        var resolvedSource = source.Value;

        if (_ignoreRules.ShouldIgnoreSource(resolvedSource.WindowTitle, resolvedSource.ProcessName))
        {
            _logger.LogDebug("来源窗口命中忽略规则，已跳过本次内容。");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var item = CaptureItem(resolvedSource);
        if (item is null)
        {
            return;
        }

        // 内容级检测覆盖所有类型：HTML 取清洗后的纯文本，避免验证码/令牌绕过隐私过滤落库。
        if (_ignoreRules.BlockOneTimeCodes && IsSensitiveContent(item))
        {
            _logger.LogDebug("检测到一次性验证码或敏感令牌，默认不采集。");
            _retention.DeleteOwnedFiles([item]);
            return;
        }

        // Windows 对一次复制可能发送多条 WM_CLIPBOARDUPDATE；300ms 内同哈希只处理一次，
        // 避免单次复制把 dup_count 从 1 累加到 2。
        var now = DateTimeOffset.Now;
        if (string.Equals(item.ContentHash, _lastCapturedHash, StringComparison.Ordinal)
            && now - _lastCapturedAt < TimeSpan.FromMilliseconds(300))
        {
            _retention.DeleteOwnedFiles([item]);
            return;
        }

        try
        {
            var result = _store.Save(
                item,
                item.UpdatedAt,
                ClipboardCapturePolicy.DuplicateWindow,
                _retention.Limit,
                _mergeDifferentSources);
            _lastCapturedHash = item.ContentHash;
            _lastCapturedAt = item.UpdatedAt;
            if (result.Merged)
            {
                _retention.DeleteOwnedFiles([item]);
            }

            _retention.DeleteOwnedFiles(result.Evicted);

            stopwatch.Stop();
            _logger.LogInformation(
                "剪贴板采集 kind={Kind} chars={Chars} width={Width} height={Height} elapsedMs={ElapsedMs} merged={Merged} evicted={Evicted} source={Source}",
                ClipClassifier.KindKey(item.Kind),
                item.TextContent?.Length ?? 0,
                item.Width ?? 0,
                item.Height ?? 0,
                stopwatch.ElapsedMilliseconds,
                result.Merged,
                result.Evicted.Count,
                item.SourceWindow ?? "未知");

            if (result.Evicted.Count > 0)
            {
                _logger.LogInformation(
                    "剪贴板超出保留上限（{Limit}），本次清理 {Count} 条最旧非固定记录。",
                    _retention.Limit,
                    result.Evicted.Count);
            }

            try
            {
                Captured?.Invoke(result.Item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "剪贴板 Captured 订阅者异常。");
            }
        }
        catch (Exception ex)
        {
            _retention.DeleteOwnedFiles([item]);
            _logger.LogError(ex, "剪贴板写入失败，已丢弃本次内容。");
        }
    }

    /// <summary>当前是否处于回贴抑制窗口内。</summary>
    private bool IsSuppressed()
    {
        lock (_sourceGate)
        {
            return DateTimeOffset.Now <= _suppressUntil;
        }
    }

    private ClipboardItem? CaptureItem(ClipboardSource source)
    {
        IDataObject? data = null;
        for (var attempt = 0; attempt < 4 && data is null; attempt++)
        {
            try
            {
                data = System.Windows.Forms.Clipboard.GetDataObject();
            }
            catch (ExternalException) when (attempt < 3)
            {
                Thread.Sleep(40);
            }
        }

        if (data is null)
        {
            _logger.LogDebug("剪贴板数据读取失败或为空。");
            return null;
        }

        if (IsBlockedByPolicy(data, source))
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        var sourceText = DescribeSource(source);

        if (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib))
        {
            if (IsOversizedImage(data))
            {
                _logger.LogWarning("图片载荷超过 {Limit} 字节，已跳过。", ClipboardCapturePolicy.MaxImageBytes);
                return null;
            }

            var image = data.GetData(DataFormats.Bitmap, autoConvert: true) as Image;
            if (image is null && System.Windows.Forms.Clipboard.ContainsImage())
            {
                image = System.Windows.Forms.Clipboard.GetImage();
            }

            if (image is not null)
            {
                try
                {
                    return BuildImageItem(image, sourceText, now);
                }
                finally
                {
                    image.Dispose();
                }
            }
        }

        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = ReadFileDrop(data);
            if (files.Count > 0)
            {
                return BuildFileItem(files, sourceText, now);
            }
        }

        if (data.GetDataPresent(DataFormats.Html))
        {
            var html = data.GetData(DataFormats.Html, autoConvert: false) as string;
            if (!string.IsNullOrWhiteSpace(html))
            {
                if (html.Length > ClipboardCapturePolicy.MaxHtmlLength)
                {
                    _logger.LogWarning("HTML 片段超过上限（{Length}），已跳过。", html.Length);
                    return null;
                }

                var content = html.Trim();
                return new ClipboardItem
                {
                    Kind = ClipboardKind.Html,
                    TextContent = content,
                    Mime = "text/html",
                    ContentHash = ClipClassifier.ComputeTextHash(ClipboardKind.Html, content),
                    SourceWindow = sourceText,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
            }
        }

        var text = ReadText(data);
        if (text is null)
        {
            return null;
        }

        if (text.Length > ClipboardCapturePolicy.MaxTextLength)
        {
            _logger.LogWarning("文本超过上限（{Length}），已跳过。", text.Length);
            return null;
        }

        var normalized = ClipClassifier.NormalizeText(text);
        if (normalized.Length == 0)
        {
            return null;
        }

        var kind = ClipClassifier.Classify(text, null, null, hasImage: false);
        if (kind == ClipboardKind.Link)
        {
            return new ClipboardItem
            {
                Kind = ClipboardKind.Link,
                TextContent = text.Trim(),
                Url = text.Trim(),
                Mime = "text/uri-list",
                ContentHash = ClipClassifier.ComputeTextHash(ClipboardKind.Link, text.Trim()),
                SourceWindow = sourceText,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }

        return new ClipboardItem
        {
            Kind = ClipboardKind.Text,
            TextContent = text,
            Mime = "text/plain; charset=utf-8",
            NormalizedText = normalized,
            ContentHash = ClipClassifier.ComputeTextHash(ClipboardKind.Text, normalized),
            SourceWindow = sourceText,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private ClipboardItem BuildImageItem(Image image, string? sourceText, DateTimeOffset now)
    {
        Directory.CreateDirectory(AppPaths.ClipboardImagesDirectory);
        var originalPath = ImageFilePath("clip");
        image.Save(originalPath, ImageFormat.Png);

        var thumbPath = ImageFilePath("thumb");
        SaveThumbnail(image, thumbPath);

        string hash;
        using (var stream = File.OpenRead(originalPath))
        {
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        return new ClipboardItem
        {
            Kind = ClipboardKind.Image,
            FilePath = originalPath,
            ThumbPath = thumbPath,
            Mime = "image/png",
            Width = image.Width,
            Height = image.Height,
            ContentHash = hash,
            SourceWindow = sourceText,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static ClipboardItem BuildFileItem(
        IReadOnlyList<string> files,
        string? sourceText,
        DateTimeOffset now)
    {
        var canonical = string.Join("\n", files);
        return new ClipboardItem
        {
            Kind = ClipboardKind.File,
            FilePath = JsonSerializer.Serialize(files),
            Mime = "application/x-file-list",
            ContentHash = ClipClassifier.ComputeTextHash(ClipboardKind.File, canonical),
            SourceWindow = sourceText,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static string ImageFilePath(string prefix) =>
        Path.Combine(
            AppPaths.ClipboardImagesDirectory,
            $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..8]}.png");

    private static void SaveThumbnail(Image image, string path)
    {
        var maxEdge = Math.Max(image.Width, image.Height);
        if (maxEdge <= ClipboardCapturePolicy.ThumbnailMaxEdge)
        {
            image.Save(path, ImageFormat.Png);
            return;
        }

        var scale = ClipboardCapturePolicy.ThumbnailMaxEdge / (double)maxEdge;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var thumb = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(thumb);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(image, 0, 0, width, height);
        thumb.Save(path, ImageFormat.Png);
    }

    /// <summary>内容级敏感检测：文本、链接与 HTML（取清洗后纯文本）统一判定。</summary>
    private bool IsSensitiveContent(ClipboardItem item)
    {
        var text = item.Kind switch
        {
            ClipboardKind.Text or ClipboardKind.Link => item.TextContent,
            ClipboardKind.Html => ClipboardPlainText.Of(item),
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(text)
            && ClipboardPrivacyFilter.IsSensitiveText(ClipClassifier.NormalizeText(text));
    }

    private static bool IsOversizedImage(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(DataFormats.Dib, autoConvert: false))
            {
                return false;
            }

            return data.GetData(DataFormats.Dib, autoConvert: false) is MemoryStream stream
                && stream.Length > ClipboardCapturePolicy.MaxImageBytes;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static string? ReadText(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            if (data.GetData(DataFormats.UnicodeText, autoConvert: true) is string unicode)
            {
                return unicode;
            }
        }

        if (data.GetDataPresent(DataFormats.Text)
            && data.GetData(DataFormats.Text, autoConvert: true) is string plain)
        {
            return plain;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadFileDrop(IDataObject data)
    {
        var value = data.GetData(DataFormats.FileDrop, autoConvert: true);
        if (value is string[] paths)
        {
            return paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        }

        if (value is System.Collections.Specialized.StringCollection collection)
        {
            return collection.Cast<string>().Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        }

        return Array.Empty<string>();
    }

    private bool IsBlockedByPolicy(IDataObject data, ClipboardSource source)
    {
        // P3-09：保险库窗口与本应用自身窗口默认不采集（密码不落历史）。
        if (ClipboardPrivacyFilter.IsPasswordManagerProcess(source.ProcessName)
            || ClipboardPrivacyFilter.IsVaultWindow(source.WindowTitle)
            || IsOwnProcess(source.ProcessName))
        {
            return true;
        }

        string[] formats;
        try
        {
            formats = data.GetFormats(autoConvert: false);
        }
        catch (ExternalException ex)
        {
            // 读不出格式说明数据对象异常：保守拒绝，否则历史/云同步排除标记会被绕过。
            _logger.LogWarning(ex, "剪贴板格式读取失败，已保守跳过本次采集。");
            return true;
        }

        if (formats.Any(ClipboardPrivacyFilter.IsSecretFormat))
        {
            return true;
        }

        // 写入方显式禁止进历史或上云时，两者都必须尊重。
        return IsZeroDword(data, ClipboardPrivacyFilter.CanIncludeInHistoryFormat)
            || IsZeroDword(data, ClipboardPrivacyFilter.CanUploadToCloudFormat);
    }

    /// <summary>来源进程是否为本应用自身（含开发态的可执行名差异）。</summary>
    private static bool IsOwnProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var name = processName.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }

        return string.Equals(name, OwnProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZeroDword(IDataObject data, string format)
    {
        try
        {
            if (!data.GetDataPresent(format, autoConvert: false))
            {
                return false;
            }

            var value = data.GetData(format, autoConvert: false);
            return value switch
            {
                int number => number == 0,
                long number => number == 0,
                MemoryStream stream => IsZeroDword(stream),
                byte[] bytes => bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == 0,
                _ => false,
            };
        }
        catch (ExternalException)
        {
            // 读不出排除标记时按“已排除”处理，避免隐私开关失效。
            return true;
        }
    }

    private static bool IsZeroDword(MemoryStream stream)
    {
        var buffer = new byte[4];
        stream.Position = 0;
        return stream.Read(buffer, 0, 4) == 4 && BitConverter.ToInt32(buffer, 0) == 0;
    }

    private ClipboardSource? CaptureSource()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var title = new StringBuilder(256);
            NativeMethods.GetWindowText(handle, title, title.Capacity);

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            string? processName = null;
            if (processId != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName + ".exe";
                }
                catch (ArgumentException)
                {
                    // 进程已退出。
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出。
                }
            }

            return new ClipboardSource(title.ToString(), processName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取剪贴板来源窗口失败，已保守跳过本次采集。");
            return null;
        }
    }

    private static string? DescribeSource(ClipboardSource source)
    {
        var parts = new[] { source.WindowTitle, source.ProcessName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();

        if (parts.Length == 0)
        {
            return null;
        }

        var text = string.Join(" · ", parts);
        return text.Length > 200 ? text[..200] : text;
    }

    private void OnClipboardUpdate()
    {
        var source = CaptureSource();
        lock (_sourceGate)
        {
            _pendingSource = source;
        }

        _signal.Set();
    }

    private ClipboardSource? TakePendingSource()
    {
        lock (_sourceGate)
        {
            var source = _pendingSource;
            _pendingSource = null;
            return source;
        }
    }

    private void UnregisterListener()
    {
        if (!_listenerRegistered || _window is null)
        {
            _listenerRegistered = false;
            return;
        }

        try
        {
            NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "移除剪贴板监听失败。");
        }

        _listenerRegistered = false;
    }

    private void RefreshSettings()
    {
        _captureEnabled = _settings.Get(
            PreferenceKeys.ClipboardCaptureEnabled,
            PreferenceDefaults.ClipboardCaptureEnabled);
        _paused = _settings.Get(
            PreferenceKeys.ClipboardPaused,
            PreferenceDefaults.ClipboardPaused);
        _mergeDifferentSources = _settings.Get(
            PreferenceKeys.ClipboardMergeSources,
            PreferenceDefaults.ClipboardMergeSources);
    }

    private void ApplySetting(SettingChanged change)
    {
        switch (change.Key)
        {
            case PreferenceKeys.ClipboardCaptureEnabled:
                _captureEnabled = change.Value is bool enabled
                    ? enabled
                    : _settings.Get(
                        PreferenceKeys.ClipboardCaptureEnabled,
                        PreferenceDefaults.ClipboardCaptureEnabled);
                _logger.LogInformation("剪贴板采集开关切换为 {Enabled}。", _captureEnabled);
                break;
            case PreferenceKeys.ClipboardPaused:
                _paused = change.Value is bool paused
                    ? paused
                    : _settings.Get(
                        PreferenceKeys.ClipboardPaused,
                        PreferenceDefaults.ClipboardPaused);
                _logger.LogInformation("剪贴板暂停状态切换为 {Paused}。", _paused);
                break;
            case PreferenceKeys.ClipboardMergeSources:
                _mergeDifferentSources = change.Value is bool merge
                    ? merge
                    : _settings.Get(
                        PreferenceKeys.ClipboardMergeSources,
                        PreferenceDefaults.ClipboardMergeSources);
                _logger.LogInformation("剪贴板多来源合并切换为 {Merge}。", _mergeDifferentSources);
                break;
            default:
                break;
        }
    }

    private sealed class SettingsObserver(Action<SettingChanged> handler) : IObserver<SettingChanged>
    {
        public void OnNext(SettingChanged value) => handler(value);

        public void OnError(Exception error)
        {
            // 设置流异常不影响采集。
        }

        public void OnCompleted()
        {
            // 无生命周期结束语义。
        }
    }

    private sealed class ClipboardMessageWindow : NativeWindow
    {
        private readonly Action _onUpdate;

        public ClipboardMessageWindow(Action onUpdate)
        {
            _onUpdate = onUpdate;
            CreateHandle(new CreateParams
            {
                Caption = "Lumina.ClipboardListener",
                Parent = HwndMessage,
            });
        }

        public void Destroy() => DestroyHandle();

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmClipboardUpdate)
            {
                _onUpdate();
                return;
            }

            base.WndProc(ref m);
        }
    }

    private readonly record struct ClipboardSource(string? WindowTitle, string? ProcessName)
    {
        public static ClipboardSource Empty { get; } = new(null, null);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    }
}
