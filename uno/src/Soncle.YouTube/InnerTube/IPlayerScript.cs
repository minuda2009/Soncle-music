// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.YouTube.InnerTube;

/// <summary>
/// The YouTube player script, reduced to what the stream clients need. A head provides it, running
/// the script in a browser engine (WebView2 on Windows), because deciphering means running YouTube's
/// own JavaScript. Without one, only clients that return plain URLs (IOS, ANDROID_VR) can play.
/// </summary>
public interface IPlayerScript
{
    /// <summary>The player's signature timestamp, which web-style clients put in the player request.</summary>
    int? SignatureTimestamp { get; }

    /// <summary>
    /// The final, playable URL for a format: applies the signature cipher and the throttling (<c>n</c>)
    /// transform. Returns null when the format cannot be deciphered.
    /// </summary>
    Task<string?> DecipherAsync(string? url, string? signatureCipher, CancellationToken ct);
}
