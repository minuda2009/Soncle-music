// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;

namespace Soncle.TestKit;

/// <summary>
/// Structural JSON comparison that ignores property order and reports the path of the first
/// difference (for example <c>$.items[3].title</c>). Numbers compare exactly by default, or
/// within <paramref name="numberTolerance"/> when one is given (audio uses ±0.1 dB).
/// </summary>
public static class JsonAssert
{
    public static void Equal(string expected, string actual, double numberTolerance = 0)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        Compare(e.RootElement, a.RootElement, "$", numberTolerance);
    }

    private static void Compare(JsonElement expected, JsonElement actual, string path, double tolerance)
    {
        if (expected.ValueKind != actual.ValueKind)
            throw Fail(path, $"expected {expected.ValueKind}, got {actual.ValueKind}");

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObjects(expected, actual, path, tolerance);
                break;
            case JsonValueKind.Array:
                var eItems = expected.EnumerateArray().ToArray();
                var aItems = actual.EnumerateArray().ToArray();
                if (eItems.Length != aItems.Length)
                    throw Fail(path, $"expected {eItems.Length} items, got {aItems.Length}");
                for (var i = 0; i < eItems.Length; i++)
                    Compare(eItems[i], aItems[i], $"{path}[{i}]", tolerance);
                break;
            case JsonValueKind.String:
                if (!string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal))
                    throw Fail(path, $"expected {Show(expected.GetString())}, got {Show(actual.GetString())}");
                break;
            case JsonValueKind.Number:
                CompareNumbers(expected, actual, path, tolerance);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break; // same ValueKind is enough
            default:
                throw Fail(path, $"unsupported JSON kind {expected.ValueKind}");
        }
    }

    private static void CompareObjects(JsonElement expected, JsonElement actual, string path, double tolerance)
    {
        var actualProps = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in actual.EnumerateObject()) actualProps[p.Name] = p.Value;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in expected.EnumerateObject())
        {
            seen.Add(p.Name);
            if (!actualProps.TryGetValue(p.Name, out var av))
                throw Fail(Child(path, p.Name), "missing property");
            Compare(p.Value, av, Child(path, p.Name), tolerance);
        }
        foreach (var p in actual.EnumerateObject())
        {
            if (!seen.Contains(p.Name))
                throw Fail(Child(path, p.Name), "unexpected property");
        }
    }

    private static void CompareNumbers(JsonElement expected, JsonElement actual, string path, double tolerance)
    {
        var ev = expected.GetDouble();
        var av = actual.GetDouble();
        if (tolerance > 0)
        {
            if (Math.Abs(ev - av) > tolerance)
                throw Fail(path, $"expected {Fmt(ev)} ±{Fmt(tolerance)}, got {Fmt(av)}");
        }
        else if (!expected.GetRawText().Equals(actual.GetRawText(), StringComparison.Ordinal) && ev != av)
        {
            throw Fail(path, $"expected {Fmt(ev)}, got {Fmt(av)}");
        }
    }

    private static string Child(string path, string name) =>
        name.All(c => char.IsLetterOrDigit(c) || c == '_') ? $"{path}.{name}" : $"{path}[{Show(name)}]";

    private static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Show(string? s) => s is null ? "null" : $"\"{s}\"";

    private static JsonException Fail(string path, string message) =>
        new($"{path}: {message}");
}
