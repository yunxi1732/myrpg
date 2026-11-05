using System;
using System.Collections.Generic;

/// <summary>Scene-owned candidates and selection. Input polling and presentation remain outside.</summary>
public sealed class InteractionManager
{
    private readonly List<InteractionOptionData> options = new List<InteractionOptionData>();
    public IReadOnlyList<InteractionOptionData> Options { get; }
    public event Action OnListChanged;
    public event Action OnSelectionChanged;
    public int SelectedIndex { get; private set; } = -1;
    private bool executing;

    public InteractionManager()
    {
        Options = options.AsReadOnly();
    }

    /// <summary>Duplicates are ignored, preserving the original item and insertion order.</summary>
    public bool Register(InteractionOptionData data)
    {
        if (data == null) return false;
        foreach (InteractionOptionData option in options)
            if (ReferenceEquals(option.owner, data.owner) &&
                string.Equals(option.id, data.id, StringComparison.Ordinal)) return false;
        var previous = GetSelected();
        int previousIndex = SelectedIndex;
        options.Add(data);
        NotifyListChanged(previous, previousIndex);
        return true;
    }

    /// <summary>Remove this exact registration. A stale/duplicate object cannot remove another item.</summary>
    public bool Unregister(InteractionOptionData data)
    {
        var previous = GetSelected();
        int previousIndex = SelectedIndex;
        if (data == null || !options.Remove(data)) return false;
        NotifyListChanged(previous, previousIndex);
        return true;
    }

    public int UnregisterOwner(object owner)
    {
        if (ReferenceEquals(owner, null)) return 0;
        var previous = GetSelected();
        int previousIndex = SelectedIndex;
        int count = options.RemoveAll(option => ReferenceEquals(option.owner, owner));
        if (count > 0) NotifyListChanged(previous, previousIndex);
        return count;
    }

    public bool Clear()
    {
        if (options.Count == 0) return false;
        var previous = GetSelected();
        int previousIndex = SelectedIndex;
        options.Clear();
        NotifyListChanged(previous, previousIndex);
        return true;
    }

    public InteractionOptionData GetSelected() =>
        SelectedIndex >= 0 && SelectedIndex < options.Count ? options[SelectedIndex] : null;

    public bool MoveSelection(int direction)
    {
        if (direction == 0 || options.Count == 0) return false;
        int next = (SelectedIndex + (direction > 0 ? 1 : -1) + options.Count) % options.Count;
        if (next == SelectedIndex) return false;
        SelectedIndex = next;
        OnSelectionChanged?.Invoke();
        return true;
    }

    public bool TryExecuteSelected()
    {
        var selected = GetSelected();
        if (executing || selected == null || selected.onExecute == null) return false;
        executing = true;
        try { selected.onExecute(); }
        finally { executing = false; }
        return true;
    }

    private void NotifyListChanged(InteractionOptionData previous, int previousIndex)
    {
        int retained = previous == null ? -1 : options.IndexOf(previous);
        SelectedIndex = options.Count == 0 ? -1 : retained >= 0 ? retained :
            Math.Max(0, Math.Min(previousIndex, options.Count - 1));
        bool changed = previousIndex != SelectedIndex || !ReferenceEquals(previous, GetSelected());
        OnListChanged?.Invoke();
        if (changed) OnSelectionChanged?.Invoke();
    }
}
