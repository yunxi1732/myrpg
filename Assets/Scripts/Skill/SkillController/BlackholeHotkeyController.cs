using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BlackholeHotkeyController : MonoBehaviour
{
    private SpriteRenderer sr;
    private KeyCode myHotkey;
    private TextMeshProUGUI myText;
    private Transform myEnemy;
    private BlackholeSkillController blackhole;

    public void SetUpHotkey(KeyCode _myHotkey, Transform _myEnemy, BlackholeSkillController _myblackhole)
    {
        sr = GetComponent<SpriteRenderer>();
        myText = GetComponentInChildren<TextMeshProUGUI>();
        myHotkey = _myHotkey;
        myText.text = _myHotkey.ToString();

        myEnemy = _myEnemy;
        blackhole = _myblackhole;
    }

    private void Update()
    {
        if (Input.GetKeyDown(myHotkey))
        {
            blackhole.AddEnemyToList(myEnemy);
            myText.color = Color.clear;
            sr.color = Color.clear;
        }
    }
}
