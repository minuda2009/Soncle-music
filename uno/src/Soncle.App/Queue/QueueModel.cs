// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.App.Queue;

/// <summary>Where a queue item came from (<c>docs/DESIGN.md</c> §5.2).</summary>
public enum QueueOrigin { Source, User, Flow, Autoplay }

/// <summary>A queue item: a track id plus its origin and a stable id for reordering/undo.</summary>
public sealed record QueueItem(string Qid, string TrackId, QueueOrigin Origin)
{
    /// <summary>Flow rows carry a tag (Familiar artist / Discovery / Key match), when known.</summary>
    public string? FlowTag { get; init; }
}

/// <summary>
/// The queue, with each item's origin. Replaces the JS app's parallel structures
/// (<c>P.queue</c> + <c>P.original</c> for shuffle + <c>P.flow.queue</c> + <c>P.flow.locked</c>),
/// giving "Up next" vs "Then from Flow" sections, Flow tags, "Flow stopped re-arranging", undo for
/// Clear queue, and shuffle as an ordering view over the same items (<c>docs/DESIGN.md</c> §5.2).
/// </summary>
public sealed class QueueModel
{
    private readonly List<QueueItem> _items = new();
    private readonly List<IReadOnlyList<QueueItem>> _undo = new();
    private int _counter;
    private List<QueueItem>? _originalOrder;

    /// <summary>The index of the item playing now, or -1.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>True once a manual edit has happened: Flow stops re-arranging the plan (F3).</summary>
    public bool FlowLocked { get; private set; }

    public bool Shuffle { get; private set; }

    public IReadOnlyList<QueueItem> Items => _items;

    public QueueItem? Current => Index >= 0 && Index < _items.Count ? _items[Index] : null;

    private string NextQid() => "q" + (++_counter).ToString("D4");

    private void Snapshot()
    {
        _undo.Add(_items.ToList());
        if (_undo.Count > 20) _undo.RemoveAt(0);
    }

    /// <summary>Replaces the queue with a source's tracks (starting an album/playlist/song).</summary>
    public void SetSource(IEnumerable<string> trackIds, QueueOrigin origin = QueueOrigin.Source)
    {
        Snapshot();
        _items.Clear();
        foreach (var id in trackIds) _items.Add(new QueueItem(NextQid(), id, origin));
        Index = _items.Count > 0 ? 0 : -1;
        FlowLocked = false;
        _originalOrder = null;
    }

    public void SetCurrentIndex(int index) => Index = index >= 0 && index < _items.Count ? index : -1;

    /// <summary>"Play next" (user origin) or "Add to queue" (user origin, at the end).</summary>
    public void Add(IEnumerable<string> trackIds, QueueOrigin origin = QueueOrigin.User, bool playNext = false)
    {
        Snapshot();
        var add = trackIds.Select(id => new QueueItem(NextQid(), id, origin)).ToList();
        if (playNext) _items.InsertRange(Math.Max(0, Index + 1), add);
        else _items.AddRange(add);
        if (origin == QueueOrigin.User) FlowLocked = true;
    }

    /// <summary>Appends what Flow/autoplay chose (never locks the plan).</summary>
    public void AppendFlow(IEnumerable<QueueItem> items) => _items.AddRange(items);

    public bool Remove(string qid)
    {
        var at = _items.FindIndex(x => x.Qid == qid);
        if (at < 0) return false;
        Snapshot();
        _items.RemoveAt(at);
        if (at < Index) Index--;
        else if (at == Index) Index = Math.Min(Index, _items.Count - 1);
        FlowLocked = true;
        return true;
    }

    /// <summary>Clear queue with Undo (Q1).</summary>
    public void Clear()
    {
        Snapshot();
        _items.Clear();
        Index = -1;
    }

    /// <summary>Puts the last edit back (Clear queue, a remove, a drag). False when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var prev = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _items.Clear();
        _items.AddRange(prev);
        Index = Math.Min(Index, _items.Count - 1);
        return true;
    }

    /// <summary>
    /// Shuffle is an ordering view over the same items (a stable permutation of the current order);
    /// turning it off returns to the original order because the permutation is kept.
    /// </summary>
    public void SetShuffle(bool on)
    {
        if (on == Shuffle) return;
        if (on)
        {
            _originalOrder = _items.ToList();
            var head = _items.Take(Index + 1).ToList();
            var rest = _items.Skip(Index + 1).OrderBy(_ => Random.Shared.Next()).ToList();
            _items.Clear();
            _items.AddRange(head);
            _items.AddRange(rest);
        }
        else if (_originalOrder is not null)
        {
            var cur = Current;
            _items.Clear();
            _items.AddRange(_originalOrder);
            Index = cur is null ? -1 : _items.FindIndex(x => x.Qid == cur.Qid);
        }
        Shuffle = on;
    }

    /// <summary>A manual drag locks the Flow plan and says so (F3).</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || from >= _items.Count || to < 0 || to >= _items.Count) return;
        Snapshot();
        var it = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, it);
        if (from == Index) Index = to;
        else if (from < Index && to >= Index) Index--;
        else if (from > Index && to <= Index) Index++;
        if (it.Origin == QueueOrigin.Flow) FlowLocked = true;
    }

    /// <summary>Resume Flow's re-arranging after a manual edit (F3).</summary>
    public void ResumeFlow() => FlowLocked = false;

    /// <summary>Up next = items after the current one that Flow did not add; Then from Flow = the rest.</summary>
    public (IReadOnlyList<QueueItem> UpNext, IReadOnlyList<QueueItem> ThenFlow) Sections()
    {
        var rest = _items.Skip(Index + 1).ToList();
        return (rest.Where(x => x.Origin != QueueOrigin.Flow).ToList(), rest.Where(x => x.Origin == QueueOrigin.Flow).ToList());
    }
}
