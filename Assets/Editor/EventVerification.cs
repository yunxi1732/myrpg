using System;
using UnityEditor;
using UnityEngine;

public static class EventVerification
{
    private sealed class Changed { }
    private sealed class Other { }

    [MenuItem("Tools/RPG/Verify Events")]
    public static void Verify()
    {
        IArchitecture architecture = new Architecture();
        int received = 0;
        object payload = new object();
        Action<object> listener = data =>
        {
            Require(ReferenceEquals(data, payload), "Payload identity");
            received++;
        };
        architecture.SendEvent<Changed>(payload);
        architecture.RegistEvent<Changed>(listener);
        architecture.SendEvent<Other>(payload);
        Require(received == 0, "Event type isolation");
        architecture.SendEvent<Changed>(payload);
        Require(received == 1, "Registered listener receives notification");
        architecture.UnRegistEvent<Changed>(listener);
        architecture.SendEvent<Changed>(payload);
        Require(received == 1, "Unregistered listener receives nothing");
        architecture.UnRegistEvent<Changed>(listener);
        architecture.RegistEvent<Changed>(listener);
        architecture.RegistEvent<Changed>(listener);
        architecture.SendEvent<Changed>(payload);
        Require(received == 3, "Repeated registration invokes twice");
        architecture.UnRegistEvent<Changed>(listener);
        architecture.SendEvent<Changed>(payload);
        Require(received == 4, "Unregister removes one occurrence");
        architecture.Dispose();
        architecture.Dispose();
        architecture.SendEvent<Changed>(payload);
        architecture.UnRegistEvent<Changed>(listener);
        Require(received == 4, "Disposed architecture has no notifications");
        bool rejected = false;
        try { architecture.RegistEvent<Changed>(listener); }
        catch (ObjectDisposedException) { rejected = true; }
        Require(rejected, "Disposed architecture rejects new listeners");
        using (var fresh = new Architecture())
        {
            fresh.SendEvent<Changed>(payload);
            Require(received == 4, "New architecture has no old listeners");
        }
        var duringSend = new Architecture();
        int late = 0;
        duringSend.RegistEvent<Changed>(_ => duringSend.Dispose());
        duringSend.RegistEvent<Changed>(_ => late++);
        duringSend.SendEvent<Changed>(null);
        Require(late == 0, "Disposal during notification stops remaining callbacks");
        Debug.Log("[Events] PASS: registration, payload, type isolation, unregister, duplicate behavior and disposal");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("[Events] FAIL: " + message);
    }
}
