using UnityEngine;

public class UI_Options : MonoBehaviour
{
    public void SaveAndExit()
    {
        if (GameSimulationClock.IsNetwork) return;
        SaveManager.instance.SaveGame();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
