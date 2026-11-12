using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour, ISaveManager
{
    public static PlayerManager instance;
    public Player player;

    public int currency;

    private void Awake()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) { enabled = false; return; }
        if (instance != null) Destroy(instance.gameObject);
        else instance = this;
    }

    public bool HaveEnoughMoney(int _price)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return false;
        if (_price > currency)
        {
            Debug.Log("don't have enough money");
            return false;
        }

        currency -= _price;
        return true;
    }

    public int GetCurrency() => currency;

    public void GrantMonsterCurrency(int amount) => currency = checked(currency + amount);

    public void LoadData(GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        this.currency = _data.currency;
    }

    public void SaveData(ref GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        _data.currency = this.currency;
    }
}
