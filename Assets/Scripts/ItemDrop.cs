using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Rpg.Networking;

public class ItemDrop : MonoBehaviour
{
    [SerializeField] private int possibleItemDrop;
    [SerializeField] private ItemData[] possibleDrop;
    private List<ItemData> dropList = new List<ItemData>();

    [SerializeField] private GameObject dropPrefab;
    internal ItemData[] PossibleDrops => possibleDrop;
    internal GameObject DropPrefab => dropPrefab;

    public virtual void GenerateDrop()
        => SelectDrops((item, velocity) => SpawnItem(item, velocity));

    internal void SelectDrops(System.Action<ItemData, Vector2> spawn)
    {
        if (!EntityRuntimePermissions.CanSimulate(this)) return;
        dropList.Clear();

        for (int i = 0; i < possibleDrop.Length; i++)
        {
            if (Random.Range(0, 100) <= possibleDrop[i].dropChance)
                dropList.Add(possibleDrop[i]);
        }

        for (int i = 0; i < possibleItemDrop && dropList.Count > 0; i++)
        {
            ItemData randomItem = dropList[Random.Range(0, Mathf.Max(dropList.Count - 1, 0))];

            dropList.Remove(randomItem);
            Vector2 velocity = new Vector2(Random.Range(-5, 5), Random.Range(15, 20));
            spawn(randomItem, velocity);
        }
    }

    protected void DropItem(ItemData _itemData)
    {
        if (!EntityRuntimePermissions.CanSimulate(this)) return;
        Vector2 randomVelocity = new Vector2(Random.Range(-5, 5), Random.Range(15, 20));
        SpawnItem(_itemData, randomVelocity);
    }

    private void SpawnItem(ItemData item, Vector2 velocity)
    {
        GameObject newDrop = Instantiate(dropPrefab, transform.position, Quaternion.identity);
        newDrop.GetComponent<ItemObject>().SetUpItem(item, velocity);
    }
}
