// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;

namespace Soncle.Cli;

/// <summary>Writes stereo IEEE-float (32-bit) WAV files: what <c>play</c> produces off Windows.</summary>
public static class WavWriter
{
    public static void Write(string path, ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        var frames = Math.Min(left.Length, right.Length);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);
        var dataBytes = frames * 2 * 4;
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                 // fmt chunk size
        w.Write((short)3);           // IEEE float
        w.Write((short)2);           // channels
        w.Write(sampleRate);
        w.Write(sampleRate * 2 * 4); // byte rate
        w.Write((short)8);           // block align
        w.Write((short)32);          // bits per sample
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        for (var i = 0; i < frames; i++) { w.Write(left[i]); w.Write(right[i]); }
    }

    /// <summary>Reads back a file written by <see cref="Write"/>.</summary>
    public static (float[] Left, float[] Right, int SampleRate) Read(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        r.ReadInt32();
        if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");
        r.ReadChars(4); r.ReadInt32();
        var fmt = r.ReadInt16(); var ch = r.ReadInt16(); var sr = r.ReadInt32();
        r.ReadInt32(); r.ReadInt16(); var bits = r.ReadInt16();
        if (fmt != 3 || ch != 2 || bits != 32) throw new InvalidDataException("expected stereo 32-bit float");
        r.ReadChars(4);
        var n = r.ReadInt32() / 8;
        var l = new float[n]; var rr = new float[n];
        for (var i = 0; i < n; i++) { l[i] = r.ReadSingle(); rr[i] = r.ReadSingle(); }
        return (l, rr, sr);
    }
}
