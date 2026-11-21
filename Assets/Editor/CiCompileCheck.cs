using System;
using UnityEditor;
using UnityEngine;

namespace Rpg.Editor
{
    public static class CiCompileCheck
    {
        public static void Run()
        {
            Debug.Log("[CI] Unity script compilation completed.");
        }

        public static void RunPrimaryAttackVerification()
        {
            Run();
            try
            {
                PrimaryAttackVerification.Verify();
                Debug.Log("[CI] Primary attack verification completed.");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
