using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class DialogueOptionView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Text optionContent;
    [SerializeField] private Text textIndex;
    private Action<int> choose;
    private int index;

    public void Bind(int choiceIndex, string content, Action<int> onChoose)
    {
        Clear();
        index = choiceIndex;
        choose = onChoose;
        optionContent.text = content;
        if (textIndex != null) textIndex.text = (choiceIndex + 1).ToString();
        button.onClick.AddListener(Select);
    }

    private void Select() => choose?.Invoke(index);

    public void Clear()
    {
        choose = null;
        if (button != null) button.onClick.RemoveListener(Select);
    }

    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
}
