// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Globalization;
using Soncle.Audio;
using Soncle.Audio.Decode;
using Soncle.Audio.Opus;
using Soncle.Core.Playback;
using Soncle.Streams;
using Soncle.YouTube;

namespace Soncle.App.Playback;

/// <summary>
/// The YouTube side of playback for the C# heads: <see cref="PrefetchAsync"/> resolves a song
/// (<see cref="StreamResolver"/>, the <c>resolveStream</c> rules), registers it with the
/// <see cref="SongDownloader"/> and returns its loudness; <see cref="LoadSongAsync"/> (the
/// <see cref="EnginePlayer"/>'s <see cref="SongLoader"/>) reads the whole song from the downloader
/// and decodes WebM/Opus in memory. Build the resolver with <see cref="OpusOnly"/> as its format
/// filter: there is no AAC decoder yet.
/// <para>
/// A decoded song is held whole in memory (48 kHz stereo float, about 23 MB a minute), so songs
/// longer than <see cref="MaxSongSeconds"/> are refused with <see cref="NotSupportedException"/>
/// (the player bar shows its plain "can't play" message) until playback can start while streaming.
/// The resolved length and bitrate give an estimate before anything is downloaded; the decoder
/// checks the exact length before it allocates the PCM.
/// </para>
/// </summary>
public sealed class YouTubeStreams : IPlaybackStreams
{
    /// <summary>YouTube's loudness reference: a song's LUFS is <c>-14 + loudnessDb</c> (the JS rule).</summary>
    public const double YouTubeReference = -14;

    /// <summary>The longest song played, s (20 min: about 460 MB of PCM, two of which can be alive across a swap).</summary>
    public const int MaxSongSeconds = 20 * 60;

    private readonly StreamResolver _resolver;
    private readonly SongDownloader _downloader;
    private readonly IOpusDecoderFactory _codec;
    private readonly ConcurrentDictionary<string, ResolvedStream> _resolved = new(StringComparer.Ordinal);
    private readonly int _maxSeconds;

    /// <param name="codec">The Opus codec; null = Concentus (tests inject a fake).</param>
    /// <param name="maxSongSeconds">The longest song played, s (tests lower it).</param>
    public YouTubeStreams(StreamResolver resolver, SongDownloader downloader, IOpusDecoderFactory? codec = null, int maxSongSeconds = MaxSongSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSongSeconds);
        _maxSeconds = maxSongSeconds;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _codec = codec ?? new ConcentusOpusDecoderFactory();
    }

    /// <summary>
    /// The resolver's format filter for heads that decode only WebM/Opus: <c>audio/webm</c> with
    /// an <c>opus</c> codecs parameter or none (the same rule as the CLI's decoder).
    /// </summary>
    public static bool OpusOnly(AudioFormat format) => format is not null && CanDecode(format.MimeType);

    /// <summary>Whether <paramref name="mime"/> is WebM/Opus ("audio/webm" or "audio/webm; codecs=\"opus\"").</summary>
    public static bool CanDecode(string? mime) =>
        mime is not null
        && mime.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase)
        && (!mime.Contains("codecs", StringComparison.OrdinalIgnoreCase) || mime.Contains("opus", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The song's length estimated from its byte length and bitrate, s, or null when either is
    /// unknown. YouTube's bitrate is the peak, at or above the average, so this is never longer
    /// than the song.
    /// </summary>
    public static double? EstimatedSeconds(ResolvedStream s) =>
        s.Length > 0 && s.Bitrate > 0 ? s.Length * 8.0 / s.Bitrate : null;

    /// <summary>The song's integrated loudness from YouTube's <c>loudnessDb</c>, or null when unknown.</summary>
    public static double? LufsOf(ResolvedStream s) =>
        double.TryParse(s.LoudnessDb, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) && double.IsFinite(db)
            ? YouTubeReference + db : null;

    /// <summary>Resolves <paramref name="id"/>, starts its download and returns its LUFS (null = unknown).</summary>
    public async Task<double?> PrefetchAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var s = await _resolver.ResolveAsync(id, ct: ct).ConfigureAwait(false);
        if (!CanDecode(s.Mime)) throw new NotSupportedException($"this build can't play {s.Mime} yet");
        if (EstimatedSeconds(s) is { } seconds && seconds > _maxSeconds) throw TooLong((long)seconds);
        _resolved[id] = s;
        // a re-registration hands a refreshed URL to the running download (same song, same length)
        _downloader.Register(id, new StreamSource(s.Url, s.Headers, s.Length, s.Mime));
        return LufsOf(s);
    }

    /// <summary>
    /// Reads the whole song from the downloader and decodes it (call it off the UI thread; the
    /// <see cref="EnginePlayer"/> does). Prefetches first if the song was never resolved.
    /// </summary>
    /// <exception cref="NotSupportedException">The stream isn't WebM/Opus, or the song is longer than the limit.</exception>
    /// <exception cref="IOException">The download ended short.</exception>
    public async Task<IAudioSource> LoadSongAsync(string id, CancellationToken ct)
    {
        if (!_resolved.TryGetValue(id, out var s))
        {
            await PrefetchAsync(id, ct).ConfigureAwait(false);
            s = _resolved[id];
        }
        if (!CanDecode(s.Mime)) throw new NotSupportedException($"this build can't play {s.Mime} yet");

        using var bytes = new MemoryStream(s.Length is > 0 and < int.MaxValue ? (int)s.Length : 0);
        await using (var src = _downloader.OpenRead(id, 0))
            await src.CopyToAsync(bytes, ct).ConfigureAwait(false);
        if (s.Length > 0 && bytes.Length != s.Length)
            throw new IOException($"incomplete download: {bytes.Length} of {s.Length} bytes");

        // the exact length is checked before the PCM is allocated
        var pcm = OpusWebmDecoder.Decode(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), _codec,
            maxFrames: (long)_maxSeconds * OpusWebmDecoder.SampleRate);
        return new MemoryPcmSource(pcm.InterleavedStereo, pcm.SampleRate);
    }

    private NotSupportedException TooLong(long seconds) =>
        new($"songs over {_maxSeconds} s can't play yet (this one is about {seconds} s)");
}
