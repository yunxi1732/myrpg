using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]

public class Stat 
{
    [SerializeField] private int baseValue;

    public List<int> modifiers;

    public int GetValue()
    {
        int finalValue = baseValue;
        foreach (int modifier in modifiers) finalValue += modifier;
        return finalValue;
    }

    public void AddModifier(int _modify)
    {
        modifiers.Add(_modify);
    }

    public void RemoveModifier(int _modify)
    {
        modifiers.RemoveAt(_modify);
    }
}
