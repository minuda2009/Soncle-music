// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Core.Tests;

/// <summary>
/// Fails if any C# file under <c>uno/</c> is missing the Soncle attribution header
/// (see AGENTS.md). Generated build output under bin/ and obj/ is skipped.
/// </summary>
public class HeaderCheckTests
{
    private const string Header = "// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later";

    [Fact]
    public void EveryCSharpFileUnderUnoHasTheSoncleHeader()
    {
        var root = FindUnoRoot();
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                continue;
            if (rel.Contains("/.git/", StringComparison.Ordinal)) continue;

            var firstLine = FirstLine(file);
            if (!string.Equals(firstLine, Header, StringComparison.Ordinal))
                missing.Add(rel);
        }

        Assert.True(missing.Count == 0,
            "Missing the Soncle header in:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    private static string FirstLine(string path)
    {
        using var reader = new StreamReader(path);
        return reader.ReadLine() ?? string.Empty;
    }

    private static string FindUnoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Soncle.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find uno/Soncle.sln by walking up from " + AppContext.BaseDirectory);
    }
}
