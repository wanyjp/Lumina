namespace Lumina.Data.Migrations;

/// <summary>启动初始化结果，供上层写入日志与诊断。</summary>
public sealed record DatabaseInitializationResult(
    int PreviousVersion,
    int CurrentVersion,
    int AppliedCount,
    string? BackupPath,
    bool Fts5Available);
