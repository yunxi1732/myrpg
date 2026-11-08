using System;
using Rpg.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomObjectCleanupVerification
{
    private sealed class Publisher
    {
        public event Action Changed;
        public void Raise() => Changed?.Invoke();
    }

    [MenuItem("Tools/RPG/Verify Room Object Cleanup")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode. Uses only temporary preview objects.");
        Scene scene = EditorSceneManager.NewPreviewScene();
        RoomNetworkObjects scope = null, replacement = null;
        try
        {
            string room = Guid.NewGuid().ToString("N");
            var publisher = new Publisher();
            int aEvents = 0, bEvents = 0, detachA = 0, detachB = 0, roomClosing = 0, playerClosing = 0;
            Action onA = () => aEvents++;
            Action onB = () => bEvents++;
            publisher.Changed += onA; publisher.Changed += onB;
            scope = new RoomNetworkObjects(room, scene, () => roomClosing++, playerId =>
            {
                Require(playerId == 2, "Only the departing guest receives player cleanup");
                playerClosing++;
                Require(!scope.CleanupPlayer(room, playerId), "Cleanup cannot re-enter the same player");
            });
            var root = Create(scene, "Room objects");
            scope.Register(room, 0, root);
            var a = Create(scene, "Host player", root.transform);
            var b = Create(scene, "Guest player", root.transform);
            Action detachHost = () => { detachA++; publisher.Changed -= onA; };
            Action detachGuest = () => { detachB++; publisher.Changed -= onB; };
            var aTicket = scope.Register(room, 1, a, detachHost);
            var bTicket = scope.Register(room, 2, b, detachGuest);
            Require(ReferenceEquals(bTicket, scope.Register(room, 2, b, detachGuest)), "Duplicate registration is idempotent");
            var hostSkill = Create(scene, "Detached host skill", root.transform);
            var guestSkill = Create(scene, "Detached guest skill", root.transform);
            scope.RegisterOwned(a.GetComponent<RoomNetworkObject>(), hostSkill);
            scope.RegisterOwned(b.GetComponent<RoomNetworkObject>(), guestSkill);
            Require(scope.Count == 5 && guestSkill.GetComponent<RoomNetworkObject>().OwnerPlayerId == 2,
                "Derived objects inherit source ownership even outside the player's hierarchy");
            publisher.Raise();
            Require(aEvents == 1 && bEvents == 1, "Both subscriptions initially active");
            Require(!scope.CleanupPlayer(Guid.NewGuid().ToString("N"), 2) && scope.Count == 5, "Old room cannot clean the current room");
            Require(scope.CleanupPlayer(room, 2), "Guest cleanup accepted once");
            Require(a != null && a.activeSelf && hostSkill != null && root != null && b == null && guestSkill == null &&
                scope.Count == 3 && detachA == 0 && detachB == 1 && playerClosing == 1,
                "Guest body and detached skill are destroyed while host and room remain");
            publisher.Raise();
            Require(aEvents == 2 && bEvents == 1, "Guest event unsubscribed without clearing host event");
            Require(!scope.CleanupPlayer(room, 2), "Repeated leave is harmless");
            var late = Create(scene, "Late old-guest object", root.transform);
            bool rejected = false;
            try { scope.Register(room, 2, late); }
            catch (InvalidOperationException) { rejected = true; }
            finally { UnityEngine.Object.DestroyImmediate(late); }
            Require(rejected, "Departed owner cannot register late objects");
            var nextGuest = Create(scene, "New guest identity", root.transform);
            scope.Register(room, 3, nextGuest);
            bTicket.Dispose();
            Require(nextGuest != null && scope.Count == 4, "Old cleanup ticket cannot affect a newly admitted player");
            scope.Dispose(); scope.Dispose(); aTicket.Dispose();
            publisher.Raise();
            Require(scope.Count == 0 && scope.IsDisposed && roomClosing == 1 && root == null && nextGuest == null &&
                detachA == 1 && detachB == 1 && aEvents == 2 && bEvents == 1 && scope.LastCleanupError == null,
                "Room closes objects, subscriptions and registrations exactly once");
            replacement = new RoomNetworkObjects(Guid.NewGuid().ToString("N"), scene);
            var newRoomObject = Create(scene, "New room object");
            replacement.Register(replacement.RoomSessionId, 1, newRoomObject);
            bTicket.Dispose(); scope.Dispose();
            Require(newRoomObject != null && replacement.Count == 1, "Old room cleanup cannot affect a new room");
            UnityEngine.Debug.Log("[RoomObjectCleanup] PASS: owner-scoped destruction, detached derived objects, event unsubscription, repeated/reentrant cleanup, late-owner rejection and old-room isolation. Live HUD/camera and scene return still need dual-instance acceptance.");
        }
        finally
        {
            try { scope?.Dispose(); }
            finally
            {
                try { replacement?.Dispose(); }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
    }

    private static GameObject Create(Scene scene, string name, Transform parent = null)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        if (parent != null) value.transform.SetParent(parent, false);
        return value;
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("[RoomObjectCleanup] FAIL: " + message); }
}
