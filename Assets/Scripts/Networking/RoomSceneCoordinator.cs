using System;
using System.Collections;
using Rpg.Networking.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Owns network scene entry, room identities and the initial two-player setup; legacy scene gameplay stays suspended.</summary>
    [DefaultExecutionOrder(-10000)] // After the -32000 clock driver, before actor movement/hits: expire elements at their exact tick boundary.
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
        public RoomSceneMonsters Monsters { get; private set; }
        public RoomCombatDamage CombatDamage { get; private set; }
        private readonly System.Collections.Generic.Queue<DamageResultDto> pendingDamageResults = new System.Collections.Generic.Queue<DamageResultDto>();
        private ElementStatesDto pendingElementStates;
        private readonly System.Collections.Generic.Queue<RoomInventoryResultDto> pendingInventoryResults = new System.Collections.Generic.Queue<RoomInventoryResultDto>();
        private long localEquipmentRequestSeq;
        private long localCraftRequestSeq;
        private long localSkillUnlockRequestSeq;
        private readonly System.Collections.Generic.Queue<SkillUnlockResultDto> pendingSkillUnlockResults = new System.Collections.Generic.Queue<SkillUnlockResultDto>();
        private readonly System.Collections.Generic.Queue<FlaskUseResultDto> pendingFlaskResults = new System.Collections.Generic.Queue<FlaskUseResultDto>();
        private float flaskHudTimer;
        private SwordSkill swordTemplate;
        private RoomSwordNetwork swords;
        private SwordStatesDto pendingSwordStates;
        private float swordStateTimer;
        private RoomSceneMonsters.SceneEntry[] monsterCatalog;
        private long monsterInitializationLoadId;
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
            host.JumpRequested += OnGuestJumpRequested;
            host.DashRequested += OnGuestDashRequested;
            host.PrimaryAttackRequested += OnPrimaryAttackRequested;
            host.EquipmentChangeRequested += OnEquipmentChangeRequested;
            host.CraftRequested += OnCraftRequested;
            host.SkillUnlockRequested += OnSkillUnlockRequested;
            host.FlaskUseRequested += OnFlaskUseRequested;
            host.SwordInputReceived += OnSwordInput;
            host.InputLockReceived += OnGuestInputLock;
            GameplayControlLock.Changed += OnLocalInputLockChanged;
            client.SceneInitializationAllowed += OnBothReady;
            client.PlayerInitializationReceived += OnPlayerInitialization;
            client.PlayerPositionsReceived += OnPlayerPositions;
            client.MonsterInitializationReceived += OnMonsterInitialization;
            client.MonsterStatesReceived += OnMonsterStates;
            client.DamageResultReceived += OnDamageResult;
            client.ElementStatesReceived += OnElementStates;
            client.EquipmentChangeResultReceived += OnEquipmentChangeResult;
            client.CraftResultReceived += OnCraftResult;
            client.SkillUnlockResultReceived += OnSkillUnlockResult;
            client.FlaskUseResultReceived += OnFlaskUseResult;
            client.SwordStatesReceived += OnSwordStates;
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
            monsterCatalog = RoomSceneMonsters.Discover(scene); // Capture authored keys/activation before the scene preparation gate changes them.
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
                Monsters.BindAuthority(Presentation, host.AllocateSceneEntityId);
                Players.BindPrimaryAttackTargets(Monsters);
                if (CombatDamage == null) CombatDamage = new RoomCombatDamage(Presentation, Monsters, host.Results, value => host.SendDamageResult(value), value => host.SendElementStates(value));
                Players.BindDamageService(CombatDamage);
                Monsters.BindDamageService(CombatDamage);
                StartCoroutine(FinishPlayerInitialization(null, token, loadId));
            }
            else StartCoroutine(WaitForInitialPlayers(token));
        }

        private IEnumerator WaitForInitialPlayers(long token)
        {
            double deadline = MonotonicClock.Now + 10;
            while (!returning && token == entityOperation)
            {
                if (MonotonicClock.Now >= deadline) { Fail(false, "Initial player state from A timed out; " + client.SceneInitializationDiagnostics); yield break; }
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
                Debug.Log($"[RoomPlayers] B received baseline room={value.roomSessionId} load={value.loadId} seq={value.stateSeq} tick={value.authorityTick}");
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
                (!Players.AllStarted || initialState == null && GameSimulationClock.AuthorityTick == 0 ||
                    initialState != null && monsterInitializationLoadId != loadId))
            {
                if (MonotonicClock.Now >= deadline)
                {
                    Fail(host.IsListening, $"Player components or monster manifest did not initialize; playersStarted={Players.AllStarted} manifest={monsterInitializationLoadId}/{loadId}" +
                        (activeRoomIsHost ? "" : "; " + client.SceneInitializationDiagnostics));
                    yield break;
                }
                yield return null;
            }
            if (returning || token != entityOperation) yield break;
            try
            {
                if (activeRoomIsHost) EnsureSwordNetwork();
                if (initialState == null) initialState = Players.CaptureInitialState(loadId, GameSimulationClock.AuthorityTick);
                Players.ApplyInitialState(initialState);
                if (!activeRoomIsHost) EnsureSwordNetwork();
                if (host.IsListening) host.SendPlayerInitialization(initialState);
                if (host.IsListening)
                {
                    host.SendMonsterInitialization(Monsters.Capture(loadId, GameSimulationClock.AuthorityTick));
                    CombatDamage.PublishElementBaseline(GameSimulationClock.AuthorityTick);
                    monsterInitializationLoadId = loadId;
                    Monsters.EnableAuthorityMovement(Players);
                    Debug.Log($"[RoomMonsters] bound={Monsters.Count} role=Authoritative; AI/actions and bidirectional physical/direct-magic damage enabled; A resolves monster hit windows against registered player entities.");
                }
                Players.EnableAuthoritativeMovement();
                initializedLoadId = loadId;
                if (activeRoomIsHost) swords.Publish();
                else if (pendingSwordStates != null) { swords.Apply(pendingSwordStates); pendingSwordStates = null; }
                if (!activeRoomIsHost)
                    while (pendingInventoryResults.Count > 0) OnInventoryResult(pendingInventoryResults.Dequeue());
                if (!activeRoomIsHost)
                    while (pendingSkillUnlockResults.Count > 0) OnSkillUnlockResult(pendingSkillUnlockResults.Dequeue());
                if (!activeRoomIsHost)
                    while (pendingFlaskResults.Count > 0) OnFlaskUseResult(pendingFlaskResults.Dequeue());
                if (!activeRoomIsHost)
                    while (pendingDamageResults.Count > 0) CombatDamage.ApplyConfirmedResult(pendingDamageResults.Dequeue());
                if (!activeRoomIsHost && pendingElementStates != null) { CombatDamage.ApplyElementStates(pendingElementStates); pendingElementStates = null; }
                if (!activeRoomIsHost) client.SendLocalInputLock(GameplayControlLock.IsLocked);
                if (!activeRoomIsHost && pendingPositions != null)
                {
                    Players.ApplyPositions(pendingPositions);
                    pendingPositions = null;
                }
                menu.ShowSceneProgress("Players ready", false);
                menu.RevealPreparedScene();
                var local = Presentation.LocalEntity;
                LogRoomCombatConfiguration();
                Debug.Log($"[RoomPlayers] READY count={Players.Entities.Count} localPlayerId={Presentation.LocalPlayerId} localEntityId={local.EntityId} role={local.View.GetComponent<NetworkEntityIdentity>().Role}; A simulates both players and sends positions every 100ms; B interpolates display positions by authority tick.");
            }
            catch (Exception error) { Fail(host.IsListening, error.Message); }
        }

        private void OnMonsterInitialization(MonsterInitializationDto value)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || Presentation == null || Monsters == null ||
                value.roomSessionId != activeRoomSessionId || value.loadId != initializationLoadId) return;
            try
            {
                if (Monsters.Apply(Presentation, value))
                {
                    if (CombatDamage == null) CombatDamage = new RoomCombatDamage(Presentation, Monsters);
                    monsterInitializationLoadId = value.loadId;
                    Debug.Log($"[RoomMonsters] bound={Monsters.Count} role=Display load={value.loadId}; existing scene objects, no clones.");
                }
            }
            catch (Exception error) { Fail(false, error.Message); }
        }

        private void LateUpdate()
        {
            if (activeRoomIsHost)
            {
                // A's ready actor survives guest departure/rejoin; only guest requests depend on the two-peer load barrier.
                if (IsSceneSession && !returning && Presentation?.LocalEntity?.View != null)
                {
                    var hostPlayer = Presentation.LocalEntity.View.GetComponent<Player>();
                    if (hostPlayer != null && hostPlayer.TryConsumePrimaryAttackPress(out long requestSeq) &&
                        hostPlayer.isActiveAndEnabled && Application.isFocused && !GameplayControlLock.IsActionInputBlocked)
                        OnPrimaryAttackRequested(host.HostIdentity, new PrimaryAttackRequestDto { requestSeq = requestSeq });
                    if (hostPlayer != null && hostPlayer.TryConsumeFlaskPress(out long flaskSeq) && hostPlayer.isActiveAndEnabled &&
                        Application.isFocused && !GameplayControlLock.IsActionInputBlocked)
                        OnFlaskUseRequested(host.HostIdentity, new FlaskUseRequestDto { roomSessionId = activeRoomSessionId, requestSeq = flaskSeq });
                    flaskHudTimer += GameSimulationClock.DeltaTime;
                    if (flaskHudTimer >= PlayerStateSendInterval) { flaskHudTimer = 0; Players?.RefreshAuthorityFlaskHud(); }
                    swordStateTimer += GameSimulationClock.DeltaTime;
                    if (swordStateTimer >= PlayerStateSendInterval) { swordStateTimer = 0; swords?.Publish(); }
                }
                SendAuthoritativePositions();
                return;
            }
            // All Player.Update collectors have run. Poll once with the newest state, including zero.
            if (!IsSceneSession || returning || activeRoomIsHost || client == null || initializedLoadId == 0 ||
                initializedLoadId != initializationLoadId || Presentation?.LocalEntity == null) return;
            var local = Presentation.LocalEntity;
            var identity = local.View.GetComponent<NetworkEntityIdentity>();
            var player = local.View.GetComponent<Player>();
            if (!identity.CanCollectMovementInput) return;
            client.SendLocalInputLock(GameplayControlLock.IsLocked);
            client.MovementMinimumIntervalMilliseconds = movementMinimumIntervalMilliseconds;
            client.SendMovementInput(player.InputData.CurrentAxis, player.InputData.WallSlideDown);
            if (player.TryConsumeJumpPress(out long actionSeq))
                client.SendJumpRequest(actionSeq);
            if (player.TryConsumeDashPress(out long dashActionSeq, out float dashAxis))
                client.SendDashRequest(dashActionSeq, dashAxis);
            if (player.TryConsumePrimaryAttackPress(out long attackRequestSeq) && player.isActiveAndEnabled &&
                Application.isFocused && !GameplayControlLock.IsActionInputBlocked)
                client.SendPrimaryAttackRequest(attackRequestSeq);
            if (player.TryConsumeFlaskPress(out long flaskRequestSeq) && player.isActiveAndEnabled && Application.isFocused && !GameplayControlLock.IsActionInputBlocked)
                client.SendFlaskUseRequest(new FlaskUseRequestDto { roomSessionId = activeRoomSessionId, requestSeq = flaskRequestSeq });
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
            host.SendMonsterStates(Monsters.CaptureStates(initializedLoadId, GameSimulationClock.AuthorityTick));
        }

        private void FixedUpdate()
        {
            if (IsSceneSession && !returning && activeRoomIsHost) CombatDamage?.AdvanceElements(GameSimulationClock.AuthorityTick);
            if (IsSceneSession && !returning && activeRoomIsHost) swords?.Advance();
        }

        private void EnsureSwordNetwork()
        {
            if (swords == null) swords = new RoomSwordNetwork(Presentation, Players, NetworkObjects, CombatDamage, swordTemplate,
                (player, input) => { if (activeRoomIsHost) OnSwordInput(host.HostIdentity, input); else client.SendSwordInput(input); },
                activeRoomIsHost ? host.Results : null, activeRoomIsHost ? (Func<int>)host.AllocateSceneEntityId : null,
                activeRoomIsHost ? (Action<SwordStatesDto>)(value => host.SendSwordStates(value)) : null);
            swords.EnsurePlayers();
        }
        private void OnSwordInput(RoomPlayerIdentity actor, SwordInputDto input) => swords?.Request(actor, input);
        private void OnSwordStates(SwordStatesDto value)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || value.roomSessionId != activeRoomSessionId) return;
            if (swords == null || initializedLoadId == 0 || initializedLoadId != initializationLoadId)
            {
                if (pendingSwordStates == null || value.stateSeq > pendingSwordStates.stateSeq && value.authorityTick >= pendingSwordStates.authorityTick)
                    pendingSwordStates = value;
                return;
            }
            swords.Apply(value);
        }

        private void OnElementStates(ElementStatesDto value)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || value.roomSessionId != activeRoomSessionId || Presentation == null) return;
            if (CombatDamage == null || initializedLoadId == 0 || initializedLoadId != initializationLoadId)
            {
                if (pendingElementStates == null || value.stateSeq > pendingElementStates.stateSeq && value.authorityTick >= pendingElementStates.authorityTick) pendingElementStates = value;
                return;
            }
            CombatDamage.ApplyElementStates(value);
        }

        private void OnDamageResult(DamageResultDto value)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || value.roomSessionId != activeRoomSessionId || Presentation == null) return;
            if (CombatDamage == null || initializedLoadId == 0 || initializedLoadId != initializationLoadId)
            {
                if (pendingDamageResults.Count >= 256) { Fail(false, "Combat results exceeded the initialization buffer"); return; }
                pendingDamageResults.Enqueue(value);
                return;
            }
            CombatDamage.ApplyConfirmedResult(value);
        }

        public bool RequestLocalEquipmentChange(RoomEntityPresentation local, ItemData_Equipment item, bool unequip)
        {
            if (!IsSceneSession || returning || Presentation == null || !ReferenceEquals(local, Presentation.LocalEntity) ||
                local == null || !local.IsValid || !local.IsReady || item == null ||
                !activeRoomIsHost && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return false;
            var combat = local.View.GetComponent<Player>().Combat;
            var request = new EquipmentChangeRequestDto { roomSessionId = activeRoomSessionId, requestSeq = ++localEquipmentRequestSeq,
                expectedVersion = combat.EquipmentVersion, configurationId = item.name, slot = (RoomEquipmentSlot)item.equipmentType,
                kind = unequip ? EquipmentChangeKind.Unequip : EquipmentChangeKind.Equip };
            if (!activeRoomIsHost) return client.SendEquipmentChangeRequest(request);
            OnEquipmentChangeRequested(host.HostIdentity, request); return true;
        }

        private void OnEquipmentChangeRequested(RoomPlayerIdentity actor, EquipmentChangeRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || Presentation == null || actor == null ||
                actor.RoomSessionId != activeRoomSessionId ||
                !ReferenceEquals(actor, host.HostIdentity) && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return;
            var result = Players?.RequestEquipmentChange(actor, request, host.Results);
            if (result == null) return;
            if (Presentation.TryGetEntity(result.entityId, out var view))
            {
                view.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.healthVersion, result.health, result.maxHealth);
                view.ApplyFlaskState(result.roomSessionId, result.authorityTick, result.flask);
                view.NotifyEquipmentChanged();
            }
            host.SendEquipmentChangeResult(result);
            Debug.Log($"[RoomEquipment] player={result.playerId} request={result.requestSeq} accepted={result.accepted} reason={result.rejection} version={result.combat.equipmentVersion} resultId={result.resultId}");
        }

        private void OnEquipmentChangeResult(EquipmentChangeResultDto result) => OnInventoryResult(result);
        private void OnCraftResult(CraftResultDto result) => OnInventoryResult(result);
        private void OnInventoryResult(RoomInventoryResultDto result)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || result.roomSessionId != activeRoomSessionId || Presentation == null) return;
            if (CombatDamage == null || initializedLoadId == 0 || initializedLoadId != initializationLoadId)
            {
                if (pendingInventoryResults.Count >= 128) { Fail(false, "Inventory results exceeded the initialization buffer"); return; }
                pendingInventoryResults.Enqueue(result); return;
            }
            ProtocolCodec.ValidateMessage(result, true);
            if (!Presentation.TryGetEntity(result.entityId, out var view) || view.PlayerId != result.playerId ||
                !CombatDamage.Receipts.TryAccept(result)) return;
            view.View.GetComponent<Player>().Combat.ApplyInventoryConfirmation(result);
            view.ApplyFlaskState(result.roomSessionId, result.authorityTick, result.flask);
            view.ApplySkillTreePresentation(result.combat.skillTree, result.authorityTick);
            view.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.healthVersion, result.health, result.maxHealth);
            view.NotifyEquipmentChanged();
            if (result is CraftResultDto crafted)
                Debug.Log($"[RoomCraft] CONFIRMED player={crafted.playerId} request={crafted.requestSeq} accepted={crafted.accepted} reason={crafted.rejection} version={crafted.combat.equipmentVersion} resultId={crafted.resultId}");
        }

        public bool RequestLocalCraft(RoomEntityPresentation local, ItemData_Equipment recipe)
        {
            if (!IsSceneSession || returning || Presentation == null || !ReferenceEquals(local, Presentation.LocalEntity) ||
                local == null || !local.IsValid || !local.IsReady || recipe == null ||
                !activeRoomIsHost && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return false;
            var request = new CraftRequestDto { roomSessionId = activeRoomSessionId, requestSeq = ++localCraftRequestSeq,
                expectedVersion = local.View.GetComponent<Player>().Combat.EquipmentVersion, recipeId = RoomCraftCatalog.RecipeId(recipe) };
            if (!activeRoomIsHost) return client.SendCraftRequest(request);
            OnCraftRequested(host.HostIdentity, request); return true;
        }
        private void OnCraftRequested(RoomPlayerIdentity actor, CraftRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || Presentation == null || actor == null ||
                actor.RoomSessionId != activeRoomSessionId ||
                !ReferenceEquals(actor, host.HostIdentity) && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return;
            var result = Players?.RequestCraft(actor, request, host.Results);
            if (result == null) return;
            if (Presentation.TryGetEntity(result.entityId, out var view)) view.NotifyEquipmentChanged();
            host.SendCraftResult(result);
            Debug.Log($"[RoomCraft] player={result.playerId} request={result.requestSeq} recipe={result.recipeId} accepted={result.accepted} reason={result.rejection} product={result.productId} version={result.combat.equipmentVersion} resultId={result.resultId}");
        }

        public bool RequestLocalSkillUnlock(RoomEntityPresentation local, string nodeId)
        {
            if (!IsSceneSession || returning || Presentation == null || !ReferenceEquals(local, Presentation.LocalEntity) ||
                local == null || !local.IsValid || !local.IsReady || string.IsNullOrWhiteSpace(nodeId) ||
                !activeRoomIsHost && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return false;
            var request = new SkillUnlockRequestDto { roomSessionId = activeRoomSessionId, requestSeq = ++localSkillUnlockRequestSeq,
                expectedVersion = local.View.GetComponent<Player>().Combat.SkillTreeVersion, nodeId = nodeId };
            if (!activeRoomIsHost) return client.SendSkillUnlockRequest(request);
            OnSkillUnlockRequested(host.HostIdentity, request); return true;
        }
        private void OnSkillUnlockRequested(RoomPlayerIdentity actor, SkillUnlockRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || Presentation == null || actor == null ||
                actor.RoomSessionId != activeRoomSessionId ||
                !ReferenceEquals(actor, host.HostIdentity) && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return;
            var result = Players?.RequestSkillUnlock(actor, request, host.Results);
            if (result == null) return;
            if (Presentation.TryGetEntity(result.entityId, out var view))
            { view.ApplySkillTreePresentation(result.state, result.authorityTick); view.NotifyEquipmentChanged(); }
            host.SendSkillUnlockResult(result);
            LogSkillUnlock(result);
        }
        private void OnSkillUnlockResult(SkillUnlockResultDto result)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || result.roomSessionId != activeRoomSessionId || Presentation == null) return;
            if (CombatDamage == null || initializedLoadId == 0 || initializedLoadId != initializationLoadId)
            {
                if (pendingSkillUnlockResults.Count >= 128) { Fail(false, "Skill unlock results exceeded the initialization buffer"); return; }
                pendingSkillUnlockResults.Enqueue(result); return;
            }
            ProtocolCodec.ValidateMessage(result, true);
            if (!Presentation.TryGetEntity(result.entityId, out var view) || view.PlayerId != result.playerId || !CombatDamage.Receipts.TryAccept(result)) return;
            view.View.GetComponent<Player>().Combat.ApplyRoomSkillTreeSnapshot(result.state, result.authorityTick);
            view.ApplySkillTreePresentation(result.state, result.authorityTick); view.NotifyEquipmentChanged();
            LogSkillUnlock(result);
        }
        private static void LogSkillUnlock(SkillUnlockResultDto result) => Debug.Log(
            $"[RoomSkillUnlock] player={result.playerId} request={result.requestSeq} node={result.nodeId} accepted={result.accepted} reason={result.rejection} currency={result.state.currency} version={result.state.version} resultId={result.resultId}");

        private void OnFlaskUseRequested(RoomPlayerIdentity actor, FlaskUseRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || Presentation == null || actor == null || actor.RoomSessionId != activeRoomSessionId ||
                !ReferenceEquals(actor, host.HostIdentity) && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return;
            var result = Players?.RequestFlaskUse(actor, request, host.Results);
            if (result == null) return;
            if (Presentation.TryGetEntity(result.entityId, out var view)) ApplyFlaskResult(view, result);
            host.SendFlaskUseResult(result); LogFlaskResult(result);
        }
        private void OnFlaskUseResult(FlaskUseResultDto result)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || result.roomSessionId != activeRoomSessionId || Presentation == null) return;
            if (CombatDamage == null || initializedLoadId == 0 || initializedLoadId != initializationLoadId)
            {
                if (pendingFlaskResults.Count >= 128) { Fail(false, "Flask results exceeded the initialization buffer"); return; }
                pendingFlaskResults.Enqueue(result); return;
            }
            ProtocolCodec.ValidateMessage(result, true);
            if (!Presentation.TryGetEntity(result.entityId, out var view) || view.PlayerId != result.playerId || !CombatDamage.Receipts.TryAccept(result)) return;
            ApplyFlaskResult(view, result); LogFlaskResult(result);
        }
        private static void ApplyFlaskResult(RoomEntityPresentation view, FlaskUseResultDto result)
        {
            view.ApplyVersionedHealth(result.roomSessionId, result.authorityTick, result.healthVersion, result.health, result.maxHealth);
            view.ApplyFlaskState(result.roomSessionId, result.authorityTick, result.flask);
        }
        private static void LogFlaskResult(FlaskUseResultDto result) => Debug.Log(
            $"[RoomFlask] player={result.playerId} request={result.requestSeq} accepted={result.accepted} reason={result.rejection} heal={result.actualHealing} hp={result.health}/{result.maxHealth} cooldownEnd={result.flask.cooldownEndTick} tick={result.authorityTick} resultId={result.resultId}");

        private void OnMonsterStates(MonsterStatesDto value)
        {
            if (!IsSceneSession || returning || activeRoomIsHost || value.roomSessionId != activeRoomSessionId ||
                value.loadId != initializationLoadId || monsterInitializationLoadId != value.loadId) return;
            Monsters?.ApplyStates(value);
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

        private void OnLocalInputLockChanged(bool locked)
        {
            if (locked) client?.RequestMovementStop();
            if (!IsSceneSession || returning || activeRoomIsHost || initializedLoadId == 0 || initializedLoadId != initializationLoadId) return;
            client.SendLocalInputLock(locked);
        }

        private void OnGuestInputLock(RoomPlayerIdentity actor, LocalInputLockDto inputLock)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || initializedLoadId == 0 || initializedLoadId != initializationLoadId ||
                actor.RoomSessionId != activeRoomSessionId) return;
            Players?.ApplyGuestInputLock(actor, inputLock.locked);
        }

        private void OnPrimaryAttackRequested(RoomPlayerIdentity actor, PrimaryAttackRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || actor == null || actor.RoomSessionId != activeRoomSessionId) return;
            if (!ReferenceEquals(actor, host.HostIdentity) && (initializedLoadId == 0 || initializedLoadId != initializationLoadId)) return;
            Players?.RequestPrimaryAttack(actor, request);
        }

        private void OnGuestDashRequested(RoomPlayerIdentity actor, DashRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || initializedLoadId == 0 ||
                initializedLoadId != initializationLoadId || actor.RoomSessionId != activeRoomSessionId) return;
            Players?.RequestGuestDash(actor, request);
        }

        private void OnGuestJumpRequested(RoomPlayerIdentity actor, JumpRequestDto request)
        {
            if (!IsSceneSession || returning || !activeRoomIsHost || initializedLoadId == 0 ||
                initializedLoadId != initializationLoadId || actor.RoomSessionId != activeRoomSessionId) return;
            Players?.RequestGuestJump(actor, request);
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
            monsterInitializationLoadId = 0;
        }

        private void PrepareRoomObjects(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                swordTemplate = root.GetComponentInChildren<SwordSkill>(true);
                if (swordTemplate != null) break;
            }
            NetworkObjects = new RoomNetworkObjects(activeRoomSessionId, scene, () =>
            {
                InvalidateEntityInitialization();
                if (presentationBinding != null) presentationBinding.Unbind();
            }, playerId =>
            {
                if (presentationBinding != null) presentationBinding.UnbindPlayer(playerId);
            });
            Players = new RoomPlayerEntities(scene, RoomPlayerEntities.FindSceneTemplate(scene), hostSpawnOffset, guestSpawnOffset, NetworkObjects, positionTeleportDistance);
            Monsters = new RoomSceneMonsters(monsterCatalog ?? RoomSceneMonsters.Discover(scene), NetworkObjects);
        }

        // Exit, disconnect, failed initialization, scene unload and owner destruction share this route.
        private void CleanupNetworkObjects(int departingPlayerId = 0)
        {
            InvalidateEntityInitialization();
            if (departingPlayerId > 0)
            {
                CombatDamage?.RemovePlayerElements(departingPlayerId, GameSimulationClock.AuthorityTick);
                NetworkObjects?.CleanupPlayer(activeRoomSessionId, departingPlayerId);
                Presentation?.RemovePlayer(departingPlayerId);
                return;
            }
            swords?.Dispose(); swords = null; pendingSwordStates = null; swordStateTimer = 0;
            CombatDamage?.Dispose(); CombatDamage = null;
            pendingDamageResults.Clear();
            pendingInventoryResults.Clear(); localEquipmentRequestSeq = localCraftRequestSeq = 0;
            pendingSkillUnlockResults.Clear(); localSkillUnlockRequestSeq = 0;
            pendingFlaskResults.Clear(); flaskHudTimer = 0;
            pendingElementStates = null;
            if (NetworkObjects != null) NetworkObjects.Dispose();
            else if (presentationBinding != null) presentationBinding.Unbind();
            NetworkObjects = null;
            Players?.Dispose();
            Players = null;
            Monsters?.Dispose(); Monsters = null;
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

        [ContextMenu("Repeat Last Damage Result (Play Mode)")]
        private void RepeatLastDamageResult()
        {
            if (!Application.isPlaying || !IsSceneSession || returning || !activeRoomIsHost) return;
            if (CombatDamage?.RepeatLastResultForVerification() != true) Debug.Log("[HitFeedback] No authority damage result to repeat yet.");
        }

        [ContextMenu("Log Room Combat Configuration (Play Mode)")]
        private void LogRoomCombatConfiguration()
        {
            if (!Application.isPlaying || Players == null) return;
            foreach (var pair in Players.Entities)
            {
                var player = pair.Value;
                if (player == null || player.Combat.RoomConfigurationId == null) continue;
                var combat = player.Combat.CaptureRoomConfiguration();
                var skills = new System.Text.StringBuilder();
                foreach (var skill in combat.skills)
                    skills.Append($"{skill.skillId}={(skill.unlocked ? "unlocked" : "locked")}({skill.remaining:0.00}/{skill.cooldown:0.00}s) ");
                Debug.Log($"[RoomCombat] player={player.Combat.PlayerId} entity={pair.Key} role={player.GetComponent<NetworkEntityIdentity>().Role} profile={combat.configurationId} " +
                    $"HP={player.stats.currentHealth}/{player.stats.GetMaxHealthValue()} strength={player.stats.strength.GetValue()} damage={player.stats.damage.GetValue()} armor={player.stats.armor.GetValue()} " +
                    $"weapon={player.Combat.GetEquipment(EquipmentType.Weapon)?.name} armorItem={player.Combat.GetEquipment(EquipmentType.Armor)?.name} " +
                    $"bag={combat.inventory.Length}/{combat.inventoryCapacity} equipmentVersion={combat.equipmentVersion} skills={skills}");
            }
        }

        [ContextMenu("Repeat Player Initialization (Play Mode)")]
        private void RepeatPlayerInitialization()
        {
            if (!Application.isPlaying || !IsSceneSession || returning) return;
            var before = Presentation?.LocalEntity;
            OnBothReady();
            Debug.Log($"[RoomPlayers] repeat count={Players?.Entities.Count ?? 0} sameLocalBinding={ReferenceEquals(before, Presentation?.LocalEntity)}");
        }

        [ContextMenu("Repeat Monster Initialization (Play Mode)")]
        private void RepeatMonsterInitialization()
        {
            if (!Application.isPlaying || !IsSceneSession || returning || !activeRoomIsHost || initializedLoadId == 0 || Monsters == null) return;
            var value = Monsters.Capture(initializedLoadId, GameSimulationClock.AuthorityTick);
            host.SendMonsterInitialization(value);
            Debug.Log($"[RoomMonsters] repeat count={Monsters.Count} ids={string.Join(",", Array.ConvertAll(value.monsters, monster => monster.entityId.ToString()))}");
        }

        [ContextMenu("Verify Scene Preparation Failure (Play Mode)")]
        private void VerifyPreparationFailure()
        {
            if (Application.isPlaying && IsSceneSession && !returning) Fail(host.IsListening, "Scene preparation failure (validation)");
        }
        private void OnDestroy()
        {
            GameplayControlLock.Changed -= OnLocalInputLockChanged;
            ClearPresentation();
            ++operation;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (host != null)
            {
                host.StateChanged -= OnHostState; host.SceneInitializationAllowed -= OnBothReady;
                host.MovementInputReceived -= OnGuestMovementInput;
                host.JumpRequested -= OnGuestJumpRequested;
                host.DashRequested -= OnGuestDashRequested;
                host.PrimaryAttackRequested -= OnPrimaryAttackRequested;
                host.EquipmentChangeRequested -= OnEquipmentChangeRequested;
                host.CraftRequested -= OnCraftRequested;
                host.SkillUnlockRequested -= OnSkillUnlockRequested;
                host.FlaskUseRequested -= OnFlaskUseRequested;
                host.SwordInputReceived -= OnSwordInput;
                host.InputLockReceived -= OnGuestInputLock;
                host.RoomExited -= OnExit; host.Disconnected -= OnDisconnect;
            }
            if (client != null)
            {
                client.SceneLoadRequested -= OnSceneLoad; client.SceneInitializationAllowed -= OnBothReady;
                client.PlayerInitializationReceived -= OnPlayerInitialization;
                client.PlayerPositionsReceived -= OnPlayerPositions;
                client.MonsterInitializationReceived -= OnMonsterInitialization;
                client.MonsterStatesReceived -= OnMonsterStates;
                client.DamageResultReceived -= OnDamageResult;
                client.ElementStatesReceived -= OnElementStates;
                client.EquipmentChangeResultReceived -= OnEquipmentChangeResult;
                client.CraftResultReceived -= OnCraftResult;
                client.SkillUnlockResultReceived -= OnSkillUnlockResult;
                client.FlaskUseResultReceived -= OnFlaskUseResult;
                client.SwordStatesReceived -= OnSwordStates;
                client.RoomExited -= OnExit; client.Disconnected -= OnDisconnect;
            }
            if (ReferenceEquals(Active, this)) Active = null;
        }
    }
}
