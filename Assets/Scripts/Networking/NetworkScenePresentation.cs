using Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Only binds presentation; does not create players or enable their simulation.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkScenePresentation : MonoBehaviour
    {
        private RoomPresentationContext context;
        private UI_InGame hud;
        private CinemachineVirtualCamera cameraRig;
        private CinemachineBrain brain;
        private RoomEntityPresentation bound;
        private CinemachineBrain.UpdateMethod originalUpdate;
        private bool originalIgnoreTimeScale;

        public void Prepare(Scene scene)
        {
            Unbind();
            hud = null; cameraRig = null; brain = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (hud == null) hud = root.GetComponentInChildren<UI_InGame>(true);
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
            if (hud != null) hud.BindNetwork(null);
            if (cameraRig != null) { cameraRig.Follow = null; cameraRig.LookAt = null; }
        }

        public void Bind(RoomPresentationContext value)
        {
            if (ReferenceEquals(context, value)) return;
            Unbind();
            context = value;
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
            if (bound != null && bound.HasHealth && bound.HasHud && hud != null && !hud.gameObject.activeInHierarchy)
                ActivateHudPath();
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
