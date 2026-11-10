using System;
using System.Reflection;
using Rpg.Networking;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomStatsPanelVerification
{
    [MenuItem("Tools/RPG/Verify Room Local Stat Panel")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            VerifyBinding(scene, false); VerifyBinding(scene, true);
            Debug.Log("[RoomStatsPanel] PASS: A/B local ownership, confirmed room stats, not-ready placeholders, remote rejection, open/close subscription cleanup and old-session callback isolation. C-key/panel presentation needs dual-instance acceptance.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void VerifyBinding(Scene scene, bool guest)
    {
        string room = Guid.NewGuid().ToString("N");
        using (var context = Context(room, guest))
        {
            var a = MakePlayer(scene, context, 1, 101, true);
            var b = MakePlayer(scene, context, 2, 102, false);
            var local = guest ? b : a; var remote = guest ? a : b;
            int localId = guest ? 102 : 101;
            context.TryGetEntity(localId, out var view);
            context.TryGetEntity(guest ? 101 : 102, out var other);
            var widget = Body(scene, "Room stat slot");
            var label = widget.AddComponent<TextMeshProUGUI>(); var slot = widget.AddComponent<UI_StatSlot>();
            Field(slot, "statValueText", label); Field(slot, "statType", StatType.damage);
            slot.BindNetwork(view); Invoke(slot, "OnEnable");
            Require(label.text == "—" && slot.BoundPlayerId == 0, "Unready entity never exposes old/single-player values");
            view.MarkReady(); other.MarkReady();
            Require(label.text == (guest ? "35" : "18") && slot.BoundPlayerId == (guest ? 2 : 1), "Each side reads its own room player's damage+strength");
            Field(slot, "statType", StatType.health); slot.UpdateStatValueUI();
            Require(label.text == (guest ? "180" : "140"), "Maximum health uses the correct room profile and B-confirmed max");
            slot.BindNetwork(other);
            Require(label.text == "—" && slot.BoundPlayerId == 0, "Binding the other admitted player is rejected");
            slot.BindNetwork(view); Invoke(slot, "OnEnable");
            var listeners = (Action)typeof(RoomEntityPresentation).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var slotListener = (Action)typeof(UI_StatSlot).GetField("networkChanged", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(slot);
            Invoke(slot, "OnDisable");
            var detached = (Action)typeof(RoomEntityPresentation).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            foreach (var callback in detached.GetInvocationList()) Require(!callback.Equals(slotListener), "Closing removes this slot's callback");
            slot.BindNetwork(null); Require(label.text == "—", "Unbind clears the visible value");

            string nextRoom = Guid.NewGuid().ToString("N");
            using (var next = Context(nextRoom, guest))
            {
                MakePlayer(scene, next, 1, 101, true); MakePlayer(scene, next, 2, 102, false);
                next.TryGetEntity(localId, out var nextView); nextView.MarkReady();
                Field(slot, "statType", StatType.damage); slot.BindNetwork(nextView); Invoke(slot, "OnEnable");
                context.Dispose(); // A captured delegate from the old session must never restore the old actor/data.
                listeners?.Invoke();
                Require(slot.BoundPlayerId == (guest ? 2 : 1) && label.text == (guest ? "35" : "18"), "Old session invalidation/callback cannot clear or overwrite the new binding");
                next.Dispose(); Require(label.text == "—" && slot.BoundPlayerId == 0, "Leaving clears a visible binding without singleton fallback");
            }
            UnityEngine.Object.DestroyImmediate(widget);
            listeners?.Invoke(); // Late managed callbacks must not access destroyed native label/components.
        }
    }

    private static Player MakePlayer(Scene scene, RoomPresentationContext context, int playerId, int entityId, bool hostProfile)
    {
        var go = Body(scene, "Room player " + playerId); var player = go.AddComponent<Player>(); var stats = go.AddComponent<PlayerStats>();
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        var view = context.RegisterEntity(entityId, playerId, go.transform);
        if (!context.IsGuest) RoomCombatConfiguration.InitializeAuthority(player, hostProfile);
        else typeof(PlayerCombatContext).GetMethod("ApplyConfirmedRoomConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(player.Combat, new object[] { RoomCombatConfiguration.CreateTestProfile(hostProfile), context.RoomSessionId, 1UL, 1L });
        Set(typeof(Entity), player, "stats", stats); Set(typeof(Player), player, "IsRuntimeInitialized", true);
        Set(typeof(CharaterStats), stats, "IsRuntimeInitialized", true);
        int hp = hostProfile ? 140 : 180; stats.currentHealth = hp;
        view.ApplyVersionedHealth(context.RoomSessionId, 1, 1, hp, hp);
        return player;
    }
    private static GameObject Body(Scene scene, string name)
    { var go = new GameObject(name); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); return go; }
    private static RoomPresentationContext Context(string room, bool guest) => new RoomPresentationContext(room, guest ? 2 : 1, guest, Identity(room, 1, 101), Identity(room, 2, 102));
    private static RoomPlayerIdentity Identity(string room, int player, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static void Field(object value, string name, object data) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, data);
    private static void Set(Type type, object value, string name, object data) => type.GetProperty(name).GetSetMethod(true).Invoke(value, new[] { data });
    private static void Invoke(object value, string name) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[RoomStatsPanel] FAIL: " + message); }
}
