using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class ItemEffect : ScriptableObject
{
    [InspectorTextArea]
    public string effectDescription;

    public virtual void ExecuteEffect(Transform _enemyPosition)
    {
        Debug.Log("effect executed");
    }
}
