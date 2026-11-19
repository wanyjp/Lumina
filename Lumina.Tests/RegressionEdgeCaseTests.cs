using System.Text;
using Lumina.Data.Settings;
using Lumina.Data.Todos;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>
/// P4-01 全量回归的异常路径：损坏备份不得破坏现有库、损坏的忽略规则 JSON 回落默认值。
/// </summary>
public class RegressionEdgeCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 13, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Restore_rejects_corrupted_backup_without_touching_database()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var todos = new TodoRepository(temp.Database);
        todos.Insert(Todo("损坏备份回归"), Now);

        var corrupt = Path.Combine(temp.BackupDirectory, "corrupt-backup.db");
        File.WriteAllText(corrupt, "this is not a sqlite database", Encoding.UTF8);

        Assert.Throws<InvalidDataException>(() => temp.Backups.Restore(corrupt));

        Assert.Equal(1, temp.CountRows("todo_item"));
        Assert.Equal("损坏备份回归", Assert.Single(todos.QueryAll()).Text);
        Assert.True(temp.Backups.Validate(corrupt).IsValid == false);
    }

    [Fact]
    public void Corrupted_ignore_rules_fall_back_to_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        settings.Set(PreferenceKeys.ClipboardIgnoreRules, "{ not-valid-json", module: "clipboard");

        var rules = new IgnoreRuleService(settings);

        Assert.Equal(IgnoreRuleService.DefaultRules.ToArray(), rules.Rules.ToArray());
        Assert.True(rules.ShouldIgnoreSource("KeePassXC", "KeePassXC.exe"));
        Assert.False(rules.ShouldIgnoreSource("记事本", "notepad.exe"));
    }

    private static TodoItem Todo(string text) => new()
    {
        Text = text,
        DueDate = new DateOnly(2026, 11, 15),
        CreatedAt = Now,
        UpdatedAt = Now,
    };
}
