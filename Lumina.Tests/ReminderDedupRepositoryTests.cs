using Lumina.Data.Reminders;

namespace Lumina.Tests;

/// <summary>P1-12 提醒去重仓储：命中、覆盖写入、按时间淘汰。</summary>
public class ReminderDedupRepositoryTests
{
    [Fact]
    public void Dedup_roundtrips_and_updates_same_key()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ReminderDedupRepository(temp.Database);
        var now = DateTimeOffset.Now;

        Assert.False(repository.Contains("sched:1:2026-11-06:m15"));

        repository.Mark("sched:1:2026-11-06:m15", now);
        Assert.True(repository.Contains("sched:1:2026-11-06:m15"));
        Assert.Equal(1, repository.Count());

        repository.Mark("sched:1:2026-11-06:m15", now.AddMinutes(1));
        Assert.Equal(1, repository.Count());
    }

    [Fact]
    public void Dedup_trims_oldest_to_target()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ReminderDedupRepository(temp.Database);
        var baseTime = DateTimeOffset.Now.AddMinutes(-600);

        for (var index = 0; index < 600; index++)
        {
            repository.Mark($"key-{index:D3}", baseTime.AddSeconds(index));
        }

        Assert.Equal(600, repository.Count());
        Assert.True(repository.Contains("key-000"));

        var deleted = repository.TrimTo(500);

        Assert.Equal(100, deleted);
        Assert.Equal(500, repository.Count());
        Assert.False(repository.Contains("key-000"));
        Assert.True(repository.Contains("key-599"));
    }
}
