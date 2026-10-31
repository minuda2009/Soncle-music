// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Audio.Output;

namespace Soncle.Audio.Tests;

/// <summary>
/// The hand-over between an old output session that still has a block to pull and the new one:
/// once the gate is closed the old session must never be inside the shared callback again.
/// </summary>
public class RenderGateTests
{
    [Fact]
    public void AnOpenGateLetsTheSessionInAndAClosedOneKeepsItOut()
    {
        var gate = new RenderGate();
        Assert.False(gate.IsClosed);
        Assert.True(gate.TryEnter());
        gate.Exit();
        Assert.True(gate.TryEnter());
        gate.Exit();
        Assert.True(gate.Close(0));
        Assert.True(gate.IsClosed);
        Assert.False(gate.TryEnter());
        Assert.False(gate.TryEnter());
        // closing again is harmless
        Assert.True(gate.Close(0));
    }

    [Fact]
    public void CloseWaitsForACallbackInProgress()
    {
        var gate = new RenderGate();
        Assert.True(gate.TryEnter());
        // the "device thread" is inside the callback: the close times out but the gate stays shut
        Assert.False(gate.Close(20));
        Assert.True(gate.IsClosed);
        using var inside = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var device = new Thread(() =>
        {
            inside.Set();
            release.Wait();
            gate.Exit();
        });
        device.Start();
        inside.Wait();
        var closer = Task.Run(() => gate.Close(5_000));
        Thread.Sleep(30);
        Assert.False(closer.IsCompleted);
        release.Set();
        Assert.True(closer.Wait(5_000));
        Assert.True(closer.Result);
        device.Join();
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public void NoCallbackRunsAfterCloseReturns()
    {
        // a device thread pulls as fast as it can; after Close returns true it must never be inside
        for (var round = 0; round < 50; round++)
        {
            var gate = new RenderGate();
            var closed = 0;
            var afterClose = 0;
            var entered = 0;
            using var started = new ManualResetEventSlim(false);
            var device = new Thread(() =>
            {
                started.Set();
                while (true)
                {
                    if (!gate.TryEnter()) break;
                    Interlocked.Increment(ref entered);
                    if (Volatile.Read(ref closed) != 0) Interlocked.Increment(ref afterClose);
                    Thread.SpinWait(20);
                    gate.Exit();
                }
            });
            device.Start();
            started.Wait();
            Thread.SpinWait(round * 50);
            Assert.True(gate.Close(5_000));
            Volatile.Write(ref closed, 1);
            device.Join();
            Assert.Equal(0, Volatile.Read(ref afterClose));
        }
    }
}
