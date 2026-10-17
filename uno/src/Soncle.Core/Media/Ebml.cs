// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Buffers.Binary;

namespace Soncle.Core.Media;

/// <summary>One EBML element found by <see cref="Ebml.Children"/>. <see cref="Unknown"/> = its size was "unknown" (all ones).</summary>
public readonly record struct EbmlElement(long Id, long Start, long DataStart, long DataEnd, bool Unknown);

/// <summary>
/// EBML reading (the container under WebM/Matroska), shared by the WebM cue/cluster slicer in
/// <c>Soncle.Flow</c> and the Opus demuxer in <c>Soncle.Audio</c>. A port of <c>readId</c>,
/// <c>readSize</c>, <c>readUint</c>, <c>readFloat</c> and <c>children</c> in
/// <c>renderer/flow/webm.js</c>; everything is bounds-checked and returns null/stops on damage.
/// </summary>
public static class Ebml
{
    /// <summary>An element id (1–4 bytes, marker bits kept, as written in the spec).</summary>
    public static (long V, int Len)? ReadId(ReadOnlySpan<byte> b, long p)
    {
        if (p < 0 || p >= b.Length) return null;
        var x = b[(int)p];
        var len = x >= 0x80 ? 1 : x >= 0x40 ? 2 : x >= 0x20 ? 3 : x >= 0x10 ? 4 : 0;
        if (len == 0 || p + len > b.Length) return null;
        long v = 0;
        for (var i = 0; i < len; i++) v = v * 256 + b[(int)p + i];
        return (v, len);
    }

    /// <summary>An element size (1–8 bytes, marker removed); -1 means "unknown" (all value bits set).</summary>
    public static (long V, int Len)? ReadSize(ReadOnlySpan<byte> b, long p)
    {
        if (p < 0 || p >= b.Length) return null;
        int x = b[(int)p];
        var len = 1; var mask = 0x80;
        while (len <= 8 && (x & mask) == 0) { len++; mask >>= 1; }
        if (len > 8 || p + len > b.Length) return null;
        long v = x & (mask - 1);
        var allOnes = v == mask - 1;
        for (var i = 1; i < len; i++)
        {
            v = v * 256 + b[(int)p + i];
            if (b[(int)p + i] != 0xff) allOnes = false;
        }
        return (allOnes ? -1 : v, len);
    }

    /// <summary>An unsigned big-endian integer of <paramref name="n"/> bytes, as a double (a JS Number; exact up to 2^53).</summary>
    public static double ReadUint(ReadOnlySpan<byte> b, long p, long n)
    {
        double v = 0;
        for (var i = 0; i < n && p + i < b.Length; i++) v = v * 256 + b[(int)(p + i)];
        return v;
    }

    /// <summary>A signed big-endian integer of <paramref name="n"/> bytes (1–8).</summary>
    public static long ReadInt(ReadOnlySpan<byte> b, long p, long n)
    {
        if (n < 1 || n > 8 || p + n > b.Length) return 0;
        long v = (sbyte)b[(int)p];   // sign-extend from the first byte
        for (var i = 1; i < n; i++) v = (v << 8) | b[(int)p + i];
        return v;
    }

    /// <summary>A 4-byte (float32) or 8-byte (float64) big-endian float; null for any other size.</summary>
    public static double? ReadFloat(ReadOnlySpan<byte> b, long p, long n)
    {
        if (p + n > b.Length) return null;
        return n switch
        {
            4 => BinaryPrimitives.ReadSingleBigEndian(b.Slice((int)p, 4)),
            8 => BinaryPrimitives.ReadDoubleBigEndian(b.Slice((int)p, 8)),
            _ => null,
        };
    }

    /// <summary>The elements between <paramref name="start"/> and <paramref name="end"/>; stops at damage, or after an unknown-size element.</summary>
    public static IEnumerable<EbmlElement> Children(ReadOnlyMemory<byte> b, long start, long end)
    {
        var p = start;
        while (p < end)
        {
            var id = ReadId(b.Span, p); if (id is null) yield break;
            var sz = ReadSize(b.Span, p + id.Value.Len); if (sz is null) yield break;
            var dataStart = p + id.Value.Len + sz.Value.Len;
            var dataEnd = sz.Value.V < 0 ? end : dataStart + sz.Value.V;
            yield return new EbmlElement(id.Value.V, p, dataStart, dataEnd, sz.Value.V < 0);
            if (sz.Value.V < 0) yield break;
            p = dataEnd;
        }
    }

    /// <summary>Like <c>Uint8Array.subarray</c>: the end is clamped to the data we have.</summary>
    public static ReadOnlyMemory<byte> Sub(ReadOnlyMemory<byte> b, long start, long end)
    {
        end = Math.Min(end, b.Length);
        return start >= end ? ReadOnlyMemory<byte>.Empty : b.Slice((int)start, (int)(end - start));
    }
}
