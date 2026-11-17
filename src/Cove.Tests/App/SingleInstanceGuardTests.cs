using Cove.App;

namespace Cove.Tests.App;

public class SingleInstanceGuardTests
{
    // Generous so a busy CI machine never fails the test; the signal normally arrives in milliseconds.
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    // A fresh id per test, so tests never see each other's kernel objects or a running Cove.
    private static string NewAppId() => $"Cove.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void FirstGuard_IsFirstInstance()
    {
        using var first = new SingleInstanceGuard(NewAppId());

        Assert.True(first.IsFirstInstance);
    }

    [Fact]
    public void SecondGuardWithSameId_IsNotFirstInstance()
    {
        var appId = NewAppId();
        using var first = new SingleInstanceGuard(appId);
        using var second = new SingleInstanceGuard(appId);

        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void Signal_RaisesShowMenuRequestedOnFirstInstance()
    {
        var appId = NewAppId();
        using var first = new SingleInstanceGuard(appId);
        using var received = new ManualResetEventSlim();
        first.ShowMenuRequested += (_, _) => received.Set();
        first.StartListening();

        using var second = new SingleInstanceGuard(appId);
        second.SignalFirstInstance();

        Assert.True(received.Wait(SignalTimeout));
    }

    [Fact]
    public void SignalSentBeforeListening_IsStillDelivered()
    {
        // A second launch can happen while the first copy is still starting up.
        var appId = NewAppId();
        using var first = new SingleInstanceGuard(appId);
        using var second = new SingleInstanceGuard(appId);
        second.SignalFirstInstance();

        using var received = new ManualResetEventSlim();
        first.ShowMenuRequested += (_, _) => received.Set();
        first.StartListening();

        Assert.True(received.Wait(SignalTimeout));
    }

    [Fact]
    public void AfterFirstIsDisposed_NextGuardIsFirstInstance()
    {
        var appId = NewAppId();
        var first = new SingleInstanceGuard(appId);
        first.Dispose();

        using var next = new SingleInstanceGuard(appId);

        Assert.True(next.IsFirstInstance);
    }

    [Fact]
    public void SecondInstance_CannotStartListening()
    {
        var appId = NewAppId();
        using var first = new SingleInstanceGuard(appId);
        using var second = new SingleInstanceGuard(appId);

        Assert.Throws<InvalidOperationException>(() => second.StartListening());
    }
}
