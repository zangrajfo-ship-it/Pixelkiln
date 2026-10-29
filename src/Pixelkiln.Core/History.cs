namespace Pixelkiln.Core;

/// <summary>
/// Snapshot undo/redo. Push a snapshot before every change. With a size function the steps also share a memory
/// budget: the oldest steps go first, but the newest one is always kept.
/// </summary>
// ponytail: full document copy per step; the budget bounds memory for big sprites. Switch to diffs if a budget's worth of steps is too few.
public sealed class History<T>(Func<T, T> clone, int max = 100, Func<T, long>? sizeOf = null, long maxBytes = History.DefaultBudget)
{
    readonly List<T> _undo = [], _redo = [];

    /// <summary>Bytes held by undo and redo steps (0 without a size function).</summary>
    public long Bytes => sizeOf is null ? 0 : _undo.Concat(_redo).Sum(sizeOf);
    public int UndoCount => _undo.Count;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(T state)
    {
        _undo.Add(clone(state));
        if (_undo.Count > max) _undo.RemoveAt(0);
        _redo.Clear();
        if (sizeOf is null) return;
        long total = _undo.Sum(sizeOf);
        while (_undo.Count > 1 && total > maxBytes)
        {
            total -= sizeOf(_undo[0]);
            _undo.RemoveAt(0);
        }
    }

    public T? Undo(T current) => Swap(_undo, _redo, current);
    public T? Redo(T current) => Swap(_redo, _undo, current);

    public void Clear() { _undo.Clear(); _redo.Clear(); }

    T? Swap(List<T> from, List<T> to, T current)
    {
        if (from.Count == 0) return default;
        to.Add(clone(current));
        var s = from[^1];
        from.RemoveAt(from.Count - 1);
        return s;
    }
}

public static class History
{
    /// <summary>Default memory for undo steps of one document.</summary>
    public const long DefaultBudget = 512L * 1024 * 1024;
}
