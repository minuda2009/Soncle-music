// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Cli;

// soncle --offline [<fixtures dir>] <command> …   recorded fixtures, no network
// soncle --live <command> …                       real YouTube, anonymous (resolve and download only for now)
var rest = args.ToList();
var offlineAt = rest.IndexOf("--offline");
var liveAt = rest.IndexOf("--live");

if (offlineAt >= 0 && liveAt >= 0)
{
    Console.Error.WriteLine("error: use either --offline or --live, not both.");
    return CliApp.Usage;
}

if (liveAt >= 0)
{
    rest.RemoveAt(liveAt);
    if (rest.Count == 0 || rest[0] is "-h" or "--help" or "help")
        return await new CliApp(new UnavailableBackend(), Console.Out, Console.Error).RunAsync(rest.ToArray());
    using var live = await LiveBackend.CreateAsync(CancellationToken.None);
    return await new CliApp(live, Console.Out, Console.Error).RunAsync(rest.ToArray());
}

if (offlineAt < 0)
{
    if (rest.Count == 0 || rest[0] is "-h" or "--help" or "help")
        return await new CliApp(new UnavailableBackend(), Console.Out, Console.Error).RunAsync(rest.ToArray());
    Console.Error.WriteLine("error: say where the data comes from: `--offline` (recorded fixtures) or `--live` (real YouTube, anonymous).");
    return CliApp.Usage;
}

rest.RemoveAt(offlineAt);
string? fixtures = null;
if (offlineAt < rest.Count && Directory.Exists(rest[offlineAt]) && rest[offlineAt] is not ("search" or "resolve" or "download" or "analyze" or "play"))
{
    fixtures = rest[offlineAt];
    rest.RemoveAt(offlineAt);
}
fixtures ??= FindFixtures();
if (fixtures is null)
{
    Console.Error.WriteLine("error: couldn't find the fixtures folder. Pass it: soncle --offline <path-to-fixtures> <command>");
    return CliApp.Usage;
}
return await new CliApp(new OfflineBackend(fixtures), Console.Out, Console.Error).RunAsync(rest.ToArray());

static string? FindFixtures()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "fixtures", "yt"))) return Path.Combine(dir.FullName, "fixtures");
    return null;
}
