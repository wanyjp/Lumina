using System.Diagnostics;
using Lumina.Domain.Ocr;
using Lumina.Services.Contracts;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Lumina.SystemAdapters.Ocr;

/// <summary>
/// Windows 本地 OCR（Windows.Media.Ocr）。构造时完成能力探测：
/// 系统没有可用语言包时 <see cref="Available"/> 为 false，由 UI 置灰入口并说明原因，而不是崩溃。
/// 图片只在内存与本地识别引擎之间传递，不发起任何网络请求。
/// </summary>
public sealed class WindowsOcrService : IOcrService
{
    private readonly ILogger<WindowsOcrService>? _logger;
    private readonly OcrEngine? _engine;

    public WindowsOcrService(ILogger<WindowsOcrService>? logger = null)
    {
        _logger = logger;
        _engine = CreateEngine(logger);
    }

    public bool Available => _engine is not null;

    public async Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var engine = _engine ?? throw new InvalidOperationException(
            "当前系统缺少 OCR 语言包。可在 Windows 设置 → 时间和语言 → 语言中添加语言包后重试。");

        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

        var text = OcrTextComposer.Compose(result.Lines.Select(line => line.Text));
        stopwatch.Stop();

        _logger?.LogInformation(
            "OCR 识别完成：{Chars} 字，{ElapsedMs} ms，语言 {Language}",
            text.Length,
            stopwatch.ElapsedMilliseconds,
            engine.RecognizerLanguage?.LanguageTag ?? "unknown");

        return text;
    }

    private static OcrEngine? CreateEngine(ILogger? logger)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is not null)
            {
                return engine;
            }

            var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
            if (language is null)
            {
                logger?.LogWarning("OCR 不可用：系统没有可用的 OCR 语言包。");
                return null;
            }

            engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is null)
            {
                logger?.LogWarning("OCR 不可用：语言 {Language} 无法创建识别引擎。", language.LanguageTag);
            }

            return engine;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "OCR 引擎初始化失败。");
            return null;
        }
    }
}
