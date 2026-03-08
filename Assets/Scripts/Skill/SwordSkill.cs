using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SwordSkill : Skill
{
    [Header("Sword Info")]
    [SerializeField] private GameObject swordPrefab;
    [SerializeField] private Vector2 launchingDir;
    [SerializeField] private float swordGravity;

    private Vector2 finalDir;

    public void CreateSword()
    {
        GameObject newSword = Instantiate(swordPrefab, player.transform.position, transform.rotation);
        SwordSkillController newSwordController = newSword.GetComponent<SwordSkillController>();

        newSwordController.SetUpSword(launchingDir, swordGravity);
    }

    private Vector2 AimDirection()
    {
        Vector2 playerPosition = player.transform.position;
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return mousePosition - playerPosition;
    }
}
