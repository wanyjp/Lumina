namespace Lumina.Data.Migrations;

/// <summary>
/// 一次顺序迁移。版本号严格递增，SQL 在单事务内整体执行；
/// 迁移脚本须使用 <c>IF NOT EXISTS</c> 等幂等写法，便于失败后重试。
/// </summary>
public sealed record Migration
{
    public required int Version { get; init; }

    public required string Description { get; init; }

    public required string Sql { get; init; }
}
