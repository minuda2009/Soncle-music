// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Net;
using Soncle.App;

namespace Soncle.App.Tests;

/// <summary>
/// The shared composition root both heads build on. These tests are offline: the HTTP handler
/// answers nothing, so any request fails the test rather than reaching YouTube, and the sound
/// device is a fake.
/// </summary>
public class PlaybackSessionTests
{
    /// <summary>An HTTP handler that must not be called; it throws if anything reaches it.</summary>
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("the session must not touch the network until a search or song");
        }
    }

    private static string NewCache() => Directory.CreateTempSubdirectory("soncle-session-").FullName;

    [Fact]
    public void CreatingTheSessionDoesNotTouchTheNetwork()
    {
        var handler = new NoNetworkHandler();
        var output = new FakeAudioOutput();
        using var session = new PlaybackSession(output, NewCache(), botGuard: null, log: _ => { }, handler: handler);

        // the view models exist and the bar is in its empty state
        Assert.NotNull(session.Search);
        Assert.NotNull(session.Bar);
        Assert.True(session.Bar.IsEmpty);
        Assert.Same(output, session.Output);

        // a tick is safe before anything plays, and it still must not go online
        session.Tick();
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void ADisposedSessionDoesNotDisposeItsOutputTwice()
    {
        var output = new FakeAudioOutput();
        var session = new PlaybackSession(output, NewCache(), log: _ => { }, handler: new NoNetworkHandler());
        session.Dispose();
        // the head may dispose its own output afterwards; the session must be safe to dispose once
        session.Dispose();
        Assert.False(output.IsRunning);
    }

    [Fact]
    public void RedactStripsUrlsSoNoTokenCanReachALog()
    {
        Assert.Equal("resolve failed <url>", PlaybackSession.Redact("resolve failed https://googlevideo.com/videoplayback?pot=SECRET"));
        Assert.Equal("plain message", PlaybackSession.Redact("plain message"));
    }
}
