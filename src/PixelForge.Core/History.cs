namespace PixelForge.Core;

/// <summary>Snapshot undo/redo. Push a snapshot before every change.</summary>
// ponytail: full document copy per step, cap 100; switch to diffs if documents get big.
public sealed class History<T>(Func<T, T> clone, int max = 100)
{
    readonly List<T> _undo = [], _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(T state)
    {
        _undo.Add(clone(state));
        if (_undo.Count > max) _undo.RemoveAt(0);
        _redo.Clear();
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
