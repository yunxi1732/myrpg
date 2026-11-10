using System;
using System.IO;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerAttackPresentationVerification
{
    [MenuItem("Tools/RPG/Verify Player Attack Presentation (4-34a)")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        VerifyProtocol(); VerifyTimeline(); VerifyControllerAndEvents();
        Debug.Log("[PlayerAttackPresentation 4-34a] PASS: initial/periodic attack DTOs, source ownership, authority start tick/facing, progress, duplicate/old/closed attack guards, reset and independent player streams; real clips and display event suppression. Dual-instance playback requires acceptance.");
    }
    private static PlayerAttackDto Attack(long id = 10, int source = 101, int combo = 1, ulong start = 103, int face = -1) =>
        new PlayerAttackDto { attackInstanceId = id, sourceEntityId = source, comboIndex = combo, startTick = start, durationTicks = 20, facingDir = face };
    private static void VerifyProtocol()
    {
        string room = Guid.NewGuid().ToString("N");
        var dto = new PlayerPositionsDto { roomSessionId = room, loadId = 1, authorityTick = 106, stateSeq = 2,
            players = new[] { new PlayerPositionDto { entityId = 101, facingDir = -1, position = new[] { 0f, 0f }, attack = Attack() },
                new PlayerPositionDto { entityId = 102, facingDir = 1, position = new[] { 2f, 0f } } } };
        var copy = (PlayerPositionsDto)RoundTrip(dto);
        Require(copy.players[0].attack.attackInstanceId == 10 && copy.players[0].attack.comboIndex == 1 && copy.players[0].attack.startTick == 103,
            "Attack metadata uses the existing position protocol and sole codec");
        dto.players[0].attack.sourceEntityId = 102; Invalid(() => ProtocolCodec.Encode(dto, true)); dto.players[0].attack.sourceEntityId = 101;
        dto.players[0].attack.comboIndex = 3; Invalid(() => ProtocolCodec.Encode(dto, true)); dto.players[0].attack.comboIndex = 1;
        dto.players[0].attack.startTick = 107; Invalid(() => ProtocolCodec.Encode(dto, true)); dto.players[0].attack.startTick = 103;
        dto.players[0].attack.durationTicks = 0; Invalid(() => ProtocolCodec.Encode(dto, true)); dto.players[0].attack.durationTicks = 20;
        var initial = new PlayerInitializationDto { roomSessionId = room, loadId = 1, authorityTick = 106, stateSeq = 1,
            players = new[] { new InitialPlayerDto { playerId = 1, entityId = 101, facingDir = -1, position = new[] { 0f, 0f }, health = 140, maxHealth = 140,
                healthVersion = 1, combat = RoomCombatConfiguration.CreateTestProfile(true), attack = Attack() },
                new InitialPlayerDto { playerId = 2, entityId = 102, facingDir = 1, position = new[] { 2f, 0f }, health = 180, maxHealth = 180,
                    healthVersion = 1, combat = RoomCombatConfiguration.CreateTestProfile(false) } } };
        Require(((PlayerInitializationDto)RoundTrip(initial)).players[0].attack.durationTicks == 20, "Joining B receives A's currently active attack progress");
        // Exercise the wire representation explicitly, rather than only validating DTOs before Unity serialization.
        initial.players[0].attack = null;
        Require(((PlayerInitializationDto)RoundTrip(initial)).players[0].attack == null &&
            ((PlayerInitializationDto)RoundTrip(initial)).players[1].attack == null, "Idle opening baseline decodes with two absent attacks");
        initial.players[0].attack = new PlayerAttackDto(); initial.players[1].attack = new PlayerAttackDto();
        var empty = (PlayerInitializationDto)ProtocolCodec.Decode((short)ProtocolId.PlayerInitialization,
            System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(initial)), true);
        Require(empty.players[0].attack == null && empty.players[1].attack == null, "Unity inline all-zero metadata is canonicalized as no attack");
        initial.players[0].attack.sourceEntityId = 101;
        Invalid(() => ProtocolCodec.Decode((short)ProtocolId.PlayerInitialization, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(initial)), true));
        dto.players[0].attack = new PlayerAttackDto(); dto.players[1].attack = new PlayerAttackDto();
        var idlePositions = (PlayerPositionsDto)ProtocolCodec.Decode((short)ProtocolId.PlayerPositions,
            System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(dto)), true);
        Require(idlePositions.players[0].attack == null && idlePositions.players[1].attack == null, "Idle periodic states also clear attacks rather than failing validation");
    }
    private static void VerifyTimeline()
    {
        var a = new AuthorityPositionBuffer(); var b = new AuthorityPositionBuffer();
        Require(a.Add(100, 1, Vector2.zero, 5, 5) && a.Add(106, 2, Vector2.zero, 5, 5, facingDir: -1, playerAttack: Attack()), "Buffer incoming A snapshots");
        a.Advance(.02f, .02f); Require(a.PlayerAttack == null, "Future attack does not play before authority start");
        a.Advance(.02f, .02f); Require(a.PlayerAttack?.attackInstanceId == 10 && a.FacingDirection == -1 && a.RenderTick >= 103, "Attack/facing start at authority tick between position samples");
        a.Advance(.04f, .02f); double progress = a.RenderTick;
        var repeated = Attack(); Require(a.Add(110, 3, Vector2.zero, 5, 5, facingDir: -1, playerAttack: repeated), "Repeat active attack");
        repeated.comboIndex = 2;
        a.Advance(.02f, .02f); Require(a.PlayerAttack.comboIndex == 1 && a.RenderTick > progress, "Repeated snapshot keeps progress and copies mutable DTOs");
        var altered = Attack(); altered.durationTicks++;
        Require(!a.Add(111, 4, Vector2.zero, 5, 5, facingDir: -1, playerAttack: altered) &&
            !a.Add(111, 4, Vector2.zero, 5, 5, facingDir: -1, playerAttack: Attack(9)), "Old attack IDs and changed metadata cannot override active attack");
        Require(a.Add(115, 4, Vector2.zero, 5, 5, facingDir: -1, playerAttack: Attack()) && a.Add(120, 5, Vector2.zero, 5, 5, facingDir: -1), "Buffer authoritative attack exit");
        Require(!a.Add(121, 6, Vector2.zero, 5, 5, facingDir: -1, playerAttack: Attack()), "An ended instance cannot reappear with a newer packet sequence");
        a.Advance(.1f, .02f); Require(a.PlayerAttack == null, "Exit sample ends playback");
        Require(a.Add(121, 6, Vector2.zero, 5, 5, playerAttack: Attack(11, combo: 2, start: 121, face: 1)), "New attack instance starts next strike");
        a.Advance(.02f, .02f); progress = a.RenderTick;
        Require(a.Add(122, 7, Vector2.zero, 5, 5, true, playerAttack: Attack(11, combo: 2, start: 121, face: 1)) && a.RenderTick >= progress,
            "Repeated initialization/teleport of the same action cannot rewind playback time");
        Require(b.Add(106, 1, Vector2.zero, 5, 5, playerAttack: Attack(2, 102, 0, 103, 1)), "B player's smaller attack ID uses an independent stream");
        b.Advance(.1f, .02f); Require(b.PlayerAttack?.sourceEntityId == 102 && a.PlayerAttack?.sourceEntityId == 101, "The two player action streams never mix");
        a.Clear(); Require(a.PlayerAttack == null && a.Count == 0 && a.Add(106, 1, Vector2.zero, 5, 5, facingDir: -1, playerAttack: Attack()), "A fresh display session resets the attack timeline");
    }
    private static void VerifyControllerAndEvents()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Controllers/Player_AC.controller");
        Require(controller != null, "Existing player animator controller");
        AnimatorStateMachine attacks = null;
        foreach (var child in controller.layers[0].stateMachine.stateMachines) if (child.stateMachine.name == "PrimaryAttack") attacks = child.stateMachine;
        Require(attacks != null, "Use the actual nested attack state machine");
        for (int i = 1; i <= 3; i++)
        {
            bool found = false;
            foreach (var state in attacks.states) if (state.state.name == "PlayerAttack" + i && state.state.motion is AnimationClip clip && clip.name == state.state.name) found = true;
            Require(found, "Existing full-path state and authored clip for strike " + i);
        }
        var scene = EditorSceneManager.NewPreviewScene();
        var room = Guid.NewGuid().ToString("N");
        var ctor = typeof(RoomPlayerIdentity).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)[0];
        using (var context = new RoomPresentationContext(room, 2, true, (RoomPlayerIdentity)ctor.Invoke(new object[] { room, 1, 101 }), (RoomPlayerIdentity)ctor.Invoke(new object[] { room, 2, 102 })))
        {
            try
            {
                var go = new GameObject("Display animation events probe"); go.SetActive(false); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                var animator = go.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.fireEvents = true;
                context.RegisterEntity(101, 1, go.transform);
                Require(!animator.fireEvents && go.GetComponent<NetworkEntityIdentity>().Role == NetworkEntityRole.Display, "Display animator events stay disabled before playback");
            }
            finally { context.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
    private static object RoundTrip(object dto)
    { using (var stream = new MemoryStream(ProtocolCodec.Encode(dto, true))) { Require(NetPacket.TryRead(stream, out short id, out byte[] body), "Frame"); return ProtocolCodec.Decode(id, body, true); } }
    private static void Invalid(Action action)
    { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Require(rejected, "Invalid action metadata rejected"); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[PlayerAttackPresentation 4-34a] FAIL: " + message); }
}
