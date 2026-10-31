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
/// </summary>
public sealed class YouTubeStreams : IPlaybackStreams
{
    /// <summary>YouTube's loudness reference: a song's LUFS is <c>-14 + loudnessDb</c> (the JS rule).</summary>
    public const double YouTubeReference = -14;

    private readonly StreamResolver _resolver;
    private readonly SongDownloader _downloader;
    private readonly IOpusDecoderFactory _codec;
    private readonly ConcurrentDictionary<string, ResolvedStream> _resolved = new(StringComparer.Ordinal);

    /// <param name="codec">The Opus codec; null = Concentus (tests inject a fake).</param>
    public YouTubeStreams(StreamResolver resolver, SongDownloader downloader, IOpusDecoderFactory? codec = null)
    {
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
        _resolved[id] = s;
        // a re-registration hands a refreshed URL to the running download (same song, same length)
        _downloader.Register(id, new StreamSource(s.Url, s.Headers, s.Length, s.Mime));
        return LufsOf(s);
    }

    /// <summary>
    /// Reads the whole song from the downloader and decodes it (call it off the UI thread; the
    /// <see cref="EnginePlayer"/> does). Prefetches first if the song was never resolved.
    /// </summary>
    /// <exception cref="NotSupportedException">The stream isn't WebM/Opus.</exception>
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

        var pcm = OpusWebmDecoder.Decode(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), _codec);
        return new MemoryPcmSource(pcm.InterleavedStereo, pcm.SampleRate);
    }
}
