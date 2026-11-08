using System;
using System.Collections;
using Rpg.Networking.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Owns network scene entry, room identities and the initial two-player setup; legacy scene gameplay stays suspended.</summary>
    public sealed class RoomSceneCoordinator : MonoBehaviour
    {
        public static RoomSceneCoordinator Active { get; private set; }
        public static bool ProtectSaves => Active != null;
        private static string entryMessage;
        public static string TakeEntryMessage() { string result = entryMessage; entryMessage = null; return result; }
        [SerializeField, Min(0)] private float validationReadyDelaySeconds;
        [SerializeField, Range(30, 50)] private int movementMinimumIntervalMilliseconds = 40;
        [SerializeField, Min(0.01f)] private float positionTeleportDistance = 5f;
        [SerializeField] private Vector2 hostSpawnOffset = Vector2.zero;
        [SerializeField] private Vector2 guestSpawnOffset = new Vector2(2.5f, 0);
        private TcpRoomHost host;
        private TcpRoomClient client;
        private UI_MainMenu menu;
        private long guestConnection, operation;
        private bool returning;
        private bool quitting;
        private int preparedSceneHandle = -1;
        private AsyncOperation inFlightLoad;
        public bool IsSceneSession => ReferenceEquals(Active, this);
        public RoomPresentationContext Presentation { get; private set; }
        private NetworkScenePresentation presentationBinding;
        public RoomPlayerEntities Players { get; private set; }
        private long entityOperation, initializationLoadId, initializedLoadId;
        private bool applyingGuestInitialization;
        private const float PlayerStateSendInterval = 0.1f;
        private float playerStateSendTimer;
        private PlayerPositionsDto pendingPositions;
        public RoomNetworkObjects NetworkObjects { get; private set; }
        private string activeRoomSessionId;
        private bool activeRoomIsHost;
        private long localConnectionSessionId;
        private int activeGuestPlayerId;

        public void Bind(UI_MainMenu owner, TcpRoomHost roomHost, TcpRoomClient roomClient)
        {
            menu = owner; host = roomHost; client = roomClient;
            host.StateChanged += OnHostState;
            client.SceneLoadRequested += OnSceneLoad;
            host.SceneInitializationAllowed += OnBothReady;
            host.MovementInputReceived += OnGuestMovementInput;
            client.SceneInitializationAllowed += OnBothReady;
            client.PlayerInitializationReceived += OnPlayerInitialization;
            client.PlayerPositionsReceived += OnPlayerPositions;
            host.RoomExited += OnExit;
            client.RoomExited += OnExit;
            host.Disconnected += OnDisconnect;
            client.Disconnected += OnDisconnect;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnHostState()
        {
            if (returning || host.GuestIdentity == null || host.Guest == null || guestConnection == host.Guest.SessionId) return;
            guestConnection = host.Guest.SessionId;
            try
            {
                ActivateSession();
                host.BeginSceneLoad();
                StartCoroutine(LoadAndPrepare(true, host.SceneLoadId, ++operation));
            }
            catch (Exception error) { Fail(true, error.Message); }
        }

        private void OnSceneLoad(SceneLoadDto load)
        {
            if (returning) return;
            ActivateSession();
            StartCoroutine(LoadAndPrepare(false, load.loadId, ++operation));
        }

        private void ActivateSession()
        {
            activeRoomIsHost = host.IsListening;
            activeRoomSessionId = activeRoomIsHost ? host.RoomSessionId : client.RoomSessionId;
            activeGuestPlayerId = activeRoomIsHost ? host.GuestIdentity.PlayerId : client.LocalIdentity.PlayerId;
            localConnectionSessionId = activeRoomIsHost ? 0 : client.Connection.SessionId;
            Active = this;
            menu.PreserveRoomInput();
            DontDestroyOnLoad(transform.root.gameObject);
            menu.ShowSceneProgress("Loading MainScene...", true);
        }

        private IEnumerator LoadAndPrepare(bool authority, long loadId, long token)
        {
            AsyncOperation load = null;
            string failure = null;
            if (!Application.CanStreamedLevelBeLoaded(RoomScene.Name)) failure = "MainScene is missing from Build Settings";
            else if (SceneManager.GetActiveScene().name != RoomScene.Name)
            {
                try
                {
                    load = inFlightLoad != null && !inFlightLoad.isDone ? inFlightLoad :
                        SceneManager.LoadSceneAsync(RoomScene.Name, LoadSceneMode.Single);
                    inFlightLoad = load;
                }
                catch (Exception error) { failure = error.Message; }
                if (load == null && failure == null) failure = "Scene loading did not start";
            }
            if (failure != null) { Fail(authority, failure); yield break; }
            if (load != null) yield return load;
            if (ReferenceEquals(load, inFlightLoad)) inFlightLoad = null;
            yield return null; // Scene-loaded gate and the enabled static scene components must finish first.
            double delayEnd = MonotonicClock.Now + validationReadyDelaySeconds;
            while (token == operation && !returning && MonotonicClock.Now < delayEnd) yield return null;
            if (token != operation || returning) yield break;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != RoomScene.Name || !scene.isLoaded || preparedSceneHandle != scene.handle)
            {
                Fail(authority, "MainScene context was not prepared");
                yield break;
            }
            try
            {
                if (authority) host.MarkSceneReady(scene.name);
                else client.MarkSceneReady(scene.name);
                if (authority && host.GuestIdentity == null) menu.ShowSceneProgress("Waiting for B", false);
                else if (!(authority ? host.CanInitializeEntities : client.CanInitializeEntities))
                    menu.ShowSceneProgress("Waiting for scene ready...", true);
                Debug.Log($"[RoomScene] local ready room={(authority ? host.RoomSessionId : client.RoomSessionId)} load={loadId} scene={scene.name}");
            }
            catch (Exception error) { Fail(authority, error.Message); }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsSceneSession) return;
            if (scene.name == "MainMenu" && returning)
            {
                Active = null;
                Destroy(transform.root.gameObject);
                return;
            }
            if (returning) return;
            if (scene.name != RoomScene.Name) return;
            ClearPresentation();
            // Preserve map/camera visuals only. Do not start preplaced single-player actors, managers or HUD.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                bool staticView = root.GetComponentInChildren<Camera>(true) != null || root.GetComponent<Grid>() != null || root.name == "Background";
                if (!staticView) { root.SetActive(false); continue; }
                foreach (Entity entity in root.GetComponentsInChildren<Entity>(true)) entity.gameObject.SetActive(false);
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null) behaviour.enabled = false;
            }
            preparedSceneHandle = scene.handle;
            try
            {
                PrepareRoomObjects(scene);
                if (presentationBinding == null) presentationBinding = gameObject.AddComponent<NetworkScenePresentation>();
                presentationBinding.Prepare(scene);
                menu.ShowNetworkSceneView();
            }
            catch (Exception error) { Fail(host.IsListening, error.Message); return; }
            Debug.Log($"[RoomScene] ACTIVE SCENE={SceneManager.GetActiveScene().name}, loaded={scene.isLoaded}, static scene view prepared");
        }

        private void OnBothReady()
        {
            if (!IsSceneSession || returning) return;
            if (!(host.IsListening ? host.CanInitializeEntities : client.CanInitializeEntities)) return;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != RoomScene.Name || preparedSceneHandle != scene.handle) { Fail(host.IsListening, "Scene changed before ready"); return; }
            long loadId = host.IsListening ? host.SceneLoadId : client.SceneLoadId;
            if (initializationLoadId == loadId) return;
            try { BeginPlayerInitialization(loadId); }
            catch (Exception error) { Fail(host.IsListening, error.Message); }
        }

        private void BeginPlayerInitialization(long loadId)
        {
            RoomPlayerIdentity local = host.IsListening ? host.HostIdentity : client.LocalIdentity;
            RoomPlayerIdentity authority = host.IsListening ? host.HostIdentity : client.HostIdentity;
            RoomPlayerIdentity guest = host.IsListening ? host.GuestIdentity : client.LocalIdentity;
            if (Presentation != null && Presentation.RoomSessionId == local.RoomSessionId && Presentation.LocalPlayerId == local.PlayerId)
                Presentation.UpdatePlayers(authority, guest);
            else
            {
                if (Presentation != null)
                {
                    ClearPresentation();
                    PrepareRoomObjects(SceneManager.GetActiveScene());
                }
                Presentation = new RoomPresentationContext(local.RoomSessionId, local.PlayerId, !host.IsListening, authority, guest);
                presentationBinding.Bind(Presentation);
            }
            initializationLoadId = loadId;
            playerStateSendTimer = 0;
            pendingPositions = null;
            applyingGuestInitialization = false;
            long token = ++entityOperation;
            menu.ShowSceneProgress("Initializing players...", true);
            if (host.IsListening)
            {
                Players.EnsurePlayers(Presentation, authority, guest);
                StartCoroutine(FinishPlayerInitialization(null, token, loadId));
            }
            else StartCoroutine(WaitForInitialPlayers(token));
        }

        private IEnumerator WaitForInitialPlayers(long token)
        {
            double deadline = MonotonicClock.Now + 10;
            while (!returning && token == entityOperation)
            {
                if (MonotonicClock.Now >= deadline) { Fail(false, "Initial player state from A timed out"); yield break; }
                yield return null;
            }
        }

        private void OnPlayerInitialization(PlayerInitializationDto value)
        {
            if (!IsSceneSession || returning || host.IsListening || Presentation == null || Players == null ||
                value.roomSessionId != Presentation.RoomSessionId || value.loadId != initializationLoadId ||
                initializedLoadId == value.loadId || applyingGuestInitialization) return;
            try
            {
                applyingGuestInitialization = true;
                Players.EnsurePlayers(Presentation, client.HostIdentity, client.LocalIdentity, value);
                StartCoroutine(FinishPlayerInitialization(value, ++entityOperation, value.loadId));
            }
            catch (Exception error) { Fail(false, error.Message); }
        }

        private IEnumerator FinishPlayerInitialization(PlayerInitializationDto initialState, long token, long loadId)
        {
            double deadline = MonotonicClock.Now + 10;
            // Awake runs after identity binding; Start must finish on both bodies before exposing either local view.
            yield return null;
            while (!returning && token == entityOperation &&
                (!Players.AllStarted || initialState == null && GameSimulationClock.AuthorityTick == 0))
            {
                if (MonotonicClock.Now >= deadline) { Fail(host.IsListening, "Player components did not initialize"); yield break; }
                yield return null;
            }
            if (returning || token != entityOperation) yield break;
            try
            {
                if (initialState == null) initialState = Players.CaptureInitialState(loadId, GameSimulationClock.AuthorityTick);
                Players.ApplyInitialState(initialState);
                if (host.IsListening) host.SendPlayerInitialization(initialState);
                Players.EnableAuthoritativeMovement();
                initializedLoadId = loadId;
                if (!activeRoomIsHost && pendingPositions != null)
                {
                    Players.ApplyPositions(pendingPositions);
                    pendingPositions = null;
                }
                menu.ShowSceneProgress("Players ready", false);
                menu.RevealPreparedScene();
                var local = Presentation.LocalEntity;
                Debug.Log($"[RoomPlayers] READY count={Players.Entities.Count} localPlayerId={Presentation.LocalPlayerId} localEntityId={local.EntityId} role={local.View.GetComponent<NetworkEntityIdentity>().Role}; A simulates both players and sends positions every 100ms; B interpolates display positions by authority tick.");
            }
            catch (Exception error) { Fail(host.IsListening, error.Message); }
        }

        private void LateUpdate()
        {
            if (activeRoomIsHost)
            {
                SendAuthoritativePositions();
                return;
            }
            // All Player.Update collectors have run. Poll once with the newest state, including zero.
            if (!IsSceneSession || returning || activeRoomIsHost || client == null || initializedLoadId == 0 ||
                initializedLoadId != initializationLoadId || Presentation?.LocalEntity == null) return;
            var local = Presentation.LocalEntity;
            var identity = local.View.GetComponent<NetworkEntityIdentity>();
            var player = local.View.GetComponent<Player>();
            if (identity == null || !identity.CanCollectMovementInput || player == null) return;
            client.MovementMinimumIntervalMilliseconds = movementMinimumIntervalMilliseconds;
            float axis = !player.isActiveAndEnabled || GameplayControlLock.IsLocked || !Application.isFocused ? 0 : player.InputData.CurrentAxis;
            client.SendMovementInput(axis);
        }

        private void SendAuthoritativePositions()
        {
            if (!IsSceneSession || returning || host == null || !host.CanInitializeEntities || host.GuestIdentity == null ||
                initializedLoadId == 0 || initializedLoadId != initializationLoadId || Players == null || !Players.AllStarted ||
                GameSimulationClock.AuthorityTick == 0)
            {
                playerStateSendTimer = 0;
                return;
            }
            // LocalPlayerStateSender timer structure, using shared non-scaled time. Never burst to catch up.
            playerStateSendTimer += GameSimulationClock.DeltaTime;
            if (playerStateSendTimer < PlayerStateSendInterval) return;
            playerStateSendTimer = 0;
            host.SendPlayerPositions(Players.CapturePositions(initializedLoadId));
        }

        private void OnPlayerPositions(PlayerPositionsDto value)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || value.roomSessionId != activeRoomSessionId ||
                value.loadId != initializationLoadId) return;
            if (initializedLoadId == initializationLoadId && initializedLoadId > 0)
                Players?.ApplyPositions(value);
            else if (pendingPositions == null || value.stateSeq > pendingPositions.stateSeq && value.authorityTick >= pendingPositions.authorityTick)
                pendingPositions = value; // Baseline arrived, but B's Start/coroutine may still be finishing.
        }

        private void OnGuestMovementInput(RoomPlayerIdentity actor, MovementInputDto input)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || initializedLoadId == 0 ||
                initializedLoadId != initializationLoadId || actor.RoomSessionId != activeRoomSessionId) return;
            Players?.ApplyGuestMovementInput(actor, input);
        }

        private void OnExit(RoomExitEvent value)
        {
            if (!IsSceneSession || returning || value.RoomSessionId != activeRoomSessionId) return;
            if (activeRoomIsHost && value.Reason == RoomExitReason.GuestLeft &&
                (value.IsLocal || value.ConnectionSessionId != guestConnection)) return;
            if (!activeRoomIsHost && value.ConnectionSessionId != localConnectionSessionId) return;
            if (value.Reason == RoomExitReason.HostClosed || !activeRoomIsHost)
                ReturnToEntry(value.Reason == RoomExitReason.HostClosed ? "Room closed" : "Left room");
            else { RemoveGuestPlayers(); menu.ShowSceneProgress("Waiting for B", false); }
        }

        private void OnDisconnect(NetworkDisconnectedEvent value)
        {
            if (!IsSceneSession || returning || value.RoomSessionId != activeRoomSessionId ||
                value.ConnectionSessionId != (activeRoomIsHost ? guestConnection : localConnectionSessionId)) return;
            if (!activeRoomIsHost) ReturnToEntry("Disconnected: " + value.Reason);
            else { RemoveGuestPlayers(); menu.ShowSceneProgress("Waiting for B", false); }
        }

        private void Fail(bool authority, string reason)
        {
            Debug.LogError("[RoomScene] " + reason);
            entryMessage = "Scene load failed: " + reason;
            if (authority) host.CloseRoom();
            else
            {
                try { client.ReportSceneFailure(); }
                catch (Exception error) { Debug.LogWarning("[RoomScene] " + error.Message); client.Close(); }
            }
            ReturnToEntry(entryMessage);
        }

        private void ReturnToEntry(string reason)
        {
            if (returning || quitting) return;
            returning = true;
            ClearPresentation();
            if (string.IsNullOrEmpty(entryMessage)) entryMessage = reason;
            ++operation; // In-flight loading may complete, but its callback must never mark a new session ready.
            menu.ShowSceneProgress("Returning to room entry...", true);
            SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
        }

        private void OnApplicationQuit() => quitting = true;

        private void InvalidateEntityInitialization()
        {
            playerStateSendTimer = 0;
            pendingPositions = null;
            ++entityOperation;
            initializationLoadId = initializedLoadId = 0;
            applyingGuestInitialization = false;
        }

        private void PrepareRoomObjects(Scene scene)
        {
            NetworkObjects = new RoomNetworkObjects(activeRoomSessionId, scene, () =>
            {
                InvalidateEntityInitialization();
                if (presentationBinding != null) presentationBinding.Unbind();
            }, playerId =>
            {
                if (presentationBinding != null) presentationBinding.UnbindPlayer(playerId);
            });
            Players = new RoomPlayerEntities(scene, RoomPlayerEntities.FindSceneTemplate(scene), hostSpawnOffset, guestSpawnOffset, NetworkObjects, positionTeleportDistance);
        }

        // Exit, disconnect, failed initialization, scene unload and owner destruction share this route.
        private void CleanupNetworkObjects(int departingPlayerId = 0)
        {
            InvalidateEntityInitialization();
            if (departingPlayerId > 0)
            {
                NetworkObjects?.CleanupPlayer(activeRoomSessionId, departingPlayerId);
                Presentation?.RemovePlayer(departingPlayerId);
                return;
            }
            if (NetworkObjects != null) NetworkObjects.Dispose();
            else if (presentationBinding != null) presentationBinding.Unbind();
            NetworkObjects = null;
            Players?.Dispose();
            Players = null;
            Presentation?.Dispose();
            Presentation = null;
        }

        private void ClearPresentation() => CleanupNetworkObjects();

        private void RemoveGuestPlayers()
        {
            if (activeGuestPlayerId > 0) CleanupNetworkObjects(activeGuestPlayerId);
            activeGuestPlayerId = 0;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (!IsSceneSession || scene.handle != preparedSceneHandle) return;
            preparedSceneHandle = -1;
            ClearPresentation();
            if (returning || quitting) return;
            if (activeRoomIsHost) host.CloseRoom();
            else client.LeaveRoom();
            ReturnToEntry("Room scene unloaded");
        }

        [ContextMenu("Repeat Player Initialization (Play Mode)")]
        private void RepeatPlayerInitialization()
        {
            if (!Application.isPlaying || !IsSceneSession || returning) return;
            var before = Presentation?.LocalEntity;
            OnBothReady();
            Debug.Log($"[RoomPlayers] repeat count={Players?.Entities.Count ?? 0} sameLocalBinding={ReferenceEquals(before, Presentation?.LocalEntity)}");
        }

        [ContextMenu("Verify Scene Preparation Failure (Play Mode)")]
        private void VerifyPreparationFailure()
        {
            if (Application.isPlaying && IsSceneSession && !returning) Fail(host.IsListening, "Scene preparation failure (validation)");
        }
        private void OnDestroy()
        {
            ClearPresentation();
            ++operation;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (host != null)
            {
                host.StateChanged -= OnHostState; host.SceneInitializationAllowed -= OnBothReady;
                host.MovementInputReceived -= OnGuestMovementInput;
                host.RoomExited -= OnExit; host.Disconnected -= OnDisconnect;
            }
            if (client != null)
            {
                client.SceneLoadRequested -= OnSceneLoad; client.SceneInitializationAllowed -= OnBothReady;
                client.PlayerInitializationReceived -= OnPlayerInitialization;
                client.PlayerPositionsReceived -= OnPlayerPositions;
                client.RoomExited -= OnExit; client.Disconnected -= OnDisconnect;
            }
            if (ReferenceEquals(Active, this)) Active = null;
        }
    }
}
