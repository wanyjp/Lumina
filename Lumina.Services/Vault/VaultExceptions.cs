namespace Lumina.Services.Vault;

/// <summary>保险库未解锁时执行敏感操作。</summary>
public sealed class VaultLockedException : InvalidOperationException
{
    public VaultLockedException()
        : base("保险库已锁定，请先输入主密码解锁。")
    {
    }

    public VaultLockedException(string message)
        : base(message)
    {
    }
}

/// <summary>保险库元数据损坏、本机账户不匹配等无法继续解锁的情况。</summary>
public sealed class VaultException : Exception
{
    public VaultException(string message)
        : base(message)
    {
    }

    public VaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
