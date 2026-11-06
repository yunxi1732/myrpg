using System.Collections.Generic;
using UnityEngine;

/// <summary>Keep this controller active; only the separate list container is hidden when empty.</summary>
public sealed class InteractionListView : MonoBehaviour
{
    [SerializeField] private RectTransform container;
    [SerializeField] private InteractionOptionView rowPrefab;
    private InteractionManager manager;
    private bool subscribed;
    private readonly List<InteractionOptionView> rows = new List<InteractionOptionView>();

    public void Bind(InteractionManager value)
    {
        Unsubscribe();
        manager = value != null && !value.IsDisposed ? value : null;
        if (isActiveAndEnabled) Subscribe();
        else ClearRows();
    }

    private void OnEnable() => Subscribe();

    private void Subscribe()
    {
        if (manager != null && manager.IsDisposed) manager = null;
        if (manager != null && !subscribed)
        {
            manager.OnListChanged += Refresh;
            manager.OnSelectionChanged += RefreshSelection;
            manager.OnDisposed += OnManagerDisposed;
            subscribed = true;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (container == null || rowPrefab == null)
        {
            Debug.LogError("[InteractionListView] 请绑定列表容器和 InteractionOption 预制体。", this);
            return;
        }
        int count = manager == null ? 0 : manager.Options.Count;
        while (rows.Count > count) RemoveLastRow();
        for (int i = 0; i < count; i++)
        {
            if (i == rows.Count) rows.Add(Instantiate(rowPrefab, container, false));
            rows[i].SetData(manager.Options[i]);
        }
        container.gameObject.SetActive(count > 0);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        for (int i = 0; i < rows.Count; i++)
            rows[i].SetSelected(manager != null && i == manager.SelectedIndex);
    }

    private void RemoveLastRow()
    {
        int index = rows.Count - 1;
        InteractionOptionView row = rows[index];
        rows.RemoveAt(index);
        if (row == null) return;
        row.SetData(null);
        row.gameObject.SetActive(false);
        row.transform.SetParent(null, false);
        if (Application.isPlaying) Destroy(row.gameObject);
        else DestroyImmediate(row.gameObject);
    }

    private void ClearRows()
    {
        while (rows.Count > 0) RemoveLastRow();
        if (container != null) container.gameObject.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (subscribed && manager != null) manager.OnListChanged -= Refresh;
        if (subscribed && manager != null) manager.OnSelectionChanged -= RefreshSelection;
        if (subscribed && manager != null) manager.OnDisposed -= OnManagerDisposed;
        subscribed = false;
    }

    private void OnManagerDisposed() => Bind(null);

    private void OnDisable()
    {
        Unsubscribe();
        ClearRows();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        ClearRows();
        manager = null;
    }
}
