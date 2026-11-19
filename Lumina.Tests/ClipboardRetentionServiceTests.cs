using Lumina.Data.Clipboard;
using Lumina.Data.Settings;
using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P2-02 保留策略编排：上限钳制、持久化、立即清理与固定豁免。</summary>
public class ClipboardRetentionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void SetLimit_clamps_to_documented_range_and_persists()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (service, _, _) = Create(temp);

        Assert.Equal(EvictionPolicy.DefaultLimit, service.Limit);

        var cleaned = service.SetLimit(-1);
        Assert.Equal(0, cleaned);
        Assert.Equal(0, service.Limit);

        service.SetLimit(10_000);
        Assert.Equal(EvictionPolicy.MaxLimit, service.Limit);

        var reloaded = new ClipboardRetentionService(
            new ClipboardRepository(temp.Database),
            new SettingsStore(new SqliteSettingsBackend(temp.Database)));
        Assert.Equal(EvictionPolicy.MaxLimit, reloaded.Limit);
    }

    [Fact]
    public void SetLimit_immediately_trims_oldest_non_pinned_and_reports_count()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (service, repository, settings) = Create(temp);

        for (var index = 0; index < 5; index++)
        {
            repository.Save(
                Item(index),
                Now.AddSeconds(index),
                TimeSpan.FromSeconds(5),
                retentionLimit: 0);
        }

        var cleaned = service.SetLimit(2);

        Assert.Equal(3, cleaned);
        var recent = repository.QueryRecent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal("内容-4", recent[0].NormalizedText);
        Assert.Equal("内容-3", recent[1].NormalizedText);
        Assert.Equal(2, settings.Get(PreferenceKeys.ClipboardRetentionLimit, 0));
    }

    [Fact]
    public void Pinned_item_is_never_removed_by_service_trim()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (service, repository, _) = Create(temp);

        var first = repository.Save(Item(0), Now, TimeSpan.FromSeconds(5), retentionLimit: 0);
        Assert.True(service.Pin(first.Item.Id, Now.AddSeconds(10)));

        for (var index = 1; index < 4; index++)
        {
            repository.Save(
                Item(index),
                Now.AddSeconds(index),
                TimeSpan.FromSeconds(5),
                retentionLimit: 0);
        }

        Assert.Equal(2, service.SetLimit(1));

        var recent = repository.QueryRecent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(first.Item.Id, recent[0].Id);
        Assert.True(recent[0].Pinned);
        Assert.Equal("内容-3", recent[1].NormalizedText);
    }

    private static (ClipboardRetentionService Service, ClipboardRepository Repository, SettingsStore Settings)
        Create(TemporaryDataRoot temp)
    {
        var repository = new ClipboardRepository(temp.Database);
        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        return (new ClipboardRetentionService(repository, settings), repository, settings);
    }

    private static ClipboardItem Item(int index) => new()
    {
        Kind = ClipboardKind.Text,
        TextContent = $"内容-{index}",
        NormalizedText = $"内容-{index}",
        ContentHash = $"hash-{index}",
        CreatedAt = Now.AddSeconds(index),
        UpdatedAt = Now.AddSeconds(index),
    };
}
