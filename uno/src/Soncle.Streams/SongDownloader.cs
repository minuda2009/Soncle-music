// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Streams;

/// <summary>What is happening to one song's download.</summary>
public readonly record struct SongProgress(long Have, long Length, bool Done, bool Failed, string? Error)
{
    public double Fraction => Length > 0 ? Math.Clamp((double)Have / Length, 0, 1) : 0;
}

/// <summary>
/// One downloader for both heads. It replaces desktop's <c>mstream:</c> proxy and Android's
/// <c>SoncleStreams.java</c>, keeping the Android 0.1.4–0.1.6 design (built for weak mobile
/// networks):
/// <list type="bullet">
/// <item>a registered song downloads in full to a cache file as fast as the connection allows,
/// and the reader reads from that file (waiting for bytes still on the way);</item>
/// <item>the first piece is small (256 KB) so playback starts almost at once; then ≤ 1 MB
/// requests;</item>
/// <item>a lost connection is retried with back-off for about 10 minutes; what is stored keeps
/// playing;</item>
/// <item>an expired URL (403) is refreshed for the same length only;</item>
/// <item>a seek far past what has arrived is served straight from the network;</item>
/// <item>a song fetched ahead of time waits until the one playing is complete;</item>
/// <item>the cache keeps four songs and is cleared at start.</item>
/// </list>
/// </summary>
public sealed class SongDownloader
{
    private const int Chunk = 1 << 20;          // googlevideo refuses more than 1 MB per request
    private const int FirstPiece = 256 * 1024;  // small, so the player has bytes to decode almost at once
    private const long Ahead = 1_500_000;        // a read this far past the download goes to the network
    private const int Keep = 4;                  // songs kept on disk (current, next, a couple back)
    private const int MaxRetries = 45;           // × back-off ≈ the ~10 minute budget

    private readonly string _cacheDir;
    private readonly IStreamFetcher _fetcher;
    private readonly IStreamRefresher? _refresher;
    private readonly IStreamClock _clock;

    internal readonly object _gate = new();
    private readonly Dictionary<string, Song> _songs = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();   // oldest first, for eviction

    /// <summary>Raised whenever a song's progress changes.</summary>
    public event Action<string, SongProgress>? ProgressChanged;

    public SongDownloader(string cacheDir, IStreamFetcher fetcher, IStreamRefresher? refresher = null, IStreamClock? clock = null)
    {
        _cacheDir = cacheDir;
        _fetcher = fetcher;
        _refresher = refresher;
        _clock = clock ?? RealStreamClock.Instance;
        Directory.CreateDirectory(_cacheDir);
        // nothing survives a process restart, exactly like the Android cache
        foreach (var f in Directory.EnumerateFiles(_cacheDir)) TryDelete(f);
    }

    /// <summary>Registers (or replaces) a song's stream and starts downloading it in the background.</summary>
    public void Register(string songKey, StreamSource source)
    {
        lock (_gate)
        {
            if (_songs.TryGetValue(songKey, out var existing))
            {
                // re-registration (e.g. a refreshed URL) hands the new source to the running download
                existing.Source = source;
                existing.Pump();
                return;
            }
            Evict();
            var song = new Song(songKey, this, source, PathFor(songKey));
            _songs[songKey] = song;
            _order.Add(songKey);
            song.Start();
        }
    }

    /// <summary>Stops a song's download and deletes its cache file.</summary>
    public void Forget(string songKey)
    {
        Song? song;
        lock (_gate)
        {
            if (!_songs.Remove(songKey, out song)) return;
            _order.Remove(songKey);
        }
        song.Cancel();
    }

    /// <summary>Current progress of a song, or null if it was never registered.</summary>
    public SongProgress? Status(string songKey)
    {
        lock (_gate) return _songs.TryGetValue(songKey, out var s) ? s.Snapshot() : null;
    }

    /// <summary>
    /// Opens [<paramref name="position"/>, end] of a song. Reads from the cache while it is being
    /// downloaded, waiting for bytes; a seek far past what has arrived is served from the network.
    /// </summary>
    public Stream OpenRead(string songKey, long position)
    {
        Song? song;
        lock (_gate) _songs.TryGetValue(songKey, out song);
        if (song is null) throw new KeyNotFoundException($"song '{songKey}' is not registered");

        // The reader makes this song the priority, so a prefetch waits for it to finish.
        song.MarkWanted();

        if (!song.Cancelled && song.Failure is null && position <= song.Have + Ahead && position <= song.Length)
            return new CacheReadStream(song, position);
        return new NetworkReadStream(song, position);
    }

    private string PathFor(string songKey)
    {
        var safe = new string(songKey.Select(c => char.IsLetterOrDigit(c) || c is '_' or '~' or '-'
            ? c
            : '_').ToArray());
        return Path.Combine(_cacheDir, safe + ".part");
    }

    private void Evict()
    {
        // keep Keep songs; cancel the oldest first (never the one just registered)
        while (_order.Count >= Keep)
        {
            var oldest = _order[0];
            _order.RemoveAt(0);
            if (_songs.Remove(oldest, out var old)) old.Cancel();
        }
    }

    private bool OtherDownloadRunning(Song me)
    {
        lock (_gate)
        {
            foreach (var s in _songs.Values)
                if (!ReferenceEquals(s, me) && s.Started && !s.Finished) return true;
            return false;
        }
    }

    private void Raise(Song song)
    {
        var p = song.Snapshot();
        ProgressChanged?.Invoke(song.Key, p);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort, like the Java cache */ }
    }

    // ---------------------------------------------------------------------

    /// <summary>One song's download and the state readers wait on.</summary>
    private sealed class Song
    {
        internal readonly SongDownloader _owner;
        private readonly List<TaskCompletionSource> _waiters = new();

        internal long _have;
        internal bool _done;
        internal bool _cancelled;
        internal string? _failure;
        private bool _wanted;
        private bool _started;
        private Task? _task;

        internal Song(string key, SongDownloader owner, StreamSource source, string file)
        {
            Key = key;
            _owner = owner;
            Source = source;
            Length = source.Length;
            File = file;
        }

        internal string Key { get; }
        internal StreamSource Source { get; set; }
        internal long Length { get; }
        internal string File { get; }

        internal long Have { get { lock (_owner._gate) return _have; } }
        internal bool Done { get { lock (_owner._gate) return _done; } }
        internal bool Cancelled { get { lock (_owner._gate) return _cancelled; } }
        internal string? Failure { get { lock (_owner._gate) return _failure; } }
        internal bool Started { get { lock (_owner._gate) return _started; } }
        internal bool Finished { get { lock (_owner._gate) return _done || _cancelled || _failure is not null; } }

        internal SongProgress Snapshot() => new(_have, Length, _done, _failure is not null || _cancelled, _failure);

        internal void Start()
        {
            if (Length <= 0)
            {
                SetFailure("zero length");
                return;
            }
            _task = Task.Run(() => RunAsync());
        }

        internal void MarkWanted() { lock (_owner._gate) { _wanted = true; Pump(); } }
        internal void Cancel()
        {
            lock (_owner._gate) { _cancelled = true; }
            Pump();
            Streaming.TryDelete(File);
        }

        internal void SetFailure(string? message)
        {
            lock (_owner._gate) { _failure = message ?? "error"; }
            Pump();
            _owner.Raise(this);
        }

        /// <summary>Wakes readers and the download loop.</summary>
        internal void Pump()
        {
            lock (_owner._gate)
            {
                foreach (var w in _waiters) w.TrySetResult();
                _waiters.Clear();
            }
        }

        /// <summary>Waits until the song changes (more bytes, done, failed) after <paramref name="seenHave"/>.</summary>
        internal Task WaitForChangeAsync(long seenHave, CancellationToken ct)
        {
            lock (_owner._gate)
            {
                if (_have != seenHave || Finished) return Task.CompletedTask;
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(tcs);
                return tcs.Task.WaitAsync(ct);
            }
        }

        private async Task RunAsync()
        {
            // A song fetched ahead of time waits until the one playing is complete, so it never
            // competes with it on a weak connection.
            while (!Cancelled && !_wanted && _owner.OtherDownloadRunning(this))
                await _owner._clock.DelayAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            lock (_owner._gate) _started = true;

            var pos = 0L;
            var fails = 0;
            try
            {
                using var file = new FileStream(File, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                var buffer = new byte[32 * 1024];
                while (!Cancelled && pos < Length)
                {
                    var source = Source;
                    var to = Math.Min(Length - 1, pos + (pos == 0 ? FirstPiece : Chunk) - 1);
                    try
                    {
                        await using var input = await _owner._fetcher.GetAsync(source.Url, source.Headers, pos, to).ConfigureAwait(false);
                        int n;
                        while (!Cancelled && (n = await input.ReadAsync(buffer.AsMemory(), default).ConfigureAwait(false)) > 0)
                        {
                            file.Seek(pos, SeekOrigin.Begin);
                            await file.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                            pos += n;
                            lock (_owner._gate) _have = pos;
                            fails = 0;
                            Pump();
                            _owner.Raise(this);
                        }
                        file.Flush();
                    }
                    catch (StreamExpiredException)
                    {
                        // ask the app for a fresh URL to the same file; a refresh for a different
                        // length is a hard error (never mix two streams), otherwise retry/back off.
                        var fresh = _owner._refresher is null
                            ? null
                            : await _owner._refresher.RefreshAsync(Key).ConfigureAwait(false);
                        if (fresh is not null && fresh.Length != Length)
                        {
                            SetFailure("stream expired");
                            return;
                        }
                        if (fresh is not null)
                        {
                            Source = fresh;
                            continue;
                        }
                        // no way to refresh: treat it like a transient failure and use the budget
                        if (++fails > MaxRetries)
                        {
                            SetFailure("stream expired");
                            return;
                        }
                        var expiredWait = TimeSpan.FromMilliseconds(Math.Min(15000, 500L << Math.Min(fails, 5)));
                        await _owner._clock.DelayAsync(expiredWait).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) when (ex is IOException or StreamHttpException or HttpRequestException)
                    {
                        // no signal / tunnel / flaky network: back off and keep trying
                        if (++fails > MaxRetries)
                        {
                            SetFailure(ex.Message.Length == 0 ? "network" : ex.Message);
                            return;
                        }
                        var wait = TimeSpan.FromMilliseconds(Math.Min(15000, 500L << Math.Min(fails, 5)));
                        await _owner._clock.DelayAsync(wait).ConfigureAwait(false);
                    }
                }
                lock (_owner._gate) _done = !Cancelled && pos >= Length;
            }
            catch (Exception ex)
            {
                SetFailure("cache: " + ex.Message);
                return;
            }
            finally
            {
                if (Cancelled) Streaming.TryDelete(File);
                Pump();
            }
            _owner.Raise(this);
        }
    }

    // ---------------------------------------------------------------------

    /// <summary>Reads [start, end] from a song's cache file, waiting for bytes that haven't arrived yet.</summary>
    private sealed class CacheReadStream : Stream
    {
        private readonly Song _song;
        private readonly long _end;
        private long _pos;
        private FileStream? _file;

        internal CacheReadStream(Song song, long start)
        {
            _song = song;
            _pos = start;
            _end = song.Length - 1;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count), default).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos > _end) return 0;
            while (true)
            {
                long have;
                lock (_song._owner._gate)
                {
                    if (_song.Cancelled) throw new IOException("stopped");
                    if (_song.Failure is not null) throw new IOException(_song.Failure);
                    have = _song._have;
                    if (_pos < have) break;
                    if (_song._done) return 0;
                }
                var seen = have;
                await _song.WaitForChangeAsync(seen, cancellationToken).ConfigureAwait(false);
            }
            _file ??= new FileStream(_song.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var available = _song.Have;
            var n = (int)Math.Min(buffer.Length, Math.Min(available, _end + 1) - _pos);
            if (n <= 0) return 0;
            _file.Seek(_pos, SeekOrigin.Begin);
            var read = await _file.ReadAsync(buffer[..n], cancellationToken).ConfigureAwait(false);
            _pos += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _song.Length;
        public override long Position { get => _pos; set => _pos = value; }
        public override long Seek(long offset, SeekOrigin origin)
        {
            _pos = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _pos + offset,
                _ => _end + 1 + offset,
            };
            return _pos;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _file?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Streams [start, end] straight from googlevideo as bytes arrive, at most 1 MB per request, for
    /// a seek far past the download. On 403 it refreshes the URL for the same length.
    /// </summary>
    private sealed class NetworkReadStream : Stream
    {
        private readonly Song _song;
        private readonly long _start;
        private readonly long _end;
        private long _pos;
        private Stream? _current;
        private int _failures;

        internal NetworkReadStream(Song song, long start)
        {
            _song = song;
            _pos = start;
            _start = start;
            _end = song.Length - 1;
        }

        private async Task<bool> EnsureOpenAsync(CancellationToken ct)
        {
            if (_current is not null) return true;
            if (_pos > _end) return false;
            var to = Math.Min(_end, _pos + (_pos == _start ? FirstPiece : Chunk) - 1);
            while (true)
            {
                var source = _song.Source;
                try
                {
                    _current = await _song._owner._fetcher.GetAsync(source.Url, source.Headers, _pos, to, ct).ConfigureAwait(false);
                    return true;
                }
                catch (StreamExpiredException)
                {
                    var fresh = _song._owner._refresher is null
                        ? null
                        : await _song._owner._refresher.RefreshAsync(_song.Key, ct).ConfigureAwait(false);
                    if (fresh is null || fresh.Length != _song.Length) throw new IOException("stream expired");
                    _song.Source = fresh;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or StreamHttpException or HttpRequestException)
                {
                    if (++_failures > 4) throw new IOException(ex.Message);
                    await _song._owner._clock.DelayAsync(TimeSpan.FromMilliseconds(400L * _failures), ct).ConfigureAwait(false);
                }
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                if (!await EnsureOpenAsync(cancellationToken).ConfigureAwait(false)) return 0;
                int n;
                try
                {
                    n = await _current!.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _end - _pos + 1)], cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (IOException ex)
                {
                    _current?.Dispose();
                    _current = null;
                    if (++_failures > 4) throw new IOException(ex.Message);
                    await _song._owner._clock.DelayAsync(TimeSpan.FromMilliseconds(400L * _failures), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (n < 0)
                {
                    _current.Dispose();
                    _current = null;
                    continue;   // next EnsureOpenAsync continues at _pos
                }
                if (n > 0)
                {
                    _pos += n;
                    _failures = 0;
                    return n;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count), default).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _song.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _current?.Dispose();
            base.Dispose(disposing);
        }
    }
}

internal static class Streaming
{
    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
