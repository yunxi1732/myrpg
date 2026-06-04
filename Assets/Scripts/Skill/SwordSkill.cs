using System;
using UnityEngine;
using UnityEngine.UI;

public enum SwordType
{
    Regular,
    Bounce,
    Pierce,
    Spin
}

public class SwordSkill : Skill
{
    public SwordType swordType = SwordType.Regular;

    [Header("Bounce Info")]
    [SerializeField] private UI_SkillTreeSlot bounceUnlockButton;
    [SerializeField] private int bounceAmount;
    [SerializeField] private float bounceGravity;
    [SerializeField] private float bounceSpeed;

    [Header("Pierce Info")]
    [SerializeField] private UI_SkillTreeSlot pierceUnlockButton;
    [SerializeField] private int pierceAmount;
    [SerializeField] private float pierceGravity;

    [Header("Spin Info")]
    [SerializeField] private UI_SkillTreeSlot spinUnlockButton;
    [SerializeField] private float hitCooldown = 0.33f;
    [SerializeField] private float maxTravelDistance = 7;
    [SerializeField] private float spinDuration = 2;
    [SerializeField] private float spinGravity = 1;

    [Header("Sword Info")]
    [SerializeField] private UI_SkillTreeSlot swordUnlockButton;
    public bool swordUnlocked { get; private set; }
    [SerializeField] private GameObject swordPrefab;
    [SerializeField] private Vector2 launchingForce;
    [SerializeField] private float swordGravity;
    [SerializeField] private float freezeDuration = 0.7f;
    [SerializeField] private float returnSpeed;

    [Header("Passive skills")]
    [SerializeField] private UI_SkillTreeSlot timeStopUnlockButton;
    public bool timeStopUnlocked { get; private set; }
    [SerializeField] private UI_SkillTreeSlot vulnerableUnlockButton;
    public bool vulnerableUnlocked { get; private set; }

    private Vector2 finalDir;

    [Header("Aim Dots Info")]
    [SerializeField] private int numberOfDots;
    [SerializeField] private float spaceBetweenDots;
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private Transform dotsParent;

    private GameObject[] dots;

    protected override void Start()
    {
        base.Start();
        GenerateDots();
        SetUpGravity();

        swordUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockSword);
        spinUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockSpinSword);
        bounceUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockBounceSword);
        swordUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockSword);
        timeStopUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockTimeStop);
        vulnerableUnlockButton.GetComponent<Button>().onClick.AddListener(UnlockVulnerable);
    }

    private void SetUpGravity()
    {
        if (swordType == SwordType.Bounce) swordGravity = bounceGravity;
        else if (swordType == SwordType.Pierce) swordGravity = pierceGravity;
        else if (swordType == SwordType.Spin) swordGravity = spinGravity;
    }
    protected override void Update()
    {
        if (Input.GetKeyUp(KeyCode.Mouse1)) finalDir = new Vector2(AimDirection().normalized.x * launchingForce.x, AimDirection().normalized.y * launchingForce.y);

        if (Input.GetKey(KeyCode.Mouse1))
        {
            for (int i = 0; i < dots.Length; i++) dots[i].transform.position = DotsPosition(i * spaceBetweenDots);
        }
    }
    public void CreateSword()
    {
        GameObject newSword = Instantiate(swordPrefab, player.transform.position, transform.rotation);
        SwordSkillController newSwordController = newSword.GetComponent<SwordSkillController>();

        if (swordType == SwordType.Bounce) newSwordController.SetUpBounce(true, bounceAmount, bounceSpeed);
        else if (swordType == SwordType.Pierce) newSwordController.SetUpPierce(pierceAmount);
        else if (swordType == SwordType.Spin) newSwordController.SetUpSpin(true, maxTravelDistance, spinDuration, hitCooldown);

        newSwordController.SetUpSword(finalDir, swordGravity, player, freezeDuration, returnSpeed);
        player.AssignNewSword(newSword);
        DotsActive(false);
    }

    #region Unlock region

    private void UnlockTimeStop()
    {
        if (timeStopUnlockButton.unlocked == true)
            timeStopUnlocked = true;
    }

    private void UnlockVulnerable()
    {
        if (vulnerableUnlockButton.unlocked == true)
            vulnerableUnlocked = true;
    }
    private void UnlockSword()
    {
        if (swordUnlockButton.unlocked == true)
        {
            swordUnlocked = true;
            swordType = SwordType.Regular;
        }
    }

    private void UnlockBounceSword()
    {
        if (bounceUnlockButton.unlocked == true)
            swordType = SwordType.Bounce;
    }

    private void UnlockSpinSword()
    {
        if (spinUnlockButton.unlocked == true)
            swordType = SwordType.Spin;
    }

    private void UnlockPierceSword()
    {
        if (pierceUnlockButton.unlocked == true)
            swordType = SwordType.Pierce;
    }

    #endregion

    #region Aim region

    private Vector2 AimDirection()
    {
        Vector2 playerPosition = player.transform.position;
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return mousePosition - playerPosition;
    }

    public void DotsActive(bool _isActive)
    {
        for (int i = 0; i < dots.Length; i++) dots[i].SetActive(_isActive);
    }

    private void GenerateDots()
    {
        dots = new GameObject[numberOfDots];
        for (int i = 0; i < numberOfDots; i++)
        {
            dots[i] = Instantiate(dotPrefab, transform.position, Quaternion.identity, dotsParent);
            dots[i].SetActive(false);
        }
    }

    private Vector2 DotsPosition(float t)
    {
        Vector2 position = (Vector2)player.transform.position + new Vector2(
            AimDirection().normalized.x * launchingForce.x,
            AimDirection().normalized.y * launchingForce.y) * t + 0.5f * (Physics2D.gravity * swordGravity) * t * t;
        return position;
    }
#endregion
}
