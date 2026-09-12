using System.Collections.Generic;

/// <summary>
/// Fixed-capacity undo history with a cursor.
///
/// This is the cheapest high-value feature in the exploration rig: during a
/// randomise search the look you wanted is routinely the one two presses ago,
/// and without a history it is gone for good. Twenty entries costs nothing.
///
/// Pushing after walking back truncates the forward branch, which is standard
/// undo semantics — the alternative (keeping an orphaned redo stack that a new
/// press can no longer reach coherently) is more confusing than losing it.
/// </summary>
public class HistoryRing<T>
{
    private readonly List<T> items = new List<T>();
    private readonly int capacity;
    private int cursor = -1;

    public int Count => items.Count;

    public HistoryRing(int capacity)
    {
        this.capacity = System.Math.Max(1, capacity);
    }

    public void Push(T item)
    {
        // Truncate the forward branch.
        if (cursor >= 0 && cursor < items.Count - 1)
            items.RemoveRange(cursor + 1, items.Count - cursor - 1);

        items.Add(item);

        if (items.Count > capacity)
            items.RemoveAt(0);

        cursor = items.Count - 1;
    }

    public bool TryBack(out T item)
    {
        if (cursor <= 0) { item = default; return false; }
        cursor--;
        item = items[cursor];
        return true;
    }

    public bool TryForward(out T item)
    {
        if (cursor < 0 || cursor >= items.Count - 1) { item = default; return false; }
        cursor++;
        item = items[cursor];
        return true;
    }
}