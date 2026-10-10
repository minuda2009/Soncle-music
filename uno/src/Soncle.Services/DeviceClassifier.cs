// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Soncle.Services;

/// <summary>What kind of audio output a label describes.</summary>
public sealed record OutputInfo(string Label, string Model, string Endpoint, string Type, bool Bluetooth, bool HandsFree);

/// <summary>Friendly name/icon/preset for each output type.</summary>
public sealed record DeviceMeta(string Name, string Icon, string Preset);

/// <summary>
/// Identifies what kind of audio output is connected, from its Windows / Chromium (or Android)
/// name. A direct port of <c>renderer/devices.js</c>, so per-device profiles classify the same on
/// every platform.
/// </summary>
public static partial class DeviceClassifier
{
    [GeneratedRegex(@"hands-?free|\bhf\b|ag audio|headset \(", RegexOptions.IgnoreCase)]
    private static partial Regex RxHandsFree();
    [GeneratedRegex(@"\bcar\b|carplay|android auto|\bsync\b|toyota|honda|suzuki|maruti|hyundai|\bkia\b|nissan|mazda|\bbmw\b|\baudi\b|\bvw\b|volkswagen|mercedes|ford|tesla|pioneer|kenwood|\bjvc\b|alpine|\bcd-|media ?nav|uconnect|mylink|entune", RegexOptions.IgnoreCase)]
    private static partial Regex RxCar();
    [GeneratedRegex(@"hdmi|\btv\b|display audio|nvidia high definition|amd high definition|intel\(r\) display|monitor|dell u|lg (ultra|tv)|samsung (tv|q)|bravia|roku|chromecast", RegexOptions.IgnoreCase)]
    private static partial Regex RxTv();
    [GeneratedRegex(@"buds|airpods(?! max)|\bwf-|earbud|\btws\b|airdopes|freebuds|liberty|\bear ?\(|nothing ear|pixel buds|galaxy buds|elite \d|jabra elite|soundcore (a|p|life p|space a)|enco|redmi ?buds|oneplus ?buds|realme ?buds|cmf ?buds|in-?ear|earphones?|powerbeats|beats (fit|studio buds|flex)|momentum true|qc ?earbuds|ultra open", RegexOptions.IgnoreCase)]
    private static partial Regex RxEarbuds();
    [GeneratedRegex(@"headphones?|airpods max|\bwh-|\bqc ?\d|quietcomfort|momentum|rockerz|\bjbl (tune|live)\b|beats (solo|studio)|\bsolo ?\d|\bath-|\bmdr-|\bhd ?\d{3}|over-?ear|on-?ear|arctis|hyperx|cloud|kraken|blackshark|g pro|astro|space one|q30|q35|q45|life q", RegexOptions.IgnoreCase)]
    private static partial Regex RxHeadphones();
    [GeneratedRegex(@"speaker|\bflip ?\d|\bcharge ?\d|\bclip ?\d|\bgo ?\d|\bboom|megaboom|wonderboom|soundlink|partybox|xtreme|\bsrs-|marshall|\bue |echo|homepod|nest|sonos|soundbar|stone ?\d|motion\+?|soundcore (motion|flare|boom)|bose (home|smart)|mi portable|tribit|zebronics|\bpulse\b", RegexOptions.IgnoreCase)]
    private static partial Regex RxSpeaker();
    [GeneratedRegex(@"realtek|conexant|intel\(r\) smart sound|smart sound|cirrus|synaptics|internal|built-?in|macbook|high definition audio device", RegexOptions.IgnoreCase)]
    private static partial Regex RxLaptop();
    [GeneratedRegex(@"\bwh-|\bwf-|airpods|buds|airdopes|rockerz|\bflip ?\d|\bcharge ?\d|soundlink|\bjbl\b|\bboat\b|soundcore|beats|\bbose\b|marshall|stone ?\d|tribit|\bue |\bsrs-|life q|space one|enco|nothing ear|\bmi |redmi|realme|oneplus|jabra|skullcandy|sennheiser|momentum|\bqc ?\d|quietcomfort|galaxy|pixel", RegexOptions.IgnoreCase)]
    private static partial Regex RxWireless();
    [GeneratedRegex(@"\busb\b|dac|focusrite|scarlett|audient|behringer|motu|fiio|topping|schiit|interface", RegexOptions.IgnoreCase)]
    private static partial Regex RxUsb();
    [GeneratedRegex(@"realtek|conexant|cirrus|usb|high definition|hdmi|focusrite|scarlett|dac", RegexOptions.IgnoreCase)]
    private static partial Regex RxWiredHint();

    public static readonly IReadOnlyDictionary<string, DeviceMeta> DeviceInfo = new Dictionary<string, DeviceMeta>
    {
        ["earbuds"] = new("Earbuds", "earbuds", "Earbuds"),
        ["headphones"] = new("Headphones", "headphones", "Headphones"),
        ["wired"] = new("Wired headphones", "headphones", "Headphones"),
        ["btspeaker"] = new("Bluetooth speaker", "speaker", "Bluetooth speaker"),
        ["speaker"] = new("Speakers", "speaker", "Flat"),
        ["laptop"] = new("Built-in speakers", "laptop", "Laptop speakers"),
        ["car"] = new("Car audio", "car", "Car"),
        ["tv"] = new("TV / monitor", "tv", "TV / HDMI"),
        ["usb"] = new("USB audio / DAC", "usb", "Flat"),
        ["unknown"] = new("Audio output", "speaker", "Flat"),
    };

    public static OutputInfo Classify(string? label, IReadOnlyList<string>? btNames = null)
    {
        btNames ??= Array.Empty<string>();
        var raw = Regex.Replace(label ?? string.Empty, @"^(Default|Communications) - ", string.Empty, RegexOptions.IgnoreCase).Trim();

        // Peel trailing "(...)" groups, respecting nesting.
        var tags = new List<string>();
        var rest = raw;
        while (rest.EndsWith(')'))
        {
            var depth = 0; var i = rest.Length - 1;
            for (; i >= 0; i--)
            {
                if (rest[i] == ')') depth++;
                else if (rest[i] == '(' && --depth == 0) break;
            }
            if (i <= 0) break;
            tags.Insert(0, rest.Substring(i + 1, rest.Length - 1 - (i + 1)).Trim());
            rest = rest.Substring(0, i).Trim();
        }
        var btTag = tags.Any(t => Regex.IsMatch(t, "^bluetooth$", RegexOptions.IgnoreCase));
        var named = tags.Where(t => !Regex.IsMatch(t, @"^(bluetooth|stereo|[0-9a-f]{4}:[0-9a-f]{4})$", RegexOptions.IgnoreCase)).ToList();
        var endpoint = tags.Count > 0 ? rest : string.Empty;
        var modelSource = named.Count > 0 ? named[0] : (tags.Count > 0 ? rest : raw);
        var model = Regex.Replace(modelSource, @"\s*(Stereo|Hands-Free.*|AG Audio)$", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (model.Length == 0) model = raw;
        var lower = raw.ToLowerInvariant();
        var wiredHint = RxWiredHint().IsMatch(raw);
        var bluetooth = btTag
            || (!wiredHint && (Regex.IsMatch(raw, @"bluetooth|\bbt\b|hands-?free|ag audio", RegexOptions.IgnoreCase)
                || Regex.IsMatch(endpoint, "^headset$", RegexOptions.IgnoreCase)
                || btNames.Any(n => !string.IsNullOrEmpty(n) && (lower.Contains(n.ToLowerInvariant()) || model.ToLowerInvariant().Contains(n.ToLowerInvariant())))
                || RxWireless().IsMatch(model)));
        var handsFree = bluetooth && RxHandsFree().IsMatch(raw);

        bool Test(Regex rx) => rx.IsMatch(model) || rx.IsMatch(raw);
        string type = "unknown";
        if (Test(RxCar())) type = "car";
        else if (Test(RxEarbuds())) type = "earbuds";
        else if (Test(RxTv())) type = "tv";
        else if (Regex.IsMatch(endpoint, "^headphones?$", RegexOptions.IgnoreCase) && !bluetooth
                 && Regex.IsMatch(model, "realtek|conexant|high definition|cirrus", RegexOptions.IgnoreCase)) type = "wired";
        else if (Test(RxHeadphones()) || (bluetooth && Regex.IsMatch(endpoint, "^head(phones?|set)$", RegexOptions.IgnoreCase))) type = "headphones";
        else if (Test(RxSpeaker()) && (bluetooth || !RxLaptop().IsMatch(model))) type = bluetooth ? "btspeaker" : "speaker";
        else if (Test(RxUsb())) type = "usb";
        else if (RxLaptop().IsMatch(model) || Regex.IsMatch(endpoint, "^speakers?$", RegexOptions.IgnoreCase)) type = "laptop";
        else if (bluetooth) type = "headphones";

        return new OutputInfo(raw, model.Length > 0 ? model : (raw.Length > 0 ? raw : "System default"), endpoint, type, bluetooth, handsFree);
    }
}
