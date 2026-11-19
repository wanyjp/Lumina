using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.SystemAdapters.Clipboard;

/// <summary>
/// 剪贴板回贴：把历史条目按原生格式写回系统剪贴板。
/// 写入前通知监听器忽略本次变更，避免自己写的内容被再次采集；
/// 写入失败返回 false，由调用方给出降级提示，不静默失败。
/// </summary>
public sealed class WindowsClipboardWriter : IClipboardWriter
{
    private readonly IClipboardMonitor _monitor;
    private readonly ISettingsStore _settings;

    /// <summary>
    /// 保留最近写入的位图引用：部分应用在粘贴时才延迟取图，
    /// 提前释放会让粘贴结果变成空白。多槽保留避免连续复制图片时把上一张提前释放。
    /// </summary>
    private const int RetainedImageLimit = 4;

    private static readonly Queue<Bitmap> RetainedImages = new();

    public WindowsClipboardWriter(IClipboardMonitor monitor, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settings);
        _monitor = monitor;
        _settings = settings;
    }

    public bool TryWrite(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind switch
        {
            ClipboardKind.Image => WriteImage(item.FilePath),
            ClipboardKind.File => WriteFileList(item),
            ClipboardKind.Html => WriteHtml(item.TextContent),
            _ => TryWritePlainText(item.TextContent ?? string.Empty),
        };
    }

    public bool TryWritePlainText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData(DataFormats.Text, text);
        return SetData(data);
    }

    private bool WriteHtml(string? html)
    {
        var model = ClipboardHtmlSanitizer.Sanitize(html);
        var safe = ClipboardHtmlSanitizer.ToSafeHtml(model);
        if (safe.Length == 0)
        {
            return false;
        }

        var plain = model.ToPlainText();
        var plainOnly = _settings.Get(
            PreferenceKeys.ClipboardHtmlPlainOnly,
            PreferenceDefaults.ClipboardHtmlPlainOnly);
        if (plainOnly)
        {
            return plain.Length > 0 && TryWritePlainText(plain);
        }

        var data = new DataObject();
        data.SetData(DataFormats.Html, ClipboardHtmlSanitizer.BuildClipboardHtml(safe));

        if (plain.Length > 0)
        {
            data.SetData(DataFormats.UnicodeText, plain);
            data.SetData(DataFormats.Text, plain);
        }

        return SetData(data);
    }

    private bool WriteImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        Bitmap copy;
        try
        {
            using var file = new Bitmap(path);
            copy = new Bitmap(file.Width, file.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(copy);
            graphics.DrawImage(file, 0, 0, file.Width, file.Height);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or ExternalException)
        {
            return false;
        }

        var data = new DataObject();
        data.SetImage(copy);

        if (!SetData(data))
        {
            copy.Dispose();
            return false;
        }

        RetainImage(copy);
        return true;
    }

    private static void RetainImage(Bitmap image)
    {
        lock (RetainedImages)
        {
            RetainedImages.Enqueue(image);
            while (RetainedImages.Count > RetainedImageLimit)
            {
                RetainedImages.Dequeue().Dispose();
            }
        }
    }

    private bool WriteFileList(ClipboardItem item)
    {
        var paths = ClipboardPlainText.FilePaths(item);
        if (paths.Count == 0)
        {
            return false;
        }

        var collection = new StringCollection();
        collection.AddRange(paths.Where(File.Exists).ToArray());
        if (collection.Count == 0)
        {
            return false;
        }

        var data = new DataObject();
        data.SetFileDropList(collection);
        return SetData(data);
    }

    private bool SetData(DataObject data)
    {
        // 剪贴板 API 要求 STA；非 UI 线程直接返回失败，由调用方降级提示。
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return false;
        }

        _monitor.SuppressCapture();

        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            // 剪贴板被其他进程占用：返回失败，不重试静默丢弃。
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }
}
