using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Noise;

/// <summary>与音频资源目录中的父文件夹一一对应的分类定义。</summary>
public sealed record NoiseCategoryDefinition(string Id, string FolderName, string DisplayName);

/// <summary>
/// 父类别清单：严格对应 `app_audio` 下的 8 个父文件夹，顺序按《音源清单》的类别分布表固定，
/// 不做合并、不新增、不调整层级。
/// </summary>
public static class NoiseCategories
{
    public const string Rain = "rain";
    public const string Water = "water";
    public const string Wind = "wind";
    public const string Fire = "fire";
    public const string ForestNight = "forest-night";
    public const string Animal = "animal";
    public const string CityTraffic = "city-traffic";
    public const string OutdoorSteps = "outdoor-steps";

    public static IReadOnlyList<NoiseCategoryDefinition> All { get; } = new NoiseCategoryDefinition[]
    {
        new(Rain, "雨", "雨"),
        new(Water, "水流", "水流"),
        new(Wind, "风", "风"),
        new(Fire, "火焰", "火焰"),
        new(ForestNight, "森林与夜", "森林与夜"),
        new(Animal, "动物", "动物"),
        new(CityTraffic, "城市与交通", "城市与交通"),
        new(OutdoorSteps, "户外脚步", "户外脚步"),
    };
}

/// <summary>
/// 音频库：扫描 8 个父类别目录下的 WAV/MP3/OGG 文件，生成分类与声源清单。
/// 资源根目录按“环境变量 → 用户数据目录 → 应用目录 → 开发工作区”的顺序解析。
/// </summary>
public sealed class NoiseLibrary
{
    public const string RootOverrideVariable = "LUMINA_AUDIO_ROOT";
    private static readonly string[] SupportedExtensions = [".wav", ".mp3", ".ogg"];

    private readonly IReadOnlyList<NoiseCategory> _categories;

    public NoiseLibrary(string? root, IReadOnlyList<NoiseCategory> categories)
    {
        Root = root;
        _categories = categories;
    }

    /// <summary>资源根目录；未找到时为 null。</summary>
    public string? Root { get; }

    public IReadOnlyList<NoiseCategory> Categories => _categories;

    public IReadOnlyList<SoundPreset> Sounds => _categories.SelectMany(category => category.Sounds).ToArray();

    /// <summary>按资源目录中的父文件夹扫描；缺失的目录保留为空分类，不改结构。</summary>
    public static NoiseLibrary Scan(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var categories = new List<NoiseCategory>(NoiseCategories.All.Count);

        foreach (var definition in NoiseCategories.All)
        {
            var folder = Path.Combine(root, definition.FolderName);

            if (!Directory.Exists(folder))
            {
                categories.Add(new NoiseCategory(definition.Id, definition.DisplayName, []));
                continue;
            }

            var sounds = Directory.EnumerateFiles(folder)
                .Where(file => SupportedExtensions.Contains(
                    Path.GetExtension(file),
                    StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.CurrentCulture)
                .Select(file => new SoundPreset(
                    Id: $"{definition.Id}/{Path.GetFileNameWithoutExtension(file)}",
                    DisplayName: Path.GetFileNameWithoutExtension(file),
                    Volume: 0.4,
                    CategoryId: definition.Id,
                    RelativePath: $"{definition.FolderName}/{Path.GetFileName(file)}"))
                .ToArray();

            categories.Add(new NoiseCategory(definition.Id, definition.DisplayName, sounds));
        }

        return new NoiseLibrary(root, categories);
    }

    /// <summary>未找到资源时返回空分类集合，界面仍按 8 个父类别展示。</summary>
    public static NoiseLibrary Empty() => new(
        null,
        NoiseCategories.All
            .Select(definition => new NoiseCategory(definition.Id, definition.DisplayName, []))
            .ToArray());

    /// <summary>按优先级解析资源根目录。</summary>
    public static NoiseLibrary Locate()
    {
        var overridden = Environment.GetEnvironmentVariable(RootOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden) && Directory.Exists(overridden))
        {
            return Scan(overridden);
        }

        var userAudio = Path.Combine(AppPaths.Root, "audio");
        if (Directory.Exists(userAudio))
        {
            return Scan(userAudio);
        }

        var appAudio = Path.Combine(AppContext.BaseDirectory, "Audio");
        if (Directory.Exists(appAudio))
        {
            return Scan(appAudio);
        }

        // 开发工作区：从输出目录向上查找，最多 6 层。
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "app_audio");
            if (Directory.Exists(candidate))
            {
                return Scan(candidate);
            }
        }

        return Empty();
    }

    /// <summary>返回声源的绝对路径；文件不存在时返回 null。</summary>
    public string? AbsolutePath(SoundPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (Root is null || preset.RelativePath is null)
        {
            return null;
        }

        var path = Path.Combine(Root, preset.RelativePath);
        return File.Exists(path) ? path : null;
    }
}
