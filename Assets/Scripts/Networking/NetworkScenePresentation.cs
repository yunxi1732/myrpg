using Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

namespace Rpg.Networking
{
    /// <summary>Only binds presentation; does not create players or enable their simulation.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkScenePresentation : MonoBehaviour
    {
        private RoomPresentationContext context;
        private UI_InGame hud;
        private NetworkStatsPanel statsPanel;
        private NetworkCraftPanel craftPanel;
        private NetworkSkillTreePanel skillTreePanel;
        private UI ui;
        private GameObject optionsRoot;
        private MenuControlLock optionsLock;
        private CinemachineVirtualCamera cameraRig;
        private CinemachineBrain brain;
        private RoomEntityPresentation bound;
        private CinemachineBrain.UpdateMethod originalUpdate;
        private bool originalIgnoreTimeScale;

        public void Prepare(Scene scene)
        {
            Unbind();
            hud = null; ui = null; optionsRoot = null; optionsLock = null; cameraRig = null; brain = null;
            var flaskVisuals = new System.Collections.Generic.HashSet<ItemData_Equipment>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var inventory = root.GetComponentInChildren<Inventory>(true);
                if (inventory != null && inventory.itemDataBase != null)
                    foreach (var data in inventory.itemDataBase) if (data is ItemData_Equipment equipment) flaskVisuals.Add(equipment);
                foreach (var list in root.GetComponentsInChildren<UI_CraftList>(true))
                    foreach (var equipment in list.CraftEquipment) if (equipment != null) flaskVisuals.Add(equipment);
                if (hud == null) hud = root.GetComponentInChildren<UI_InGame>(true);
                if (ui == null) ui = root.GetComponentInChildren<UI>(true);
                if (cameraRig == null) cameraRig = root.GetComponentInChildren<CinemachineVirtualCamera>(true);
                if (brain == null) brain = root.GetComponentInChildren<CinemachineBrain>(true);
                // Allow the existing dialogue's explicit test/open entry without enabling NPC gameplay or the single-player UI controller.
                foreach (DialogueView dialogue in root.GetComponentsInChildren<DialogueView>(true)) dialogue.enabled = true;
            }
            if (brain != null)
            {
                originalUpdate = brain.m_UpdateMethod;
                originalIgnoreTimeScale = brain.m_IgnoreTimeScale;
            }
            if (hud != null) { hud.ConfigureRoomFlaskIcons(flaskVisuals); hud.BindNetwork(null); }
            optionsRoot = ui.OptionsPanel;
            optionsRoot.SetActive(false);
            optionsLock = optionsRoot.GetComponent<MenuControlLock>();
            if (optionsLock == null) optionsLock = optionsRoot.AddComponent<MenuControlLock>();
            optionsLock.Configure();
            if (statsPanel == null) statsPanel = gameObject.AddComponent<NetworkStatsPanel>();
            statsPanel.Prepare(scene);
            if (craftPanel == null) craftPanel = gameObject.AddComponent<NetworkCraftPanel>();
            craftPanel.Prepare(scene);
            if (skillTreePanel == null) skillTreePanel = gameObject.AddComponent<NetworkSkillTreePanel>();
            skillTreePanel.Prepare(scene);
            if (cameraRig != null) { cameraRig.Follow = null; cameraRig.LookAt = null; }
        }

        public void Bind(RoomPresentationContext value)
        {
            if (ReferenceEquals(context, value)) return;
            Unbind();
            context = value;
        }

        private void Update()
        {
            if (bound == null || !Application.isFocused || !Input.GetKeyDown(KeyCode.O)) return;
            var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focused != null && (focused.GetComponentInParent<TMP_InputField>() != null ||
                focused.GetComponentInParent<UnityEngine.UI.InputField>() != null)) return;
            ui.SwitchWithKeyTo(optionsRoot);
        }

        public void OpenOptions()
        {
            if (bound == null || !optionsLock.PrepareOpen()) return;
            optionsRoot.SetActive(true);
        }

        public void CloseOptions()
        {
            optionsRoot.SetActive(false);
            optionsLock.Release();
        }

        private void LateUpdate()
        {
            RoomEntityPresentation local = context?.LocalEntity;
            if (local != null && !local.View.gameObject.activeInHierarchy) local = null;
            if (local != null)
            {
                var identity = local.View.GetComponent<NetworkEntityIdentity>();
                if (identity == null || !identity.IsLocalInputOwner || identity.EntityId != local.EntityId ||
                    context.IsGuest && identity.Role != NetworkEntityRole.Display) local = null;
            }
            if (!ReferenceEquals(bound, local))
            {
                bound = local;
                if (hud != null) hud.BindNetwork(local);
                if (statsPanel != null) statsPanel.BindNetwork(local);
                if (craftPanel != null) craftPanel.BindNetwork(local);
                if (skillTreePanel != null) skillTreePanel.BindNetwork(local);
                if (local == null) CloseOptions();
                if (cameraRig != null)
                {
                    cameraRig.Follow = local?.View;
                    cameraRig.PreviousStateIsValid = false;
                    if (local != null)
                    {
                        foreach (CinemachineComponentBase component in cameraRig.GetComponentsInChildren<CinemachineComponentBase>(true)) component.enabled = true;
                        foreach (CinemachineExtension extension in cameraRig.GetComponentsInChildren<CinemachineExtension>(true)) extension.enabled = true;
                        cameraRig.enabled = true;
                    }
                }
                if (brain != null)
                {
                    brain.m_UpdateMethod = context != null && context.IsGuest ? CinemachineBrain.UpdateMethod.ManualUpdate : originalUpdate;
                    brain.m_IgnoreTimeScale = true;
                    brain.enabled = local != null;
                }
            }
            if (bound != null && bound.HasHealth && bound.HasHud && hud != null)
            {
                bool menuOpen = statsPanel != null && statsPanel.IsOpen || craftPanel != null && craftPanel.IsOpen ||
                    skillTreePanel != null && skillTreePanel.IsOpen || optionsRoot.activeInHierarchy;
                if (menuOpen)
                {
                    if (hud.gameObject.activeSelf) hud.gameObject.SetActive(false);
                }
                else if (!hud.gameObject.activeInHierarchy) ActivateHudPath();
            }
            // RemotePlayerView.LateUpdate (9000) finishes before this callback (10000).
            // Manually update B's camera now, preserving the scene's lens/body/damping.
            if (bound != null && context.IsGuest && brain != null) brain.ManualUpdate();
        }

        private void ActivateHudPath()
        {
            // The original Canvas is held inactive at scene entry. Open only its InGame_UI branch.
            Transform path = hud.transform;
            while (path.parent != null)
            {
                foreach (Transform sibling in path.parent) if (sibling != path) sibling.gameObject.SetActive(false);
                path = path.parent;
            }
            for (Transform current = hud.transform; current != null; current = current.parent) current.gameObject.SetActive(true);
            hud.BindNetwork(bound);
        }

        public void Unbind()
        {
            if (optionsRoot != null) CloseOptions();
            if (statsPanel != null) statsPanel.Unbind();
            if (craftPanel != null) craftPanel.Unbind();
            if (skillTreePanel != null) skillTreePanel.Unbind();
            if (hud != null) hud.UnbindNetwork();
            if (cameraRig != null) { cameraRig.Follow = null; cameraRig.LookAt = null; }
            if (brain != null)
            {
                brain.enabled = false;
                brain.m_UpdateMethod = originalUpdate;
                brain.m_IgnoreTimeScale = originalIgnoreTimeScale;
            }
            context = null; bound = null;
        }

        public void UnbindPlayer(int playerId)
        {
            if (bound != null && bound.PlayerId == playerId) Unbind();
        }

        private void OnDestroy() => Unbind();
    }
}
