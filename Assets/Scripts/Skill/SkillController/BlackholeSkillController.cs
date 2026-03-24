using System;
using System.Collections.Generic;
using UnityEngine;

public class BlackholeSkillController : MonoBehaviour
{
    [SerializeField] private GameObject hotkeyPrefab;
    [SerializeField] private List<KeyCode> keycodeList;

    private float maxSize;
    private float growSpeed;
    private bool canGrow = true;
    private float shrinkSpeed;
    private bool canShrink;

    private List<Transform> targets = new List<Transform>();
    private List<GameObject> createdHotkey = new List<GameObject>();

    private bool cloneAttackReleased = false;
    private int amountOfAttack = 4;
    private float cloneAttackCooldown = 0.3f;
    private float cloneAttackTimer;
    private bool canCreateHotkey = true;
    private float blackholeTimer;
    private bool playerCanDisappear = true;

    public bool playerCanExitState { get; private set; }

    public void SetUpBlackhole(int _amountOfAttack, float _maxSize, float _growSpeed, float _shrinkSpeed, float _cloneAttackCooldown, float _blackholeDuration)
    {
        amountOfAttack = _amountOfAttack;
        maxSize = _maxSize;
        growSpeed = _growSpeed;
        shrinkSpeed = _shrinkSpeed;
        cloneAttackCooldown = _cloneAttackCooldown;
        blackholeTimer = _blackholeDuration;

        if (SkillManager.instance.clone.crystalInsteadOfClone) playerCanDisappear = false;
    }

    private void Update()
    {
        cloneAttackTimer -= Time.deltaTime;
        blackholeTimer -= Time.deltaTime;
        if (blackholeTimer < 0)
        {
            blackholeTimer = Mathf.Infinity;
            if (targets.Count > 0) ReleaseCloneAttack();
            else FinishBlackholeAbility();
        }

        if (Input.GetKeyDown(KeyCode.R)) ReleaseCloneAttack();

        CloneAttackLogic();

        if (canGrow && !canShrink)
        {
            transform.localScale = Vector2.Lerp(transform.localScale, new Vector2(maxSize, maxSize), growSpeed * Time.deltaTime);
        }
        if (canShrink)
        {
            transform.localScale = Vector2.Lerp(transform.localScale, new Vector2(-1, -1), shrinkSpeed * Time.deltaTime);
            if (transform.localScale.x <= 0) Destroy(gameObject);
        }
    }

    private void ReleaseCloneAttack()
    {
        if (targets.Count <= 0) return;

        cloneAttackReleased = true;
        canCreateHotkey = false;
        DestroyHotkey();

        if (playerCanDisappear)
        {
            playerCanDisappear = false;
            PlayerManager.instance.player.MakeTransparent(true);
        }
    }

    private void CloneAttackLogic()
    {
        if (cloneAttackTimer < 0 && cloneAttackReleased && amountOfAttack > 0)
        {
            cloneAttackTimer = cloneAttackCooldown;
            int randomIndex = UnityEngine.Random.Range(0, targets.Count);

            float xOffset;
            if (UnityEngine.Random.Range(0, 100) < 50) xOffset = 2;
            else xOffset = -2;

            if (SkillManager.instance.clone.crystalInsteadOfClone)
            {
                SkillManager.instance.crystal.CreateCrystal();
                SkillManager.instance.crystal.CurrentCrystalChooseRandomEnemy();
            }
            else SkillManager.instance.clone.CreateClone(targets[randomIndex], new Vector3(xOffset, 0));
            amountOfAttack--;
            if (amountOfAttack <= 0)
            {
                Invoke("FinishBlackholeAbility", 0.5f);
            }
        }
    }

    private void FinishBlackholeAbility()
    {
        DestroyHotkey();
        playerCanExitState = true;
        cloneAttackReleased = false;
        canShrink = true;
    }

    private void DestroyHotkey()
    {
        for (int i = 0; i < createdHotkey.Count; i++) Destroy(createdHotkey[i]);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Enemy enemy = collision.GetComponent<Enemy>();
        if (enemy)
        {
            enemy.FreezeTime(true);
            CreateHotkey(collision);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        Enemy enemy = collision.GetComponent<Enemy>();
        if (enemy) enemy.FreezeTime(false);
    }

    private void CreateHotkey(Collider2D collision)
    {
        if (keycodeList.Count <= 0 || !canCreateHotkey) return;
        GameObject newHotkey = Instantiate(hotkeyPrefab, collision.transform.position + new Vector3(0, 2), Quaternion.identity);
        createdHotkey.Add(newHotkey);
        KeyCode chosenKey = keycodeList[UnityEngine.Random.Range(0, keycodeList.Count)];
        keycodeList.Remove(chosenKey);
        BlackholeHotkeyController newHotkeyScript = newHotkey.GetComponent<BlackholeHotkeyController>();
        newHotkeyScript.SetUpHotkey(chosenKey, collision.transform, this);
    }

    public void AddEnemyToList(Transform _enemy) => targets.Add(_enemy);
}
