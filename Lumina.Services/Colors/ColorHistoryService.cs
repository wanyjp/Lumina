using System.Text.Json;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Colors;

/// <summary>
/// 取色历史色板：最近使用在前、按 HEX 去重、上限 <see cref="Limit"/> 条。
/// 以 JSON 数组存放在偏好键 <see cref="PreferenceKeys.ColorHistory"/> 中，随设置后端一起持久化。
/// </summary>
public sealed class ColorHistoryService
{
    public const int Limit = 12;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISettingsStore _settings;

    public ColorHistoryService(ISettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<ColorSample> Load()
    {
        var json = _settings.Get(PreferenceKeys.ColorHistory, PreferenceDefaults.ColorHistory);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredColor>>(json, JsonOptions);
            if (stored is null)
            {
                return [];
            }

            return stored
                .Take(Limit)
                .Select(item => new ColorSample(item.R, item.G, item.B, 0, 0))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>记录一次取色：同色去重并置顶，超过上限丢弃最旧，返回最新列表。</summary>
    public async Task<IReadOnlyList<ColorSample>> AddAsync(
        ColorSample sample,
        CancellationToken cancellationToken = default)
    {
        var colors = Load().ToList();
        colors.RemoveAll(item => string.Equals(item.Hex, sample.Hex, StringComparison.OrdinalIgnoreCase));
        colors.Insert(0, sample with { X = 0, Y = 0 });

        if (colors.Count > Limit)
        {
            colors.RemoveRange(Limit, colors.Count - Limit);
        }

        Save(colors);
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
        return colors;
    }

    public void Save(IReadOnlyList<ColorSample> colors)
    {
        var stored = colors
            .Take(Limit)
            .Select(item => new StoredColor(item.R, item.G, item.B))
            .ToList();

        _settings.Set(PreferenceKeys.ColorHistory, JsonSerializer.Serialize(stored, JsonOptions));
    }

    internal sealed record StoredColor(byte R, byte G, byte B);
}
