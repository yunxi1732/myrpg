using System;
using UnityEditor;
using UnityEngine;

public static class InventoryRegressionVerification
{
    [MenuItem("Tools/RPG/Verify Migration Regression %&r")]
    public static void Verify()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode for lifecycle regression.");
        IOCContainerVerification.Verify();
        ArchitectureVerification.Verify();
        EventVerification.Verify();
        InventorySystemVerification.Verify();
        InventorySaveVerification.Verify();
        var root = new GameObject("Temporary inventory lifecycle verification");
        var model = new InventoryModel();
        using (var architecture = new Architecture())
        {
            InventoryView view = null;
            try
            {
                view = new InventoryView(root.transform, root.transform, root.transform, architecture, model);
                for (int i = 0; i < 20; i++)
                {
                    root.SetActive(false);
                    int before = view.NotificationCount;
                    architecture.SendEvent<InventoryChanged>(model);
                    Require(view.NotificationCount == before, "Hidden view must unsubscribe");
                    root.SetActive(true);
                    architecture.SendEvent<InventoryChanged>(model);
                    Require(view.NotificationCount == before + 1, "Enabled view must receive exactly once");
                }
                int count = view.NotificationCount;
                view.Dispose();
                view.Dispose();
                architecture.SendEvent<InventoryChanged>(model);
                Require(view.NotificationCount == count, "Disposed view must unsubscribe");
                architecture.Dispose();
                architecture.SendEvent<InventoryChanged>(model);
                Require(view.NotificationCount == count, "Disposed architecture must not notify");
            }
            finally { view?.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }
        Debug.Log("[MigrationRegression] PASS: automated checks including 20 lifecycle cycles. Actual game regression remains separate.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("[MigrationRegression] FAIL: " + message);
    }
}

