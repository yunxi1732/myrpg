using System;
using System.Reflection;
using Rpg.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PrimaryAttackVerification
{
    [MenuItem("Tools/RPG/Verify Primary Attack Requests")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            string session = Guid.NewGuid().ToString("N");
            using (var room = new RoomNetworkObjects(session, scene))
            {
                var allocateMethod = typeof(RoomNetworkObjects).GetMethod("AllocatePrimaryAttackInstanceId", BindingFlags.Instance | BindingFlags.NonPublic);
                Func<long> allocate = () => (long)allocateMethod.Invoke(room, null);
                var a = new PrimaryAttackRuntime(session, 1, 101, allocate);
                var b = new PrimaryAttackRuntime(session, 2, 102, allocate);
                Require(a.TryQueue(100, 10, PrimaryAttackRejection.None), "A's first request may have sequence gaps");
                Require(!a.TryQueue(100, 10, PrimaryAttackRejection.None) && !a.TryQueue(99, 10, PrimaryAttackRejection.None), "Duplicate/old requests cannot replace the pending attack");
                Require(a.TryBegin(11, PrimaryAttackRejection.None, 29, 40), "A fixed-step commit succeeds");
                Require(a.Current.RequestSeq == 100 && a.Current.AttackInstanceId == 1 && a.Current.PlayerId == 1 &&
                    a.Current.EntityId == 101 && a.Current.RoomSessionId == session && a.Current.StartTick == 11 && a.Current.EndTick == 40,
                    "Authority instance has its own ID and source metadata");
                Require(!a.TryBegin(12, PrimaryAttackRejection.None, 29, 40), "One request cannot execute twice");
                Require(b.TryQueue(1, 11, PrimaryAttackRejection.None) && b.TryBegin(12, PrimaryAttackRejection.None, 29, 40) &&
                    b.Current.AttackInstanceId == 2 && b.Current.RequestSeq == 1, "B has an independent request stream and shares only the room instance allocator");
                Require(!a.TryQueue(101, 20, PrimaryAttackRejection.None) && a.LastRejection == PrimaryAttackRejection.Busy,
                    "Active attack rejects re-entry");
                Require(!a.TryQueue(102, 40, PrimaryAttackRejection.None) && a.LastRejection == PrimaryAttackRejection.Interval && a.NextAllowedTick == 51,
                    "Attack interval persists after animation ends, without refresh on rejection");
                Require(!a.TryQueue(102, 51, PrimaryAttackRejection.None), "Rejected request cannot replay when its interval expires");
                Require(a.TryQueue(105, 51, PrimaryAttackRejection.None) && !a.TryBegin(52, PrimaryAttackRejection.Dead, 29, 40) &&
                    !a.HasPending && a.NextAllowedTick == 51 && a.Current.AttackInstanceId == 1,
                    "Execution recheck drops a newly dead actor without starting interval or allocating an ID");
                foreach (PrimaryAttackRejection reason in Enum.GetValues(typeof(PrimaryAttackRejection)))
                {
                    if (reason == PrimaryAttackRejection.None || reason == PrimaryAttackRejection.InvalidSequence) continue;
                    long seq = a.LastRequestSeq + 1;
                    Require(!a.TryQueue(seq, 60, reason) && a.LastRequestSeq == seq && !a.HasPending && a.NextAllowedTick == 51,
                        "Invalid request consumed without cooldown: " + reason);
                }
                Require(a.TryQueue(200, 60, PrimaryAttackRejection.None), "New valid request after rejections");
                a.CancelPending();
                Require(!a.TryBegin(61, PrimaryAttackRejection.None, 29, 40) && !a.TryQueue(200, 61, PrimaryAttackRejection.None),
                    "Lock/disable cancellation does not allow replay");
                var replacement = new PrimaryAttackRuntime(session, 3, 103, allocate);
                Require(replacement.TryQueue(1, 70, PrimaryAttackRejection.None) && replacement.TryBegin(71, PrimaryAttackRejection.None, 29, 40) &&
                    replacement.Current.AttackInstanceId == 3 && replacement.NextAllowedTick == 111,
                    "Guest replacement has fresh request/interval state, with no reuse of room instance IDs");
            }
            Debug.Log("[PrimaryAttackRequests] PASS: per-actor request deduplication, execution recheck, rejection/cancellation, interval and room instance-ID allocation. Live actor conditions/connection ownership require handshake and dual-instance acceptance; no resultId or damage produced.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PrimaryAttackRequests] FAIL: " + message);
    }
}
