// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.App.Notifications;

/// <summary>
/// How loudly a message is shown (<c>docs/DESIGN.md</c> §5.5). <see cref="Silent"/> is for state the
/// control already shows (shuffle, repeat, sleep set); it is recorded but never surfaced.
/// </summary>
public enum NoticeLevel { Silent = 0, Info = 1, Action = 2, Important = 3 }

/// <summary>One message: text, level, and an optional single action (Undo / Turn on / Choose).</summary>
public sealed record Notice(string Message, NoticeLevel Level = NoticeLevel.Info, string? ActionLabel = null, Action? Action = null, long AtMs = 0);

/// <summary>
/// One notification slot with levels, replacing the app's 88 direct <c>toast()</c> calls
/// (<c>docs/DESIGN.md</c> §5.5). Messages queue by priority, duplicates within a few seconds merge,
/// and an action message is never replaced by an info one. Silent notices are dropped: the UI
/// already shows that state.
/// </summary>
public sealed class NotificationService
{
    public const int MergeWindowMs = 2500;

    private readonly Func<long> _now;
    private readonly List<Notice> _queue = new();

    public NotificationService(Func<long>? now = null) => _now = now ?? (() => Environment.TickCount64);

    /// <summary>The message currently on screen, or null.</summary>
    public Notice? Current { get; private set; }

    /// <summary>Raised whenever <see cref="Current"/> changes (show or clear).</summary>
    public event Action<Notice?>? Changed;

    /// <summary>Pushes a message. Returns true if it is now showing.</summary>
    public bool Push(string message, NoticeLevel level = NoticeLevel.Info, string? actionLabel = null, Action? action = null)
    {
        if (level == NoticeLevel.Silent) return false;
        var at = _now();
        // a duplicate of what is already showing merges (no re-show)
        if (Current is not null && Current.Message == message && at - Current.AtMs <= MergeWindowMs) return true;
        // a duplicate already queued merges into the queued one
        var dup = _queue.FindIndex(n => n.Message == message && at - n.AtMs <= MergeWindowMs);
        if (dup >= 0)
        {
            if (level > _queue[dup].Level) _queue[dup] = _queue[dup] with { Level = level, ActionLabel = actionLabel, Action = action };
            return Current is not null && Current.Message == message;
        }

        var notice = new Notice(message, level, actionLabel, action, at);
        if (Current is null || level > Current.Level)
        {
            if (Current is not null) _queue.Insert(0, Current);
            Show(notice);
        }
        else
        {
            _queue.Add(notice);
            SortQueue();
        }
        return ReferenceEquals(Current, notice);
    }

    /// <summary>Dismisses what is showing; the highest-priority queued message takes its place.</summary>
    public void Dismiss()
    {
        if (_queue.Count == 0) { Show(null); return; }
        var next = _queue[0];
        _queue.RemoveAt(0);
        Show(next);
    }

    public void Clear()
    {
        _queue.Clear();
        Show(null);
    }

    private void SortQueue() => _queue.Sort((a, b) => b.Level.CompareTo(a.Level) is var byLevel && byLevel != 0 ? byLevel : a.AtMs.CompareTo(b.AtMs));

    private void Show(Notice? n)
    {
        Current = n;
        Changed?.Invoke(n);
    }
}
