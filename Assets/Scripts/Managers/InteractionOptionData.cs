using System;
using UnityEngine;

/// <summary>One immutable candidate. Identity is owner reference + case-sensitive ID.</summary>
public sealed class InteractionOptionData
{
    public readonly object owner;
    public readonly string id;
    public readonly string displayName;
    public readonly string keyTip;
    public readonly KeyCode key;
    public readonly Action onExecute;

    public InteractionOptionData(object owner, string id, string displayName, Action onExecute,
        string keyTip = "E", KeyCode key = KeyCode.E)
    {
        if (ReferenceEquals(owner, null)) throw new ArgumentNullException(nameof(owner));
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("交互选项 ID 不能为空。", nameof(id));
        this.owner = owner;
        this.id = id;
        this.displayName = displayName ?? "";
        this.keyTip = keyTip ?? "";
        this.key = key;
        this.onExecute = onExecute;
    }
}
