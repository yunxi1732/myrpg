using System.Collections;
using Rpg.HotUpdate;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

// Save selection and single-player scene entry live with game code, not the built-in menu view.
public sealed class MenuSinglePlayerFlow
{
    private readonly UI_MainMenu menu;
    private readonly MenuRoomFlow room;

    public MenuSinglePlayerFlow(UI_MainMenu menu, MenuRoomFlow room)
    {
        this.menu = menu;
        this.room = room;
    }

    public void Initialize()
    {
        if (menu.ContinueButton != null && SaveManager.instance != null)
            menu.ContinueButton.SetActive(SaveManager.instance.HaveSavedData());
    }

    public void ContinueGame()
    {
        if (!CanEnter()) return;
        AudioManager.TryPlaySFX(4, null);
        menu.StartCoroutine(LoadSceneWithFadeEffect(1.5f));
    }

    public void NewGame()
    {
        if (!CanEnter()) return;
        AudioManager.TryPlaySFX(4, null);
        SaveManager.instance.DeleteSavedData();
        menu.StartCoroutine(LoadSceneWithFadeEffect(1.5f));
    }

    public void ExitGame()
    {
        AudioManager.TryPlaySFX(4, null);
        Debug.Log("Exit game!");
    }

    private bool CanEnter() => menu.ResourcesReady && !room.BlockSinglePlayerEntry();

    private IEnumerator LoadSceneWithFadeEffect(float delay)
    {
        menu.FadeOut();
        yield return GameSimulationClock.Wait(delay);

        var load = HotUpdateScenes.LoadGame();
        yield return load;
        if (load.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("[HotUpdate] Cannot enter " + menu.SceneName + ": " + load.OperationException);
            menu.FadeIn();
        }
    }
}
