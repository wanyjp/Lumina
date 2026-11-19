using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>保险库存储端口：P3-07 新增。</summary>
public interface IVaultStore
{
    VaultMetadata? ReadMetadata();

    void WriteMetadata(VaultMetadata metadata);

    /// <summary>按「收藏优先、更新时间倒序」返回全部条目（含密文，不解密）。</summary>
    IReadOnlyList<VaultEntry> QueryAll();

    VaultEntry? Find(long id);

    long Insert(VaultEntry entry, DateTimeOffset now);

    void Update(VaultEntry entry, DateTimeOffset now);

    /// <summary>修改主密码时在同一事务内批量写回重新加密的条目。</summary>
    void UpdateEncryptedBatch(IReadOnlyList<VaultEntry> entries, DateTimeOffset now);

    void Delete(long id);

    /// <summary>查看/复制密码后更新使用时间。</summary>
    void MarkUsed(long id, DateTimeOffset at);

    void MarkLocked(DateTimeOffset at);
}
