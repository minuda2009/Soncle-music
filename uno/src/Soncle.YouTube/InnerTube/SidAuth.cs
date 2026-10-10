// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Soncle.YouTube.InnerTube;

/// <summary>
/// The <c>Authorization</c> header for a signed-in InnerTube request, exactly as youtubei.js'
/// <c>generateSidAuth</c>: <c>SAPISIDHASH {t}_{sha1("{t} {SAPISID} https://www.youtube.com")}</c>.
/// The SAPISID value is a credential: it is never logged or returned, only hashed.
/// </summary>
public static class SidAuth
{
    private const string Origin = "https://www.youtube.com";

    public static string Header(string sapisid, long unixSeconds)
    {
        if (string.IsNullOrEmpty(sapisid)) throw new ArgumentException("SAPISID is empty", nameof(sapisid));
        var input = $"{unixSeconds} {sapisid} {Origin}";
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(input)));
        return $"SAPISIDHASH {unixSeconds}_{hash}";
    }

    /// <summary>Reads one cookie's value out of a <c>Cookie</c> header string, or null (youtubei.js <c>getCookie</c>).</summary>
    public static string? CookieValue(string? cookieHeader, string name)
    {
        if (string.IsNullOrEmpty(cookieHeader)) return null;
        foreach (var part in cookieHeader.Split(';'))
        {
            var kv = part.Trim();
            var eq = kv.IndexOf('=');
            if (eq > 0 && kv.AsSpan(0, eq).SequenceEqual(name)) return kv[(eq + 1)..];
        }
        return null;
    }
}
