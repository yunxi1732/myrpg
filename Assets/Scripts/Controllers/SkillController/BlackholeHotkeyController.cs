using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BlackholeHotkeyController : MonoBehaviour
{
    internal Rpg.Networking.RoomBlackholeNetwork Room;
    internal int TargetEntityId;
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
    internal void ConfirmSelection()
    {
        myText.color = Color.clear;
        sr.color = Color.clear;
    }

    private void Update()
    {
        if (!GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(myHotkey))
        {
            if (Room != null)
            {
                Room.SendSelection(TargetEntityId, myHotkey);
                return;
            }
            blackhole.AddEnemyToList(myEnemy);
            ConfirmSelection();
        }
    }
}
