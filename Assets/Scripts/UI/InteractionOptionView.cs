using UnityEngine;
using UnityEngine.UI;

/// <summary>Display only: no input, selection or execution callbacks.</summary>
public sealed class InteractionOptionView : MonoBehaviour
{
    [SerializeField] private Text interactionContent;
    [SerializeField] private Text textKeyTip;
    private Image background;

    public void SetSelected(bool selected)
    {
        if (background == null) background = GetComponent<Image>();
        if (background != null) background.color = selected ? Color.white : new Color(0.26f, 0.26f, 0.26f, 1);
        if (interactionContent != null) interactionContent.color = selected ? Color.black : Color.white;
    }

    public void SetData(InteractionOptionData data)
    {
        if (interactionContent != null) interactionContent.text = data != null ? data.displayName : "";
        if (textKeyTip != null) textKeyTip.text = data != null ? data.keyTip : "";
    }
}
