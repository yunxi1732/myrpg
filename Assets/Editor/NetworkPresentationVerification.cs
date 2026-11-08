using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Cinemachine;
using Rpg.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Temporary preview-scene bindings only. Invoke managed gates directly; do not send Unity lifecycle messages in preview scenes.</summary>
public static class NetworkPresentationVerification
{
    private static TcpRoomHost host;
    private static TcpRoomClient client;
    private static Stopwatch timer;

    [MenuItem("Tools/RPG/Verify Network Entity Identity")]
    public static void VerifyIdentity() => Verify();

    [MenuItem("Tools/RPG/Verify Network Presentation")]
    public static void Verify()
    {
        if (Application.isPlaying || timer != null) throw new InvalidOperationException("Run once outside Play mode.");
        if (!SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity").isLoaded)
            throw new InvalidOperationException("Open MainScene outside Play mode first. Scene objects will only be cloned into a temporary preview scene.");
        try
        {
            host = new TcpRoomHost(); client = new TcpRoomClient();
            host.Init(); client.Init();
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
            finally { reservation.Stop(); }
            host.StartListening(port); client.Connect("127.0.0.1", port);
            timer = Stopwatch.StartNew();
            EditorApplication.update += Check;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }
        catch { Cleanup(); throw; }
    }

    private static void Check()
    {
        try
        {
            Require(timer.Elapsed.TotalSeconds < 12, "Handshake timed out");
            host.Update(); client.Update();
            if (client.State == RoomConnectionState.Connecting || client.State == RoomConnectionState.Handshaking) return;
            Require(client.State == RoomConnectionState.Connected && host.GuestIdentity != null, "Use A-assigned identities");
            VerifyBindings();
            UnityEngine.Debug.Log("[NetworkPresentation] PASS: HUD/bars, authority-tick interpolation and snaps, reusable 2D physics roles, B camera/settings and session cleanup. Actual motion still needs dual-instance acceptance.");
            Cleanup();
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void VerifyBindings()
    {
        VerifyPositionTimeline();
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity");
        UI_InGame sourceHud = Find<UI_InGame>(source);
        UI_HealthBar template = Find<UI_HealthBar>(source);
        CinemachineVirtualCamera sourceCamera = Find<CinemachineVirtualCamera>(source);
        Require(sourceHud != null && template != null && sourceCamera != null, "MainScene presentation components");
        Scene preview = EditorSceneManager.NewPreviewScene();
        RoomPresentationContext a = null, b = null;
        NetworkScenePresentation binder = null;
        Exception failure = null;
        try
        {
            CopyRoot(sourceHud.transform.root.gameObject, preview);
            CopyRoot(sourceCamera.transform.root.gameObject, preview);
            VerifyStandaloneFallback(preview);
            UI_InGame hud = Find<UI_InGame>(preview);
            CinemachineVirtualCamera camera = Find<CinemachineVirtualCamera>(preview);
            CinemachineBrain brain = Find<CinemachineBrain>(preview);
            Require(hud != null && template != null && camera != null && brain != null, "Use the actual scene HUD, EntityStatusUI and camera");
            float lens = camera.m_Lens.OrthographicSize;
            var framing = camera.GetCinemachineComponent<CinemachineFramingTransposer>();
            Require(framing != null, "Existing framing transposer");
            Vector3 damping = new Vector3(framing.m_XDamping, framing.m_YDamping, framing.m_ZDamping);
            foreach (GameObject root in preview.GetRootGameObjects()) root.SetActive(false);
            var owner = new GameObject("Presentation verification");
            SceneManager.MoveGameObjectToScene(owner, preview);
            binder = owner.AddComponent<NetworkScenePresentation>();
            binder.Prepare(preview);
            camera.transform.root.gameObject.SetActive(true);
            var visibility = hud.GetComponent<CanvasGroup>();
            Require(visibility != null && visibility.alpha == 0 && camera.Follow == null, "No identity/data hides the entire HUD and clears camera");
            // Missing and destroyed Unity components must both be reacquired with Unity null semantics.
            UnityEngine.Object.DestroyImmediate(visibility);
            hud.UnbindNetwork();
            Require(hud.GetComponent<CanvasGroup>() == null, "Unbinding does not recreate components during cleanup");
            hud.BindNetwork(null);
            visibility = hud.GetComponent<CanvasGroup>();
            Require(visibility != null && visibility.alpha == 0, "Binding recreates a missing CanvasGroup after a destroyed cached reference");

            string room = host.RoomSessionId;
            a = new RoomPresentationContext(room, host.HostIdentity.PlayerId, false, host.HostIdentity, host.GuestIdentity);
            b = new RoomPresentationContext(room, client.LocalIdentity.PlayerId, true, client.HostIdentity, client.LocalIdentity);
            Transform first = MakeView(preview, "A display fixture", template, new Vector3(-8, 2));
            Transform second = MakeView(preview, "B display fixture", template, new Vector3(8, 2));
            AddRuntimeFixture(first); AddRuntimeFixture(second);
            // Register A first: B must never bind to the first object or the original solo player.
            var remote = b.RegisterEntity(host.HostIdentity.EntityId, host.HostIdentity.PlayerId, first);
            binder.Bind(b); Refresh(binder);
            Require(hud.BoundPlayerId == 0 && camera.Follow == null, "Remote registration cannot bind B HUD/camera");
            var local = b.RegisterEntity(client.LocalIdentity.EntityId, client.LocalIdentity.PlayerId, second);
            VerifyDisplayRuntime(first); VerifyDisplayRuntime(second);
            remote.MarkReady(); local.MarkReady();
            binder.Bind(b); Refresh(binder);
            Require(hud.BoundPlayerId == client.LocalIdentity.PlayerId && hud.GetComponent<CanvasGroup>().alpha == 0,
                "Bind by local playerId while waiting for state");
            var remoteBar = first.GetComponentInChildren<UI_HealthBar>(true);
            var localBar = second.GetComponentInChildren<UI_HealthBar>(true);
            Require(remoteBar.BoundEntityId == remote.EntityId && localBar.BoundEntityId == local.EntityId &&
                localBar.GetComponent<CanvasGroup>().alpha == 0, "Bars have separate entity IDs and hide before health");
            Require(remote.ApplyHealth(room, 10, 1, 20, 100) && local.ApplyHealth(room, 10, 1, 75, 150), "Separate confirmed health");
            Require(local.ApplyHud(room, 10, 1, 22, null, 0, new float[] { .1f, .2f, .3f, .4f, .5f }), "Independent HUD stream accepts its first sequence");
            Refresh(binder);
            var serializedHud = new SerializedObject(hud);
            var hudHealth = (Slider)serializedHud.FindProperty("slider").objectReferenceValue;
            Require(hud.gameObject.activeInHierarchy && hud.GetComponent<CanvasGroup>().alpha == 1 && hudHealth.value == 75 && hudHealth.maxValue == 150,
                "Whole scene HUD shows B confirmed state");
            Require(remoteBar.GetComponentInChildren<Slider>(true).value == 20 && localBar.GetComponentInChildren<Slider>(true).value == 75,
                "World bars cannot cross-bind health");
            Require(camera.Follow == second && brain.m_UpdateMethod == CinemachineBrain.UpdateMethod.ManualUpdate,
                "B camera follows its display entity after display updates");
            Require(camera.m_Lens.OrthographicSize == lens && new Vector3(framing.m_XDamping, framing.m_YDamping, framing.m_ZDamping) == damping,
                "Existing lens and damping retained");
            Require(!local.ApplyHealth("old-room", 11, 2, 1, 150) && !local.ApplyHealth(room, 9, 2, 1, 150) && !local.ApplyHealth(room, 11, 1, 1, 150),
                "Wrong session, older tick and duplicate sequence cannot overwrite");
            Require(local.ApplyPosition(room, 10, 100, new Vector2(1, 2)) &&
                remote.ApplyPosition(room, 10, 1, new Vector2(-1, 2)), "Position sequence is tracked separately per entity");
            Require(!local.ApplyPosition("old-room", 11, 101, Vector2.zero) &&
                !local.ApplyPosition(room, 9, 101, Vector2.zero) &&
                !local.ApplyPosition(room, 11, 100, Vector2.zero) && second.position.x == 1 && second.position.y == 2,
                "Wrong session, older tick and repeated position sequence never rewind a display");
            Require(local.ApplyPosition(room, 10, 101, new Vector2(3, 4)), "A newer sequence may correct the same authority tick");
            second.GetComponent<RemotePlayerView>().AdvanceDisplay(GameSimulationClock.FixedDeltaTime * 5);
            Require(second.position.x == 3 && second.position.y == 4, "Same-tick correction is displayed on its authority timeline");
            Require(local.ApplyPosition(room, 15, 102, new Vector2(4, 4), true) &&
                second.position.x == 4 && second.GetComponent<RemotePlayerView>().BufferedStateCount == 1,
                "Reinitialization clears the old path and places the display immediately");
            second.GetComponent<RemotePlayerView>().TeleportDistance = 2;
            Require(local.ApplyPosition(room, 20, 103, new Vector2(10, 4)) && second.position.x == 10 &&
                second.GetComponent<RemotePlayerView>().BufferedStateCount == 1, "Configured large-jump threshold clears the buffer and snaps");
            Require(local.ApplyHealth(room, 11, 2, 75, 150), "High position sequence cannot suppress a separate health stream");
            Transform ownView = MakeView(preview, "A local fixture", template, Vector3.zero);
            AddRuntimeFixture(ownView);
            var ownA = a.RegisterEntity(host.HostIdentity.EntityId, host.HostIdentity.PlayerId, ownView);
            ownA.MarkReady();
            Transform authorityGuest = MakeView(preview, "A guest authority fixture", template, Vector3.right);
            AddRuntimeFixture(authorityGuest);
            a.RegisterEntity(host.GuestIdentity.EntityId, host.GuestIdentity.PlayerId, authorityGuest);
            VerifyRoleMatrix(ownView, authorityGuest, first, second);
            VerifyReusablePhysicsRole(preview, a, b);
            a.RemoveRemotePlayers();
            Require(ReferenceEquals(a.LocalEntity, ownA), "Guest departure preserves A local binding");
            bool oldGuestRejected = false;
            try { a.RegisterEntity(host.GuestIdentity.EntityId, host.GuestIdentity.PlayerId, second); }
            catch (ArgumentException) { oldGuestRejected = true; }
            Require(oldGuestRejected, "Departed guest identity cannot register again without admission");
            b.Dispose(); Refresh(binder);
            Require(!local.IsValid && b.LocalEntity == null && hud.BoundPlayerId == 0 && localBar.BoundEntityId == 0 &&
                hud.GetComponent<CanvasGroup>().alpha == 0 && camera.Follow == null && !brain.enabled, "Leaving clears identities, subscriptions, HUD, bars and camera");
            Require(!local.ApplyHealth(room, 12, 3, 1, 150), "Old presentation rejects late state after exit");
            Require(!local.ApplyPosition(room, 25, 104, Vector2.zero), "Expired entity rejects late positions after exit");
            var expired = second.GetComponent<NetworkEntityIdentity>();
            Require(expired.Role == NetworkEntityRole.Unassigned && !expired.IsLocalInputOwner &&
                !EntityRuntimePermissions.CanSimulate(second) && !EntityRuntimePermissions.CanPersist(second),
                "Expired identity cannot regain single-player permissions");
            using (var replacement = new RoomPresentationContext(room, client.LocalIdentity.PlayerId, true, client.HostIdentity, client.LocalIdentity))
            {
                bool retiredRejected = false;
                try { replacement.RegisterEntity(client.LocalIdentity.EntityId, client.LocalIdentity.PlayerId, second); }
                catch (ObjectDisposedException) { retiredRejected = true; }
                Require(retiredRejected, "A new session cannot revive an expired actor's old control state");
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            CleanupStep(() => { if (binder != null) binder.Unbind(); }, ref failure);
            CleanupStep(() => a?.Dispose(), ref failure);
            CleanupStep(() => b?.Dispose(), ref failure);
            CleanupStep(() => EditorSceneManager.ClosePreviewScene(preview), ref failure);
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        UnityEngine.Debug.Log("[NetworkEntityIdentity] PASS: A has two authoritative roles, B has two display roles; input ownership is independent; display physics, state updates, animation attacks, damage, pickup and saves are blocked; camera selects B by playerId; exit revokes identity.");
    }

    private static void CleanupStep(Action cleanup, ref Exception failure)
    {
        try { cleanup(); }
        catch (Exception error)
        {
            failure = failure == null ? error : new AggregateException("Presentation verification and cleanup errors", failure, error);
        }
    }

    private static Transform MakeView(Scene scene, string name, UI_HealthBar template, Vector3 position)
    {
        var view = new GameObject(name);
        SceneManager.MoveGameObjectToScene(view, scene);
        view.transform.position = position;
        var bar = UnityEngine.Object.Instantiate(template.gameObject, view.transform);
        bar.SetActive(true);
        return view.transform;
    }

    private static void AddRuntimeFixture(Transform view)
    {
        var body = view.gameObject.AddComponent<Rigidbody2D>();
        body.gravityScale = 3;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.velocity = Vector2.one;
        view.gameObject.AddComponent<CapsuleCollider2D>();
        var trigger = new GameObject("Child gameplay trigger");
        trigger.transform.SetParent(view, false);
        trigger.AddComponent<BoxCollider2D>().isTrigger = true;
        var player = view.gameObject.AddComponent<Player>();
        view.gameObject.AddComponent<PlayerStats>();
        view.gameObject.AddComponent<PlayerItemDrop>();
        var manager = view.gameObject.AddComponent<PlayerManager>();
        manager.player = player;
        var visual = new GameObject("Visual and animation callbacks");
        visual.transform.SetParent(view, false);
        var animator = visual.AddComponent<Animator>();
        animator.applyRootMotion = true;
        visual.AddComponent<PlayerAnimationTriggers>();
        // Valid gizmo references without starting any controller or using the scene's real player.
        player.attackCheck = visual.transform;
        var serialized = new SerializedObject(player);
        serialized.FindProperty("groundCheck").objectReferenceValue = visual.transform;
        serialized.FindProperty("wallCheck").objectReferenceValue = visual.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void VerifyDisplayRuntime(Transform view)
    {
        var identity = view.GetComponent<NetworkEntityIdentity>();
        var player = view.GetComponent<Player>();
        var stats = view.GetComponent<PlayerStats>();
        var body = view.GetComponent<Rigidbody2D>();
        Require(identity.Role == NetworkEntityRole.Display && !player.IsAuthoritative && !body.simulated && body.velocity == Vector2.zero &&
            body.gravityScale == 0 && body.interpolation == RigidbodyInterpolation2D.None && !view.GetComponent<Collider2D>().enabled &&
            !view.GetComponentInChildren<Animator>().applyRootMotion, "Display has animation but no local physics, gravity, colliders or root motion");
        foreach (Collider2D collider in view.GetComponentsInChildren<Collider2D>(true))
            Require(!collider.enabled, "Display child/static trigger colliders are also excluded from gameplay physics");
        stats.currentHealth = 87;
        stats.isIgnited = true;
        InvokeManagedMethod(player, "Start"); InvokeManagedMethod(stats, "Start");
        InvokeManagedMethod(player, "Update"); InvokeManagedMethod(stats, "Update");
        player.SetVelocity(100, 100); player.CheckForDashInput(); player.SlowEntityBy(.5f, 1);
        player.Die(); player.DamageImpact(); player.AnimationTrigger();
        Require(!player.BusyFor(1).MoveNext() && player.stateMachine?.currentState == null && body.velocity == Vector2.zero,
            "Display never enters or advances the legacy player state machine");
        var machine = new PlayerStateMachine();
        var forbiddenState = new PlayerState(player, machine, "Idle");
        machine.Initialize(forbiddenState); machine.ChangeState(forbiddenState);
        Require(machine.currentState == null, "Direct state transitions cannot start display gameplay");
        var callbacks = view.GetComponentInChildren<PlayerAnimationTriggers>();
        InvokeManagedMethod(callbacks, "AttackTrigger"); InvokeManagedMethod(callbacks, "ThrowSword");
        stats.TakeDamage(100); stats.DecreaseHealthBy(100); stats.IncreaseHealthBy(100);
        stats.ApplyAilments(true, true, true); stats.KillEntity(); stats.DoDamage(stats); stats.CloneDoDamage(stats, 2);
        Require(stats.currentHealth == 87 && !stats.isDead, "Display cannot calculate health, damage, status effects or death");
        view.GetComponent<PlayerItemDrop>().GenerateDrop();
        view.gameObject.AddComponent<ItemObject>().PickUpItem();
        var trigger = view.gameObject.AddComponent<ItemObjectTrigger>();
        InvokeManagedMethod(trigger, "OnTriggerEnter2D", view.GetComponent<Collider2D>());
        var manager = view.GetComponent<PlayerManager>();
        manager.currency = 99;
        var data = new GameData { currency = 37 };
        manager.LoadData(data); manager.SaveData(ref data);
        Require(manager.currency == 99 && data.currency == 37 && !manager.HaveEnoughMoney(1),
            "Display cannot read/write the single-player save or spend local currency");
    }

    private static void VerifyRoleMatrix(Transform a, Transform aGuest, Transform bHost, Transform b)
    {
        var aa = a.GetComponent<NetworkEntityIdentity>();
        var ab = aGuest.GetComponent<NetworkEntityIdentity>();
        var ba = bHost.GetComponent<NetworkEntityIdentity>();
        var bb = b.GetComponent<NetworkEntityIdentity>();
        Require(aa.IsAuthoritative && ab.IsAuthoritative && !ba.IsAuthoritative && !bb.IsAuthoritative,
            "Both A entities are authoritative; both B entities are display-only");
        Require(aa.IsLocalInputOwner && !ab.IsLocalInputOwner && !ba.IsLocalInputOwner && bb.IsLocalInputOwner,
            "Each machine collects input only for its own playerId");
        Require(EntityRuntimePermissions.CanReadSimulationInput(a) && !EntityRuntimePermissions.CanReadSimulationInput(aGuest) &&
            !EntityRuntimePermissions.CanReadSimulationInput(b), "A keyboard cannot control the guest; B local input ownership cannot enable simulation");
        Require(a.GetComponent<Rigidbody2D>().simulated && aGuest.GetComponent<Rigidbody2D>().simulated,
            "A remote authority retains automatic physical simulation");
        Require(a.GetComponent<Collider2D>().enabled && aGuest.GetComponent<Collider2D>().enabled &&
            a.GetComponent<RemotePlayerView>() == null && aGuest.GetComponent<RemotePlayerView>() == null,
            "Both A authorities retain their colliders and never run display interpolation");
        Require(a.GetComponent<Rigidbody2D>().gravityScale == 3 && aGuest.GetComponent<Rigidbody2D>().gravityScale == 3 &&
            a.GetComponent<Rigidbody2D>().interpolation == RigidbodyInterpolation2D.Interpolate,
            "Authority preserves the existing gravity and Rigidbody2D configuration");
        var localPlayer = a.GetComponent<Player>();
        var guestPlayer = aGuest.GetComponent<Player>();
        // Invoking virtual Entity.Start on Player would dispatch to Player.Start without Awake.
        // This probe only needs the Rigidbody reference; do not start an edit-mode player controller.
        var bodySetter = typeof(Entity).GetProperty(nameof(Entity.rb)).GetSetMethod(true);
        Require(bodySetter != null, "Entity Rigidbody fixture setter exists");
        bodySetter.Invoke(localPlayer, new object[] { a.GetComponent<Rigidbody2D>() });
        bodySetter.Invoke(guestPlayer, new object[] { aGuest.GetComponent<Rigidbody2D>() });
        localPlayer.SetVelocity(3, 2); guestPlayer.SetVelocity(5, -2);
        Require(localPlayer.rb.velocity == new Vector2(3, 2) && guestPlayer.rb.velocity == new Vector2(5, -2),
            "Authoritative movement is permitted for A guest independently of local input ownership");
        var source = a.GetComponent<PlayerStats>();
        var target = aGuest.GetComponent<PlayerStats>();
        target.currentHealth = 83;
        source.DoDamage(target); source.CloneDoDamage(target, 2); source.DoMagicalDamage(target);
        Require(target.currentHealth == 83 && !EntityRuntimePermissions.CanDamage(source, target), "Network players cannot damage each other");
    }

    private static void VerifyReusablePhysicsRole(Scene scene, RoomPresentationContext authority, RoomPresentationContext display)
    {
        foreach (bool isDisplay in new[] { false, true })
        {
            var value = new GameObject("Generic entity physics fixture");
            value.SetActive(false);
            SceneManager.MoveGameObjectToScene(value, scene);
            var body = value.AddComponent<Rigidbody2D>();
            body.gravityScale = 2;
            var collider = value.AddComponent<BoxCollider2D>();
            (isDisplay ? display : authority).RegisterEntity(isDisplay ? 1002 : 1001, 0, value.transform);
            var identity = value.GetComponent<NetworkEntityIdentity>();
            Require(identity.IsAuthoritative == !isDisplay && !identity.IsLocalInputOwner &&
                body.simulated == !isDisplay && collider.enabled == !isDisplay && body.gravityScale == (isDisplay ? 0 : 2),
                "Non-player objects reuse the same role/physics component without Player or CharacterController");
        }
    }

    private static void VerifyPositionTimeline()
    {
        var buffer = new AuthorityPositionBuffer();
        Require(buffer.Add(100, 1, Vector2.zero, 5, 5) && buffer.Snapped && buffer.Count == 1,
            "Birth places immediately and seeds the authority timeline");
        Require(buffer.Add(105, 2, Vector2.right, 5, 5) && !buffer.Snapped, "Normal movement buffers a second tick");
        buffer.Advance(.05f, .02f);
        Require(Mathf.Abs(buffer.Position.x - .5f) < .001f, "Midpoint comes from authority ticks, not arrival intervals");
        Require(!buffer.Add(104, 3, Vector2.left, 5, 5) && !buffer.Add(106, 2, Vector2.left, 5, 5),
            "Old ticks and repeated sequences cannot re-enter the buffer");
        buffer.Advance(.05f, .02f); buffer.Advance(1, .02f);
        Require(buffer.Position == Vector2.right && buffer.RenderTick == 105 && buffer.Count == 1,
            "Consumed samples are removed; gaps hold the latest position without prediction");
        Require(buffer.Add(110, 3, new Vector2(20, 0), 5, 5) && buffer.Snapped && buffer.Count == 1 && buffer.Position.x == 20,
            "Large jump clears the old path and places immediately");
        Require(buffer.Add(115, 4, new Vector2(21, 0), 5, 5), "Movement resumes after a snap");
        buffer.Advance(.05f, .02f);
        Require(Mathf.Abs(buffer.Position.x - 20.5f) < .001f, "Interpolation resumes on the new timeline");
        Require(buffer.Add(116, 5, new Vector2(22, 0), 5, 5, true) && buffer.Snapped && buffer.Count == 1 && buffer.Position.x == 22,
            "Reinitialization explicitly resets interpolation even for a small displacement");
        buffer.Clear();
        Require(buffer.Add(1, 1, Vector2.zero, 1000, 1000), "A new entity lifetime has no old tick/sequence state");
        for (int i = 2; i <= 50; i++) buffer.Add((ulong)i, i, Vector2.right * i, 1000, 1000);
        Require(buffer.Count <= 32, "Jitter/initialization buffers remain bounded");
    }

    private static void InvokeManagedMethod(Component target, string method, params object[] arguments)
    {
        var callback = target.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(callback != null, "Missing verification callback: " + target.GetType().Name + "." + method);
        try { callback.Invoke(target, arguments); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }

    private static void VerifyStandaloneFallback(Scene preview)
    {
        var probe = new GameObject("Standalone permissions fixture");
        SceneManager.MoveGameObjectToScene(probe, preview);
        AddRuntimeFixture(probe.transform);
        var player = probe.GetComponent<Player>();
        Require(player.IsAuthoritative && player.IsLocalInputOwner && player.CanReadSimulationInput && player.CanUseLegacyActions &&
            EntityRuntimePermissions.CanPersist(player), "A standalone player without network identity retains local gameplay permissions");
        var manager = probe.GetComponent<PlayerManager>();
        var data = new GameData { currency = 17 };
        manager.LoadData(data);
        Require(manager.currency == 17, "Single-player load is still permitted");
        manager.currency = 23;
        manager.SaveData(ref data);
        Require(data.currency == 23, "Single-player save participant is still permitted");
    }

    private static void CopyRoot(GameObject source, Scene preview)
    {
        var staging = new GameObject("Inactive staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, preview);
        try
        {
            var copy = UnityEngine.Object.Instantiate(source, staging.transform);
            copy.SetActive(false);
            copy.transform.SetParent(null, true);
        }
        finally { UnityEngine.Object.DestroyImmediate(staging); }
    }

    private static T Find<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }
    private static void Refresh(NetworkScenePresentation binder) => InvokeManagedMethod(binder, "LateUpdate");
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("[NetworkPresentation] FAIL: " + message); }
    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { client?.Dispose(); }
        finally { host?.Dispose(); host = null; client = null; timer = null; }
    }
}
