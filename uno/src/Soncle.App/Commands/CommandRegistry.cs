// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.App.Commands;

/// <summary>What a command belongs to in the palette (renderer/app.js: the palette's groups).</summary>
public enum CommandGroup { Actions, GoTo, SoundSettings }

/// <summary>
/// One command (<c>docs/DESIGN.md</c> §5.4). A single list feeds Ctrl+K, keyboard accelerators,
/// context menus, tooltips, the shortcut list (<c>?</c>), the SMTC / taskbar buttons and the phone's
/// media actions, so nothing is wired by hand in a second place.
/// </summary>
public sealed record Command(
    string Id, string Title, string Icon, Action Execute, string? Shortcut = null,
    Func<bool>? CanExecute = null, string Sub = "", string Words = "", CommandGroup Group = CommandGroup.Actions);

/// <summary>The one command list. Ported from <c>paletteCommands()</c> in <c>renderer/app.js</c>.</summary>
public sealed class CommandRegistry
{
    private readonly List<Command> _commands = new();
    private readonly Dictionary<string, Command> _byId = new();

    public IReadOnlyList<Command> Commands => _commands;

    public void Add(Command c)
    {
        _commands.Add(c);
        _byId[c.Id] = c;
    }

    public Command? ById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Commands that can run now, in list order (the palette's "when" filter).</summary>
    public IEnumerable<Command> Available() => _commands.Where(c => c.CanExecute?.Invoke() ?? true);

    /// <summary>Runs a command if it is available. False when it does not exist or cannot run.</summary>
    public bool Run(string id)
    {
        var c = ById(id);
        if (c is null || !(c.CanExecute?.Invoke() ?? true)) return false;
        c.Execute();
        return true;
    }

    /// <summary>
    /// Ctrl+K search: matches the title, the shortcut, or the extra words, so "crossfade" and "mix
    /// similar" both find what the person means. Available commands only.
    /// </summary>
    public IEnumerable<Command> Search(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return Available();
        return Available().Where(c => Contains(c.Title, q) || Contains(c.Shortcut ?? "", q) || Contains(c.Words, q));
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>The shortcut list shown by <c>?</c>, from the same registry (K3).</summary>
    public IEnumerable<(string Shortcut, string Title)> Shortcuts() =>
        _commands.Where(c => !string.IsNullOrEmpty(c.Shortcut)).Select(c => (c.Shortcut!, c.Title));
}
