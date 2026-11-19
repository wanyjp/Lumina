using System.Threading;
using Lumina.Services.Runtime;

namespace Lumina.Tests;

/// <summary>单实例激活信号的可执行验证。</summary>
public class SingleInstanceActivationTests
{
    [Fact]
    public void Activation_signal_invokes_listener()
    {
        var name = @"Local\Lumina.Tests.Activate." + Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim();
        using var listener = new SingleInstanceActivation(() => activated.Set(), name);

        Assert.True(SingleInstanceActivation.TrySignal(name));
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Signal_returns_false_when_no_listener_exists()
    {
        var name = @"Local\Lumina.Tests.Activate." + Guid.NewGuid().ToString("N");
        Assert.False(SingleInstanceActivation.TrySignal(name));
    }

    [Fact]
    public void Signal_returns_false_and_reports_error_when_no_listener_exists()
    {
        var name = @"Local\Lumina.Tests.Activate." + Guid.NewGuid().ToString("N");
        Exception? error = null;

        Assert.False(SingleInstanceActivation.TrySignal(out error, name));
        Assert.NotNull(error);
        Assert.IsType<WaitHandleCannotBeOpenedException>(error);
    }

    [Fact]
    public void Guard_success_reports_no_error_and_no_abandoned_recovery()
    {
        var name = @"Local\Lumina.Tests.Success." + Guid.NewGuid().ToString("N");
        using var guard = new SingleInstanceGuard(name);

        Assert.True(guard.TryAcquire());
        Assert.Null(guard.LastError);
        Assert.False(guard.RecoveredFromAbandoned);
    }

    [Fact]
    public void Activation_listener_coexists_with_owned_mutex_and_receives_signal()
    {
        var name = @"Local\Lumina.Tests.Coexist." + Guid.NewGuid().ToString("N");

        using var guard = new SingleInstanceGuard(name);
        Assert.True(guard.TryAcquire());

        using var activated = new ManualResetEventSlim();
        using var listener = new SingleInstanceActivation(() => activated.Set(), name);

        Assert.True(SingleInstanceActivation.TrySignal(name));
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Instance_name_override_applies_only_without_explicit_name()
    {
        var original = Environment.GetEnvironmentVariable(
            SingleInstanceGuard.InstanceNameOverrideVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                SingleInstanceGuard.InstanceNameOverrideVariable,
                "Lumina.Benchmark.Test");

            Assert.Equal(
                @"Local\Lumina.Benchmark.Test",
                SingleInstanceGuard.ResolveName());
            Assert.Equal(
                @"Local\Lumina.Explicit",
                SingleInstanceGuard.ResolveName(@"Local\Lumina.Explicit"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                SingleInstanceGuard.InstanceNameOverrideVariable,
                original);
        }
    }
}
