using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UI_SkillTooltip : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI skillText;
    [SerializeField] private TextMeshProUGUI skillName;

    public void ShowTooltip(string _text, string _name)
    {
        skillText.text = _text;
        skillName.text = _name;
        gameObject.SetActive(true);
    }

    public void HideTooltip() => gameObject.SetActive(false);
}
