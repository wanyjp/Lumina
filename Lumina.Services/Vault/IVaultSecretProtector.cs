namespace Lumina.Services.Vault;

/// <summary>
/// 保险库附加保护：把主密码校验哈希再包一层本机账户绑定（Windows 实现用 DPAPI）。
/// 仅作附加保护——即使数据库被拷走，也要在原始账户下才能校验主密码。
/// </summary>
public interface IVaultSecretProtector
{
    bool IsAvailable { get; }

    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}
