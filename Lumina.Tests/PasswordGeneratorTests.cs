using Lumina.Services.Vault;

namespace Lumina.Tests;

/// <summary>P3-08 生成器：长度、字符类别、强随机与参数校验。</summary>
public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Generate_honors_requested_length(int length)
    {
        var password = PasswordGenerator.Generate(new PasswordGeneratorOptions { Length = length });

        Assert.Equal(length, password.Length);
    }

    [Fact]
    public void Generate_includes_every_enabled_class()
    {
        var password = PasswordGenerator.Generate(new PasswordGeneratorOptions { Length = 16 });

        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, ch => !char.IsLetterOrDigit(ch));
    }

    [Fact]
    public void Generate_excludes_disabled_classes()
    {
        var password = PasswordGenerator.Generate(new PasswordGeneratorOptions
        {
            Length = 32,
            Uppercase = false,
            Digits = false,
            Symbols = false,
        });

        Assert.All(password, ch => Assert.True(char.IsLower(ch)));
    }

    [Fact]
    public void Generate_returns_different_values_for_each_call()
    {
        var values = Enumerable.Range(0, 8)
            .Select(_ => PasswordGenerator.Generate(new PasswordGeneratorOptions()))
            .ToArray();

        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(65)]
    public void Generate_rejects_out_of_range_length(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PasswordGenerator.Generate(new PasswordGeneratorOptions { Length = length }));
    }

    [Fact]
    public void Generate_requires_at_least_one_class()
    {
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.Generate(new PasswordGeneratorOptions
            {
                Lowercase = false,
                Uppercase = false,
                Digits = false,
                Symbols = false,
            }));
    }
}
