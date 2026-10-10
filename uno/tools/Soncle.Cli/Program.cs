// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Cli;

// `soncle --offline [<fixtures dir>] <command> …` runs against recorded fixtures with no network.
// Real YouTube needs the InnerTube client, which is not part of this build yet.
var rest = args.ToList();
var offlineAt = rest.IndexOf("--offline");
if (offlineAt < 0)
{
    var app0 = new CliApp(new UnavailableBackend(), Console.Out, Console.Error);
    if (rest.Count == 0 || rest[0] is "-h" or "--help" or "help") return await app0.RunAsync(rest.ToArray());
    Console.Error.WriteLine("error: real YouTube access is not part of this build yet (the InnerTube client is a later step).");
    Console.Error.WriteLine("       Try `soncle --offline <command> …` to run against the recorded fixtures.");
    return CliApp.Failed;
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
var app = new CliApp(new OfflineBackend(fixtures), Console.Out, Console.Error);
return await app.RunAsync(rest.ToArray());

static string? FindFixtures()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "fixtures", "yt"))) return Path.Combine(dir.FullName, "fixtures");
    return null;
}
