// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.TestKit;

/// <summary>
/// Locates the repository's shared <c>fixtures/</c> folder. Tests run from a per-project output
/// directory, so we walk up until a folder containing <c>fixtures</c> is found. The root is looked
/// up lazily on each call, so the helper keeps working whether or not M01 has added the folder yet.
/// </summary>
public static class Fixtures
{
    /// <summary>The absolute path to the repository's <c>fixtures/</c> folder.</summary>
    public static string RootPath => FindRoot();

    /// <summary>The absolute path to a file or folder inside <c>fixtures/</c>.</summary>
    public static string Path(string relative)
    {
        if (relative is null) throw new ArgumentNullException(nameof(relative));
        var cleaned = relative.Replace('\\', '/').TrimStart('/');
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(RootPath, cleaned));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate)) return candidate;
            // a checkout without fixtures/ (before M01) still has uno/; use its parent
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "uno", "Soncle.sln")))
                return System.IO.Path.Combine(dir.FullName, "fixtures");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not find the repository 'fixtures' folder by walking up from " + AppContext.BaseDirectory);
    }
}
