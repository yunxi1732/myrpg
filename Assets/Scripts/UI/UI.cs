using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UI : MonoBehaviour, ISaveManager
{
    [Header("End screen")]
    [SerializeField] private GameObject endText;
    public UI_FadeScreen fadeScreen;
    [SerializeField] private GameObject restartButton;
    [Space]

    [SerializeField] private GameObject characterUI;
    [SerializeField] private GameObject skillTreeUI;
    [SerializeField] private GameObject craftUI;
    [SerializeField] private GameObject optionsUI;
    [SerializeField] private GameObject inGameUI;

    public UI_SkillTooltip skillTooltip;
    public UI_ItemTooltip itemTooltip;
    public UI_StatTooltip statTooltip;
    public UI_CraftWindow craftWindow;

    [SerializeField] private UI_VolumeSlider[] volumeSettings;

    private readonly UISystem panels = new UISystem();
    [SerializeField] private InteractionListView interactionList;
    private readonly InteractionManager interactions = new InteractionManager();
    public InteractionManager Interactions
    {
        get { EnsureSceneLifetime(); return interactions; }
    }
    [SerializeField] private KeyCode previousInteractionKey = KeyCode.PageUp;
    [SerializeField] private KeyCode nextInteractionKey = KeyCode.PageDown;
    private GameManager blankMenuPauseManager;
    public bool IsShuttingDown { get; private set; }
    private bool sceneSubscribed;
    private int boundSceneHandle;

    public BasePanel RegisterPanel(UIPanelId id, GameObject scenePanel)
    {
        if (IsShuttingDown) throw new System.ObjectDisposedException(nameof(UI));
        EnsureSceneLifetime();
        return panels.RegisterPanel(id, scenePanel);
    }
    public BasePanel GetPanel(UIPanelId id) => panels.GetPanel(id);

    private void Awake()
    {
        // Network scene-entry prepares static context first; HUD/entity binding belongs to later steps.
        if (Rpg.Networking.RoomSceneCoordinator.ProtectSaves && gameObject.scene.name == Rpg.Networking.Protocol.RoomScene.Name)
        {
            enabled = false;
            return;
        }
        EnsureSceneLifetime();
        if (interactionList != null) interactionList.Bind(Interactions);
        RegisterScenePanel(UIPanelId.Character, characterUI);
        RegisterScenePanel(UIPanelId.SkillTree, skillTreeUI);
        RegisterScenePanel(UIPanelId.Craft, craftUI);
        RegisterScenePanel(UIPanelId.Options, optionsUI);
        RegisterScenePanel(UIPanelId.InGame, inGameUI);

        // End-screen objects have their own lifecycle, outside the menu group.
        if (endText != null) endText.SetActive(false);
        if (restartButton != null) restartButton.SetActive(false);
        SwitchTo(skillTreeUI);

        fadeScreen.gameObject.SetActive(true);
    }

    private void RegisterScenePanel(UIPanelId id, GameObject scenePanel)
    {
        if (scenePanel != null)
            RegisterPanel(id, scenePanel);
        else
            Debug.LogWarning("[UI] Scene panel is not assigned: " + id, this);
    }

    private void OnDestroy()
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        if (sceneSubscribed)
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            sceneSubscribed = false;
        }
        try { if (interactionList != null) interactionList.Bind(null); }
        finally
        {
            try { interactions.Dispose(); }
            finally
            {
                try { panels.Dispose(); }
                finally { SetBlankMenuLock(false); }
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        SwitchTo(inGameUI);

        itemTooltip.gameObject.SetActive(false);
        statTooltip.gameObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
            SwitchWithKeyTo(characterUI);

        if (Input.GetKeyDown(KeyCode.B))
            SwitchWithKeyTo(craftUI);

        if (Input.GetKeyDown(KeyCode.K))
            SwitchWithKeyTo(skillTreeUI);

        if (Input.GetKeyDown(KeyCode.O))
            SwitchWithKeyTo(optionsUI);

        HandleInteractionInput();
    }

    private void HandleInteractionInput()
    {
        if (Time.timeScale <= 0 || GameplayControlLock.IsActionInputBlocked || Interactions.Options.Count == 0) return;
        GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused != null && (focused.GetComponentInParent<UnityEngine.UI.InputField>() != null ||
            focused.GetComponentInParent<TMP_InputField>() != null)) return;

        float scroll = Input.mouseScrollDelta.y;
        if (Input.GetKeyDown(previousInteractionKey) || scroll > 0.01f) Interactions.MoveSelection(-1);
        else if (Input.GetKeyDown(nextInteractionKey) || scroll < -0.01f) Interactions.MoveSelection(1);
        var selected = Interactions.GetSelected();
        if (selected != null && selected.key != KeyCode.None && Input.GetKeyDown(selected.key))
            Interactions.TryExecuteSelected();
    }

    public void SwitchTo(GameObject _menu)
    {
        if (IsShuttingDown) return;
        EnsureSceneLifetime();
        if (_menu == null)
        {
            // Preserve the legacy all-menus-hidden pause without releasing other owners.
            SetBlankMenuLock(true);
            panels.CloseMenuGroup();
        }
        else
        {
            if (!panels.TryGetPanelId(_menu, out UIPanelId id))
            {
                Debug.LogWarning("[UI] Cannot switch to an unregistered menu.", _menu);
                return;
            }
            if (!panels.ShowExclusive(id)) return;
            SetBlankMenuLock(false);
            AudioManager.instance.PlaySFX(4, null);
        }
    }

    private void SetBlankMenuLock(bool locked)
    {
        if (locked)
        {
            if (GameplayControlLock.Acquire(this) && Application.isPlaying && PlayerManager.instance != null)
                PlayerManager.instance.player?.CancelSwordAimForControlLock();
            if (Application.isPlaying && blankMenuPauseManager == null && GameManager.instance != null)
            {
                blankMenuPauseManager = GameManager.instance;
                blankMenuPauseManager.SetPauseOwner(this, true);
            }
        }
        else
        {
            if (GameplayControlLock.Release(this)) GameplayControlLock.SuppressActionInputForCurrentFrame();
            if (blankMenuPauseManager != null) blankMenuPauseManager.SetPauseOwner(this, false);
            blankMenuPauseManager = null;
        }
    }

    private void OnDisable() => SetBlankMenuLock(false);

    private void EnsureSceneLifetime()
    {
        if (sceneSubscribed || IsShuttingDown) return;
        boundSceneHandle = gameObject.scene.handle;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        sceneSubscribed = true;
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.handle == boundSceneHandle) OnDestroy();
    }

    public void SwitchWithKeyTo(GameObject _menu)
    {
        if (_menu != null && _menu.activeSelf)
        {
            SwitchTo(inGameUI);
            return;
        }

        SwitchTo(_menu);
    }

    public void SwitchOnEndScreen()
    { 
        fadeScreen.FadeOut();
        StartCoroutine(EndScreenCorutione());
    }

    IEnumerator EndScreenCorutione()
    {
        yield return new WaitForSeconds(1);
        endText.SetActive(true);
        yield return new WaitForSeconds(1);
        restartButton.SetActive(true);
    }

    public void RestartGameButton()
    {
        AudioManager.instance.PlaySFX(4, null);
        GameManager.instance.RestartScene();
    }

    public void LoadData(GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        foreach (KeyValuePair<string, float> kvp in _data.volumeSettings)
        {
            foreach (UI_VolumeSlider item in volumeSettings)
            {
                if (item.parameter == kvp.Key)
                    item.LoadSlider(kvp.Value);
            }
        }
    }

    public void SaveData(ref GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        _data.volumeSettings.Clear();

        foreach (UI_VolumeSlider item in volumeSettings)
        {
            _data.volumeSettings.Add(item.parameter, item.slider.value);
        }
    }
}
