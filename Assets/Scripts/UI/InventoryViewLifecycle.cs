using System;
using UnityEngine;

/// <summary>Forwards existing slot-parent visibility changes; requires no Inspector binding.</summary>
public sealed class InventoryViewLifecycle : MonoBehaviour
{
    public Action Changed;
    private void OnEnable() => Changed?.Invoke();
    private void OnDisable() => Changed?.Invoke();
}
