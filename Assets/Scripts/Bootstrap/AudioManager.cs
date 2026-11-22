using System;
using UnityEngine;

// Audio sources belong to a prefab instantiated by the built-in MainMenu.
// The game assembly provides its world-distance policy after loading.
public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;
    public static Func<Transform, float, bool> IsSourceAudible;
    private static bool warnedMissing;

    [SerializeField] private float sfxMinDistance;
    [SerializeField] private AudioSource[] sfx;
    [SerializeField] private AudioSource[] bgm;

    public bool playBgm;
    private int bgmIndex;
    private bool canPlaySFX;

    private void Awake()
    {
        if (instance != null && instance != this) Destroy(instance.gameObject);
        instance = this;
        warnedMissing = false;
        Invoke("AllowSFX", 1f);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(instance, this)) instance = null;
    }

    private void Update()
    {
        if (!playBgm) StopAllBGM();
        else if (bgm != null && bgmIndex >= 0 && bgmIndex < bgm.Length &&
                 bgm[bgmIndex] != null && !bgm[bgmIndex].isPlaying) PlayBGM(bgmIndex);
    }

    public static void TryPlaySFX(int index, Transform source)
    {
        AudioManager manager = instance;
        if (manager != null) manager.PlaySFX(index, source);
        else WarnMissing();
    }

    public static void TryStopSFX(int index)
    {
        AudioManager manager = instance;
        if (manager != null) manager.StopSFX(index);
        else WarnMissing();
    }

    private static void WarnMissing()
    {
        if (warnedMissing) return;
        warnedMissing = true;
        Debug.LogWarning("[Audio] No active AudioManager; sound requests are skipped.");
    }

    public void PlaySFX(int index, Transform source)
    {
        if (!canPlaySFX) return;
        if (source != null && IsSourceAudible != null && !IsSourceAudible(source, sfxMinDistance)) return;
        if (sfx == null || index < 0 || index >= sfx.Length || sfx[index] == null) return;
        sfx[index].pitch = UnityEngine.Random.Range(0.9f, 1.1f);
        sfx[index].Play();
    }

    public void StopSFX(int index)
    {
        if (sfx != null && index >= 0 && index < sfx.Length && sfx[index] != null)
            sfx[index].Stop();
    }
    public void PlayRandomBGM()
    {
        if (bgm == null || bgm.Length == 0) return;
        bgmIndex = UnityEngine.Random.Range(0, bgm.Length);
        PlayBGM(bgmIndex);
    }

    public void PlayBGM(int index)
    {
        if (bgm == null || index < 0 || index >= bgm.Length || bgm[index] == null) return;
        bgmIndex = index;
        StopAllBGM();
        bgm[index].Play();
    }

    public void StopAllBGM()
    {
        if (bgm == null) return;
        for (int i = 0; i < bgm.Length; i++)
            if (bgm[i] != null) bgm[i].Stop();
    }

    private void AllowSFX() => canPlaySFX = true;
}
