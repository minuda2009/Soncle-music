// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Output;

/// <summary>
/// One output session's claim on the render callback it shares with later sessions. A sound
/// device whose <c>Stop</c> returns before its last block is rendered (WASAPI, AudioTrack: the
/// faded last block plays out on the device thread) can be started again while the old device
/// thread still has one block to pull; the engine behind the callback is single-threaded, so the
/// new session closes the old one's gate first. After <see cref="Close"/> returns true the old
/// session never enters the callback again and is not inside it.
/// </summary>
/// <remarks>
/// <see cref="TryEnter"/> and <see cref="Exit"/> run on the device thread: no lock, no
/// allocation, no event, only interlocked flags (Dekker style: each side writes its own flag with
/// a full fence, then reads the other's). <see cref="Close"/> spins briefly on the caller's thread.
/// </remarks>
public sealed class RenderGate
{
    private int _inside;
    private int _closed;

    /// <summary>Whether <see cref="Close"/> was called.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Device thread: true when the callback may be called (then call <see cref="Exit"/> after it); false once closed.</summary>
    public bool TryEnter()
    {
        Interlocked.Exchange(ref _inside, 1);
        if (Volatile.Read(ref _closed) == 0) return true;
        Volatile.Write(ref _inside, 0);
        return false;
    }

    /// <summary>Device thread: the callback returned (or threw).</summary>
    public void Exit() => Volatile.Write(ref _inside, 0);

    /// <summary>
    /// Shuts the gate and waits up to <paramref name="timeoutMs"/> for a callback in progress to
    /// return. True when the session is out of the callback for good; false on time-out (the gate
    /// stays shut, so the session leaves the callback with its current call).
    /// </summary>
    public bool Close(int timeoutMs)
    {
        Interlocked.Exchange(ref _closed, 1);
        if (Volatile.Read(ref _inside) == 0) return true;
        var start = Environment.TickCount64;
        var spin = new SpinWait();
        while (Volatile.Read(ref _inside) != 0)
        {
            if (Environment.TickCount64 - start > timeoutMs) return false;
            spin.SpinOnce();
        }
        return true;
    }
}
