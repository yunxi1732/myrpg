using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_SkillTreeSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISaveManager
{
    private UI ui;
    private Image skillImage;

    [SerializeField] private int skillCost;
    [SerializeField] private string skillName;
    [TextArea]
    [SerializeField] private string skillDescription;

    public bool unlocked;

    [SerializeField] private UI_SkillTreeSlot[] shouldBeUnlocked;
    [SerializeField] private UI_SkillTreeSlot[] shouldBeLocked;
    public string RoomNodeId => skillName;
    public int RoomCost => skillCost;
    public IReadOnlyList<UI_SkillTreeSlot> RoomRequired => shouldBeUnlocked ?? System.Array.Empty<UI_SkillTreeSlot>();
    public IReadOnlyList<UI_SkillTreeSlot> RoomExcluded => shouldBeLocked ?? System.Array.Empty<UI_SkillTreeSlot>();
    private Rpg.Networking.RoomEntityPresentation networkEntity;
    private bool networkBound;
    private Color lockedColor;
    private bool colorCaptured;
    public void BindNetwork(Rpg.Networking.RoomEntityPresentation entity)
    {
        networkBound = true; networkEntity = entity;
        if (skillImage == null) skillImage = GetComponent<Image>();
        if (!colorCaptured && skillImage != null) { lockedColor = skillImage.color; colorCaptured = true; }
        var combat = entity != null && entity.IsValid && entity.IsReady && entity.View != null &&
            entity.View.GetComponent<Rpg.Networking.NetworkEntityIdentity>()?.IsLocalInputOwner == true ? entity.View.GetComponent<Player>()?.Combat : null;
        if (combat == null) networkEntity = null;
        unlocked = combat?.IsRoomNodeUnlocked(skillName) == true;
        if (skillImage != null) skillImage.color = unlocked ? Color.white : lockedColor;
        var button = GetComponent<Button>();
        if (button != null) button.interactable = combat != null && combat.RoomNodeCost(skillName) >= 0;
    }

    private void OnValidate()
    {
        gameObject.name = "SkillTreeSlot_UI - " + skillName;
    }

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => UnlockSkillSlot());
    }

    private void Start()
    {
        skillImage = GetComponent<Image>();
        ui = GetComponentInParent<UI>();

        if ((networkBound || Rpg.Networking.EntityRuntimePermissions.NetworkActive)) { BindNetwork(networkEntity); return; }
        if (unlocked)
            skillImage.color = Color.white;
    }

    public void UnlockSkillSlot()
    {
        if (networkBound || Rpg.Networking.EntityRuntimePermissions.NetworkActive)
        {
            if (Rpg.Networking.RoomSceneCoordinator.Active != null)
                Rpg.Networking.RoomSceneCoordinator.Active.RequestLocalSkillUnlock(networkEntity, skillName);
            return; // Confirmation updates the icon. No solo currency or skill callbacks run here.
        }
        AudioManager.instance.PlaySFX(4, null);
        for (int i = 0; i < shouldBeUnlocked.Length; i++)
        {
            if (shouldBeUnlocked[i].unlocked == false)
            {
                Debug.Log("cannot unlock the skill");
                return;
            }
        }

        for (int i = 0;i < shouldBeLocked.Length; i++)
        {
            if (shouldBeLocked[i].unlocked == true)
            {
                Debug.Log("cannot unlock the skill");
                return;
            }
        }

        if (PlayerManager.instance.HaveEnoughMoney(skillCost) == false)
            return;

        unlocked = true;
        skillImage.color = Color.white;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (ui == null) ui = GetComponentInParent<UI>(true);
        int price = networkBound && networkEntity != null && networkEntity.IsValid && networkEntity.View != null ?
            networkEntity.View.GetComponent<Player>().Combat.RoomNodeCost(skillName) : skillCost;
        if (ui != null && ui.skillTooltip != null) ui.skillTooltip.ShowTooltip(skillDescription, skillName, System.Math.Max(0, price));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (ui != null && ui.skillTooltip != null) ui.skillTooltip.HideTooltip();
    }

    public void LoadData(GameData _data)
    {
        if (networkBound || !Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        if (_data.skillTree.TryGetValue(skillName, out bool value))
        {
            unlocked = value;
        }
    }

    public void SaveData(ref GameData _data)
    {
        if (networkBound || !Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        if (_data.skillTree.TryGetValue(skillName, out bool value))
        {
            _data.skillTree.Remove(skillName);
            _data.skillTree.Add(skillName, unlocked);
        }
        else
            _data.skillTree.Add(skillName, unlocked);
    }
}
