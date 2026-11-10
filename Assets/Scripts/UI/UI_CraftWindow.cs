using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_CraftWindow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI itemName;
    [SerializeField] private TextMeshProUGUI itemDescription;
    [SerializeField] private Image itemIcon;
    [SerializeField] private Button craftButton;
    [SerializeField] private Image[] materialImage;
    private Rpg.Networking.RoomEntityPresentation networkEntity;
    public void BindNetwork(Rpg.Networking.RoomEntityPresentation entity)
    {
        networkEntity = entity;
        if (craftButton != null) craftButton.interactable = entity != null && entity.IsValid && entity.IsReady;
    }

    public void SetupCraftWindow(ItemData_Equipment _data)
    {
        if (_data == null) return;
        craftButton.onClick.RemoveAllListeners();

        for (int i = 0; i < materialImage.Length; i++)
        {
            materialImage[i].color = Color.clear;
            materialImage[i].GetComponentInChildren<TextMeshProUGUI>().color = Color.clear;
        }

        for (int i = 0; _data.craftingMaterials != null && i < _data.craftingMaterials.Count && i < materialImage.Length; i++)
        {
            if (_data.craftingMaterials.Count > materialImage.Length)
                Debug.LogWarning("you have more materials amount than you have slots in craft window");

            materialImage[i].sprite = _data.craftingMaterials[i].data.itemIcon;
            materialImage[i].color = Color.white;

            TextMeshProUGUI materialSlotText = materialImage[i].GetComponentInChildren<TextMeshProUGUI>();

            materialSlotText.text = _data.craftingMaterials[i].stackSize.ToString();
            materialSlotText.color = Color.white;
        }

        itemIcon.sprite = _data.itemIcon;
        itemName.text = _data.itemName;
        itemDescription.text = _data.GetDescription();

        craftButton.interactable = !Rpg.Networking.RoomSceneCoordinator.ProtectSaves || networkEntity != null && networkEntity.IsValid && networkEntity.IsReady;
        craftButton.onClick.AddListener(() =>
        {
            if (Rpg.Networking.RoomSceneCoordinator.ProtectSaves)
            {
                Rpg.Networking.RoomSceneCoordinator.Active.RequestLocalCraft(networkEntity, _data);
                return;
            }
            Inventory.instance.canCraft(_data, _data.craftingMaterials);
        });
    }
}
