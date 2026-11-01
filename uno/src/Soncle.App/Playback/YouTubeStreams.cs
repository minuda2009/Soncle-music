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
/// <see cref="EnginePlayer"/>'s <see cref="SongLoader"/>) opens the song as a
/// <see cref="StreamingOpusSource"/> over the downloader, so sound starts with the first piece
/// (256 KB) while the rest downloads. Build the resolver with <see cref="OpusOnly"/> as its format
/// filter: there is no AAC decoder yet.
/// <para>
/// Only a few seconds of PCM are held per song (the decode-ahead ring), so there is no length
/// limit. A seek far past the download is served from the network by the downloader; the
/// resolved length and bitrate give the length estimate until the file's own Duration is read.
/// </para>
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
        _resolved[id] = s;
        // a re-registration hands a refreshed URL to the running download (same song, same length)
        _downloader.Register(id, new StreamSource(s.Url, s.Headers, s.Length, s.Mime));
        return LufsOf(s);
    }

    /// <summary>
    /// Opens the song for streaming playback and returns once its head is parsed and the first
    /// audio is decoded (call it off the UI thread; the <see cref="EnginePlayer"/> does). The
    /// download carries on in the background. Prefetches first if the song was never resolved.
    /// </summary>
    /// <exception cref="NotSupportedException">The stream isn't WebM/Opus.</exception>
    /// <exception cref="IOException">The download failed before the first audio.</exception>
    public async Task<IAudioSource> LoadSongAsync(string id, CancellationToken ct)
    {
        if (!_resolved.TryGetValue(id, out var s))
        {
            await PrefetchAsync(id, ct).ConfigureAwait(false);
            s = _resolved[id];
        }
        if (!CanDecode(s.Mime)) throw new NotSupportedException($"this build can't play {s.Mime} yet");

        var estimate = EstimatedSeconds(s) is { } seconds ? (long)(seconds * OpusWebmDecoder.SampleRate) : (long?)null;
        var source = new StreamingOpusSource(
            position => _downloader.OpenRead(id, position), _codec, estimate,
            () => _downloader.Status(id)?.Fraction ?? 0);
        try
        {
            await source.Ready.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            source.Dispose();
            throw;
        }
        return source;
    }
}
