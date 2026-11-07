using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class HitEffectReplayVerification
{
    [MenuItem("Tools/RPG/Verify Hit Effect Replay (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying) return;
        var root = new GameObject("HitEffectReplayVerification");
        try
        {
            foreach (var name in new[] { "HitFX_00", "HitFX_Critical" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/" + name + ".prefab");
                var clone = UnityEngine.Object.Instantiate(prefab, root.transform);
                var fx = clone.GetComponent<HitEffectFX>();
                var animator = clone.GetComponent<Animator>();
                var renderer = clone.GetComponent<SpriteRenderer>();
                var scale = clone.transform.localScale;
                var color = renderer.color;
                Require(clone.GetComponentsInChildren<ParticleSystem>(true).Length == 0, name + " has no particles");
                Require(!fx.IsPlaying, "Adding replay component does not auto-start lifetime");
                fx.Play(Vector3.zero, Quaternion.identity);
                int state = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                animator.Update(0.3f);
                animator.speed = 0f;
                renderer.color = Color.clear;
                clone.transform.localScale = Vector3.one * 4;
                var position = new Vector3(2, 3, 0);
                var rotation = Quaternion.Euler(0, 180, 25);
                fx.Play(position, rotation);
                Require(clone.transform.position == position && Quaternion.Angle(clone.transform.rotation, rotation) < 0.01f, "Reset absolute pose");
                Require(clone.transform.localScale == scale && renderer.color == color, "Reset presentation");
                var info = animator.GetCurrentAnimatorStateInfo(0);
                Require(info.fullPathHash == state && Mathf.Abs(info.normalizedTime) < 0.001f && animator.speed == 1f, "Restart animator from beginning");
                var timer = typeof(HitEffectFX).GetField("remainingTime", BindingFlags.NonPublic | BindingFlags.Instance);
                Require((float)timer.GetValue(fx) == 0.5f && fx.IsPlaying, "Reset half-second lifetime");
                timer.SetValue(fx, 0f);
                typeof(HitEffectFX).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(fx, null);
                Require(!clone.activeSelf && !fx.IsPlaying, "Expiration hides standalone effect");
                fx.Play(position, rotation);
                Require(clone.activeSelf && fx.IsPlaying && (float)timer.GetValue(fx) == 0.5f, "Replay after expiration");
            }
            Debug.Log("[HitEffectReplay] PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[HitEffectReplay] " + message);
    }
}
