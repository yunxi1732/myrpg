using System;
using UnityEngine;
using UnityEngine.UI;
using Rpg.Networking;
using Rpg.Networking.Protocol;

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
    private bool throwRequested;
    internal bool ThrowPending => throwRequested;
    internal GameObject SwordPrefab => swordPrefab;

    [Header("Aim Dots Info")]
    [SerializeField] private int numberOfDots;
    [SerializeField] private float spaceBetweenDots;
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private Transform dotsParent;

    private GameObject[] dots;
    internal RoomSwordNetwork RoomNetwork { get; private set; }
    internal Vector2 RoomDirection;
    private bool localAim;
    private long localActionSeq;
    private double lastAimSend;
    private Vector2 lastDirection;
    private bool ActionLocked => RoomNetwork == null ? GameplayControlLock.IsActionInputBlocked :
        player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked;

    public SwordSkill CreateRoomAdapter(Player owner, RoomSwordNetwork room)
    {
        var adapter = new GameObject("Room SwordSkill").AddComponent<SwordSkill>();
        adapter.transform.SetParent(owner.transform, false);
        adapter.RoomNetwork = room; adapter.BindRoomOwner(owner.Combat);
        adapter.swordPrefab = swordPrefab; adapter.launchingForce = launchingForce; adapter.swordGravity = swordGravity;
        adapter.returnSpeed = returnSpeed; adapter.freezeDuration = freezeDuration; adapter.cooldown = cooldown;
        adapter.dotPrefab = dotPrefab; adapter.numberOfDots = numberOfDots; adapter.spaceBetweenDots = spaceBetweenDots; adapter.dotsParent = adapter.transform;
        adapter.RoomDirection = Vector2.right; adapter.swordType = SwordType.Regular;
        if (owner.IsAuthoritative) owner.Combat.Skills.Configure<SwordSkill>(cooldown);
        return adapter;
    }

    protected override void Start()
    {
        if (RoomNetwork != null) { if (player.IsLocalInputOwner) GenerateDots(); return; }
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
        if (RoomNetwork != null) { if (player.IsLocalInputOwner) UpdateRoomInput(); return; }
        if (GameplayControlLock.IsActionInputBlocked)
        {
            CancelAim();
            return;
        }
        if (player.stateMachine.currentState == player.aimSwordState && Input.GetKey(KeyCode.Mouse1))
        {
            for (int i = 0; i < dots.Length; i++) dots[i].transform.position = DotsPosition(i * spaceBetweenDots);
        }
    }

    public void BeginAim()
    {
        if (ActionLocked) { CancelAim(); return; }
        throwRequested = false;
        finalDir = Vector2.zero;
        DotsActive(true);
    }

    public bool RequestThrow()
    {
        return RequestThrow(AimDirection().normalized);
    }
    internal bool RequestThrow(Vector2 direction)
    {
        if (ActionLocked) { CancelAim(); return false; }
        finalDir = Vector2.Scale(direction, launchingForce);
        throwRequested = true;
        return true;
    }

    public void CancelAim()
    {
        throwRequested = false;
        finalDir = Vector2.zero;
        DotsActive(false);
    }

    // Animation events may arrive after cancellation. Only a fresh, permitted release can throw.
    public bool TryCreateSwordFromInput()
    {
        if (ActionLocked || RoomNetwork != null && player.stats.currentHealth <= 0) { CancelAim(); return false; }
        if (!throwRequested) return false;
        throwRequested = false;
        CreateSword();
        return true;
    }
    public void CreateSword()
    {
        if (RoomNetwork != null && !player.IsAuthoritative) return;
        GameObject newSword = Instantiate(swordPrefab, player.transform.position, transform.rotation);
        SwordSkillController newSwordController = newSword.GetComponent<SwordSkillController>();

        if (RoomNetwork != null) RoomNetwork.RegisterSword(player, newSword);
        else if (swordType == SwordType.Bounce) newSwordController.SetUpBounce(true, bounceAmount, bounceSpeed);
        else if (swordType == SwordType.Pierce) newSwordController.SetUpPierce(pierceAmount);
        else if (swordType == SwordType.Spin) newSwordController.SetUpSpin(true, maxTravelDistance, spinDuration, hitCooldown);

        newSwordController.SetUpSword(finalDir, swordGravity, player, freezeDuration, returnSpeed);
        if (RoomNetwork == null) AudioManager.instance.PlaySFX(12, null);
        player.AssignNewSword(newSword);
        DotsActive(false);
        if (RoomNetwork != null) { Combat.Skills.Commit<SwordSkill>(); RoomNetwork.Publish(); }
    }
    private void SendRoomInput(SwordAction action, Vector2 direction)
    {
        lastAimSend = MonotonicClock.Now; lastDirection = direction;
        RoomNetwork.SendInput(player, new SwordInputDto { roomSessionId = Combat.RoomSessionId, actionSeq = ++localActionSeq,
            action = action, direction = new[] { direction.x, direction.y } });
    }
    private void UpdateRoomInput()
    {
        if (ActionLocked || !Application.isFocused || player.stats.currentHealth <= 0)
        {
            if (localAim) SendRoomInput(SwordAction.Cancel, RoomDirection);
            localAim = false; DotsActive(false); return;
        }
        Vector2 direction = AimDirection().normalized;
        if (direction == Vector2.zero) direction = Vector2.right;
        if (Input.GetKeyDown(KeyCode.Mouse1))
        {
            if (RoomNetwork.HasSword(Combat.PlayerId)) SendRoomInput(SwordAction.Recall, direction);
            else if (Combat.Skills.Get<SwordSkill>().Unlocked) { localAim = true; SendRoomInput(SwordAction.Aim, direction); }
        }
        if (localAim && Input.GetKeyUp(KeyCode.Mouse1)) { localAim = false; SendRoomInput(SwordAction.Release, direction); }
        else if (localAim && MonotonicClock.Now - lastAimSend >= .04 &&
            ((direction - lastDirection).sqrMagnitude > .0001f || MonotonicClock.Now - lastAimSend >= .1)) SendRoomInput(SwordAction.Direction, direction);
        DotsActive(localAim);
        if (localAim) for (int i = 0; i < dots.Length; i++) dots[i].transform.position = DotsPosition(i * spaceBetweenDots);
    }

    #region Unlock region

    public override void CheckUnlock()
    {
        UnlockSword();
        UnlockBounceSword();
        UnlockSpinSword();
        UnlockPierceSword();
        UnlockTimeStop();
        UnlockVulnerable();
    }

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
        if (dots == null) return;
        for (int i = 0; i < dots.Length; i++)
            if (dots[i] != null) dots[i].SetActive(_isActive);
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
