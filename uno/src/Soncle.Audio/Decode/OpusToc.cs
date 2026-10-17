// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Audio.Decode;

/// <summary>
/// How long an Opus packet is, read from its table-of-contents byte (RFC 6716 §3.1), without
/// decoding it. Durations are in samples at 48 kHz (Opus' internal reference rate).
/// </summary>
public static class OpusToc
{
    /// <summary>Longest legal packet: 120 ms (RFC 6716 §3.2.5).</summary>
    public const int MaxSamples = 5760;

    /// <summary>Samples per frame for a TOC config number (0–31), at 48 kHz.</summary>
    public static int FrameSamples(int config) => config switch
    {
        < 12 => (config & 3) switch { 0 => 480, 1 => 960, 2 => 1920, _ => 2880 },   // SILK: 10, 20, 40, 60 ms
        < 16 => (config & 1) == 0 ? 480 : 960,                                        // hybrid: 10, 20 ms
        _ => (config & 3) switch { 0 => 120, 1 => 240, 2 => 480, _ => 960 },         // CELT: 2.5, 5, 10, 20 ms
    };

    /// <summary>The number of frames in the packet (1–48), or 0 if it is too short to say.</summary>
    public static int FrameCount(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 1) return 0;
        return (packet[0] & 3) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length < 2 ? 0 : packet[1] & 0x3f,
        };
    }

    /// <summary>Total samples per channel at 48 kHz, or 0 for an empty or invalid packet.</summary>
    public static int Samples(ReadOnlySpan<byte> packet)
    {
        var frames = FrameCount(packet);
        if (frames == 0) return 0;
        var total = frames * FrameSamples(packet[0] >> 3);
        return total > MaxSamples ? 0 : total;
    }
}
