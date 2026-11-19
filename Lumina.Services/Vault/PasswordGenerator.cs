using System.Security.Cryptography;

namespace Lumina.Services.Vault;

/// <summary>密码生成选项；小写字母始终启用，保证至少有一类字符可选。</summary>
public sealed record PasswordGeneratorOptions
{
    public int Length { get; init; } = 16;

    public bool Lowercase { get; init; } = true;

    public bool Uppercase { get; init; } = true;

    public bool Digits { get; init; } = true;

    public bool Symbols { get; init; } = true;
}

/// <summary>
/// P3-08 密码生成器：<see cref="RandomNumberGenerator"/> 强随机，长度 8–64，
/// 每个启用的字符类别至少出现一次，再整体 Fisher–Yates 洗牌。
/// </summary>
public static class PasswordGenerator
{
    public const int MinLength = 8;
    public const int MaxLength = 64;

    private const string LowerPool = "abcdefghijkmnopqrstuvwxyz";
    private const string UpperPool = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string DigitPool = "23456789";
    private const string SymbolPool = "!@#$%^&*()-_=+[]{}:,.?";

    public static string Generate(PasswordGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Length is < MinLength or > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"密码长度必须在 {MinLength}–{MaxLength} 之间。");
        }

        var pools = new List<string>(4);
        if (options.Lowercase)
        {
            pools.Add(LowerPool);
        }

        if (options.Uppercase)
        {
            pools.Add(UpperPool);
        }

        if (options.Digits)
        {
            pools.Add(DigitPool);
        }

        if (options.Symbols)
        {
            pools.Add(SymbolPool);
        }

        if (pools.Count == 0)
        {
            throw new ArgumentException("至少需要启用一类字符。", nameof(options));
        }

        var all = string.Concat(pools);
        var characters = new char[options.Length];
        var index = 0;

        // 先保证每个启用的类别至少一个字符，再随机补齐。
        foreach (var pool in pools)
        {
            characters[index++] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }

        while (index < characters.Length)
        {
            characters[index++] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        Shuffle(characters);
        return new string(characters);
    }

    private static void Shuffle(Span<char> characters)
    {
        for (var i = characters.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
    }
}
