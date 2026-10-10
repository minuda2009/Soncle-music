// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text;

namespace Soncle.YouTube.InnerTube;

/// <summary>
/// Decodes a format's <c>xtags</c> (URL-encoded base64url of the protobuf
/// <c>FormatXTags { repeated KeyValuePair xtags = 1 }</c>, <c>KeyValuePair { string key = 1; string value = 2 }</c>),
/// as youtubei.js does, to find flags such as <c>drc=1</c>. Unknown or malformed input gives no tags.
/// </summary>
public static class XTags
{
    public static IReadOnlyList<KeyValuePair<string, string>> Decode(string? xtags)
    {
        var tags = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(xtags)) return tags;
        byte[] bytes;
        try
        {
            var b64 = Uri.UnescapeDataString(xtags).Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            bytes = Convert.FromBase64String(b64);
        }
        catch (FormatException) { return tags; }

        var pos = 0;
        try
        {
            while (pos < bytes.Length)
            {
                var tag = ReadVarint(bytes, ref pos);
                if (tag == 10)   // field 1, length-delimited
                {
                    var len = (int)ReadVarint(bytes, ref pos);
                    var end = pos + len;
                    if (len < 0 || end > bytes.Length) return tags;
                    string? key = null, value = null;
                    while (pos < end)
                    {
                        var t = ReadVarint(bytes, ref pos);
                        if (t is 10 or 18)
                        {
                            var l = (int)ReadVarint(bytes, ref pos);
                            if (l < 0 || pos + l > end) return tags;
                            var s = Encoding.UTF8.GetString(bytes, pos, l);
                            pos += l;
                            if (t == 10) key = s; else value = s;
                        }
                        else if (!Skip(bytes, ref pos, (int)(t & 7))) return tags;
                    }
                    pos = end;
                    if (key is not null) tags.Add(new(key, value ?? ""));
                }
                else if (!Skip(bytes, ref pos, (int)(tag & 7))) return tags;
            }
        }
        catch (IndexOutOfRangeException) { }
        return tags;
    }

    public static bool Has(IReadOnlyList<KeyValuePair<string, string>> tags, string key, string value) =>
        tags.Any(t => t.Key == key && t.Value == value);

    private static ulong ReadVarint(byte[] b, ref int pos)
    {
        ulong r = 0; var shift = 0;
        while (true)
        {
            var x = b[pos++];
            r |= (ulong)(x & 0x7F) << shift;
            if ((x & 0x80) == 0) return r;
            shift += 7;
            if (shift > 63) throw new IndexOutOfRangeException();
        }
    }

    private static bool Skip(byte[] b, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint(b, ref pos); return true;
            case 1: pos += 8; return pos <= b.Length;
            case 2: { var l = (int)ReadVarint(b, ref pos); pos += l; return l >= 0 && pos <= b.Length; }
            case 5: pos += 4; return pos <= b.Length;
            default: return false;
        }
    }
}
