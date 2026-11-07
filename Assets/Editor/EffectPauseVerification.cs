using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class EffectPauseVerification
{
    private static bool running;
    private sealed class Snapshot
    {
        internal PooledTemporaryEffect instance;
        internal Vector3 position;
        internal Color color;
        internal float timer;
        internal float animationTime;
        internal Snapshot(PooledTemporaryEffect value)
        {
            instance = value;
            position = value.transform.position;
            color = ColorOf(value);
            timer = TimerOf(value);
            var animator = value.GetComponent<Animator>();
            animationTime = animator != null ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0;
        }
    }

    [MenuItem("Tools/RPG/Verify Effect Dialogue Pause (Play Mode)")]
    public static async void Verify()
    {
        if (running || !Application.isPlaying || GameManager.instance == null || Time.timeScale != 1f)
        {
            Debug.LogWarning("[EffectPause] 请在 MainScene 未暂停的 Play 中运行，且不要重复执行。");
            return;
        }
        running = true;
        var root = new GameObject("EffectPauseVerification");
        var pool = root.AddComponent<TemporaryEffectPool>();
        DialogueView view = null;
        var asset = ScriptableObject.CreateInstance<DialogueAsset>();
        int locksBefore = GameplayControlLock.HolderCount;
        try
        {
            var instances = new List<PooledTemporaryEffect>();
            foreach (string name in new[] { "PopUpText", "HitFX_00", "HitFX_Critical", "AfterImageFX" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/" + name + ".prefab");
                var instance = pool.Take(prefab, Vector3.zero, Quaternion.identity);
                if (instance.TryGetComponent<PopUpTextFX>(out var text)) text.Play("Pause baseline");
                else if (instance.TryGetComponent<HitEffectFX>(out var hit)) hit.Play(Vector3.zero, Quaternion.identity);
                else instance.GetComponent<AfterImageFx>().Play(1.5f, instance.GetComponent<SpriteRenderer>().sprite, Vector3.zero, Quaternion.identity);
                instances.Add(instance);
            }
            await Wait(0.08f, false);
            var dialoguePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Dialogue/DialogueBox.prefab");
            view = UnityEngine.Object.Instantiate(dialoguePrefab, root.transform).GetComponent<DialogueView>();
            asset.nodes = new List<DialogueNode> { new DialogueNode { id = "start", content = "Effect pause verification" } };
            view.StartDialogue(asset, "Verification");
            Require(view.IsDialogueRunning && Time.timeScale == 0, "Real DialogueView pauses game time");
            await Task.Yield();
            await Task.Yield(); // Capture after the frame in which dialogue requested pause.
            var snapshots = instances.ConvertAll(item => new Snapshot(item));
            view.Bind(view.Runner); // Existing-session binding must not replay effects.
            await Wait(0.75f, true); // Longer than normal hit lifetime, while scaled time is frozen.
            foreach (var snapshot in snapshots)
            {
                var item = snapshot.instance;
                Require(item != null && item.IsInUse && item.gameObject.activeSelf && item.OwnerPool.IdleCount == 0, "Pause cannot return a playing effect");
                Require(item.transform.position == snapshot.position && ColorOf(item) == snapshot.color, "Movement and fade freeze");
                Require(Mathf.Approximately(TimerOf(item), snapshot.timer), "Game-time timer freezes");
                var animator = item.GetComponent<Animator>();
                if (animator != null)
                {
                    Require(animator.updateMode == AnimatorUpdateMode.Normal, "Animator uses scaled time");
                    Require(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime - snapshot.animationTime) < 0.001f, "Animation freezes");
                }
            }
            view.Close();
            Require(Time.timeScale == 1 && GameplayControlLock.HolderCount == locksBefore, "Dialogue releases only its own pause and lock");
            await Wait(0.08f, false);
            foreach (var snapshot in snapshots)
            {
                var item = snapshot.instance;
                Require(item.IsInUse && item.gameObject.activeSelf, "Resume continues the existing effect");
                if (item.GetComponent<AfterImageFx>() != null)
                    Require(ColorOf(item).a < snapshot.color.a, "Afterimage fade resumes");
                else Require(TimerOf(item) < snapshot.timer, "Timer continues rather than restarting");
                var animator = item.GetComponent<Animator>();
                if (animator != null) Require(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > snapshot.animationTime, "Animation continues from its paused position");
            }
            await Wait(2f, false);
            foreach (var item in instances)
                Require(!item.IsInUse && !item.gameObject.activeSelf && item.OwnerPool.IdleCount == 1, "Each playback eventually returns exactly once");
            Require(pool.GetComponentsInChildren<PooledTemporaryEffect>(true).Length == 4, "Pause and resume create no additional playbacks");
            Debug.Log("[EffectPause] PASS");
        }
        catch (Exception error) { Debug.LogException(error); }
        finally
        {
            if (view != null) view.Close();
            if (pool != null) pool.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root);
            if (asset != null) UnityEngine.Object.Destroy(asset);
            running = false;
        }
    }
    private static Color ColorOf(PooledTemporaryEffect item)
    {
        var text = item.GetComponent<TextMeshPro>();
        return text != null ? text.color : item.GetComponent<SpriteRenderer>().color;
    }
    private static float TimerOf(PooledTemporaryEffect item)
    {
        if (item.TryGetComponent<PopUpTextFX>(out var text)) return FloatField(text, "textTimer");
        if (item.TryGetComponent<HitEffectFX>(out var hit)) return FloatField(hit, "remainingTime");
        return 0;
    }
    private static float FloatField(object target, string name) => (float)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static async Task Wait(float duration, bool realtime)
    {
        float start = realtime ? Time.realtimeSinceStartup : Time.time;
        float deadline = Time.realtimeSinceStartup + 10;
        do
        {
            await Task.Yield();
            if (!Application.isPlaying || Time.realtimeSinceStartup > deadline)
                throw new InvalidOperationException("Play stopped or pause verification timed out.");
        } while ((realtime ? Time.realtimeSinceStartup : Time.time) - start < duration);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[EffectPause] " + message);
    }
}
