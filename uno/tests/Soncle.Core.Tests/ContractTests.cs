// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Core.Models;
using Soncle.TestKit;

namespace Soncle.Core.Tests;

/// <summary>
/// The C# contracts must cover every <c>window.api</c> member in <c>fixtures/contract/api.json</c>.
/// Each interface member carries the JS member it replaces in a <c>// name(</c> comment; this test
/// checks every listed member appears there, so the contract can't silently drop one.
/// </summary>
public class ContractTests
{
    [Fact]
    public void EveryApiMemberIsDocumentedInTheContracts()
    {
        var api = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("contract/api.json"))).RootElement;
        var contractSources = Directory
            .EnumerateFiles(FindCoreSrc(), "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains("Contracts") && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .Select(File.ReadAllText);
        var text = string.Join("\n", contractSources);

        var missing = new List<string>();
        foreach (var m in api.GetProperty("members").EnumerateArray())
        {
            var name = m.GetProperty("name").GetString()!;
            // the JS member is cited as "// name(" for methods or "// name" for properties/events
            var cited = System.Text.RegularExpressions.Regex.IsMatch(text, $@"// {System.Text.RegularExpressions.Regex.Escape(name)}(?![\w])");
            if (!cited) missing.Add(name);
        }
        Assert.True(missing.Count == 0, "not documented in Contracts: " + string.Join(", ", missing));
    }

    [Fact]
    public void ModelsRoundTripUnknownProperties()
    {
        var json = """{"id":"x","title":"t","artists":[{"name":"a","extraField":1}],"duration":12.5,"futureThing":{"a":1}}""";
        var track = SoncleJson.Deserialize<Track>(json)!;
        Assert.Equal("x", track.Id);
        var back = SoncleJson.Serialize(track);
        Assert.Contains("futureThing", back);
        Assert.Contains("extraField", back);
    }

    [Fact]
    public void PageModelCarriesSectionsAndContinuation()
    {
        var page = new Page { HasMore = true, Continuation = "tok" };
        page.Sections.Add(new Shelf { Title = "Songs", Items = { new Track { Id = "1", Title = "A" } } });
        var json = SoncleJson.Serialize(page);
        Assert.Contains("\"hasMore\":true", json);
        var again = SoncleJson.Deserialize<Page>(json)!;
        Assert.True(again.HasMore);
        Assert.Equal("tok", again.Continuation);
        Assert.Equal("A", again.Sections[0].Items[0].Title);
    }

    private static string FindCoreSrc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "uno", "src", "Soncle.Core");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("uno/src/Soncle.Core not found");
    }
}
