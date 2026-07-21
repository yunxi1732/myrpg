using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour, ISaveManager
{
    public static GameManager instance;
    [SerializeField] private Checkpoint[] checkpoints;
    [SerializeField] private string closestCheckpointId;

    private void Awake()
    {
        if (instance != null)
            Destroy(instance.gameObject);
        else
            instance = this;

        checkpoints = FindObjectsOfType<Checkpoint>();
    }

    public void RestartScene()
    {
        SaveManager.instance.SaveGame();
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.name);
    }

    public void LoadData(GameData _data)
    {
        foreach (KeyValuePair<string, bool> pair in _data.checkpoints)
        {
            foreach (Checkpoint c in checkpoints)
            {
                if (c.id == pair.Key && pair.Value == true)
                    c.ActiveCheckpoint();
            }
        }

        closestCheckpointId = _data.closestCheckpointId;
        Invoke("PlacePlayerAtClosestCheckpoint", 0.1f);
    }

    private void PlacePlayerAtClosestCheckpoint()
    {
        foreach (Checkpoint c in checkpoints)
        {
            if (closestCheckpointId == c.id)
            {
                PlayerManager.instance.player.transform.position = c.transform.position;
            }
        }
    }

    public void SaveData(ref GameData _data)
    {
        _data.closestCheckpointId = FindClosestCheckpoint().id;
        _data.checkpoints.Clear();

        foreach (Checkpoint checkpoint in checkpoints)
        {
            _data.checkpoints.Add(checkpoint.id, checkpoint.activationStatus);
        }
    }

    private Checkpoint FindClosestCheckpoint()
    {
        float closestDistance = Mathf.Infinity;
        Checkpoint closestCheckpoint = null;
        foreach (var checkpoint in checkpoints)
        {
            float distanceToCheckpoint = Vector2.Distance(PlayerManager.instance.player.transform.position, checkpoint.transform.position);
            if (distanceToCheckpoint < closestDistance && checkpoint.activationStatus == true)
            {
                closestDistance = distanceToCheckpoint;
                closestCheckpoint = checkpoint;
            }
        }
        return closestCheckpoint;
    }
}
