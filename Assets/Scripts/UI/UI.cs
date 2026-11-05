using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

    public BasePanel RegisterPanel(UIPanelId id, GameObject scenePanel) => panels.RegisterPanel(id, scenePanel);
    public BasePanel GetPanel(UIPanelId id) => panels.GetPanel(id);

    private void Awake()
    {
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

    private void OnDestroy() => panels.Clear();

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
    }

    public void SwitchTo(GameObject _menu)
    {
        if (_menu == null)
        {
            panels.CloseMenuGroup();
        }
        else
        {
            if (!panels.TryGetPanelId(_menu, out UIPanelId id))
            {
                Debug.LogWarning("[UI] Cannot switch to an unregistered menu.", _menu);
                return;
            }
            AudioManager.instance.PlaySFX(4, null);
            panels.ShowExclusive(id);
        }

        if (GameManager.instance != null)
        {
            if (_menu == inGameUI)
                GameManager.instance.PauseGame(false);
            else
                GameManager.instance.PauseGame(true);

        }
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
        _data.volumeSettings.Clear();

        foreach (UI_VolumeSlider item in volumeSettings)
        {
            _data.volumeSettings.Add(item.parameter, item.slider.value);
        }
    }
}
