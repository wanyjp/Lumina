namespace Lumina.Domain.Models;

/// <summary>世界时钟城市；预设城市与自建城市共用同一张表与同一扁平列表。</summary>
public sealed record WorldCity(
    long Id,
    string IanaId,
    string DisplayName,
    int SortOrder);
