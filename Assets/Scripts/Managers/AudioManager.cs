// ============================================================
// AudioManager.cs
//
// RESPONSIBILITY: Owns all audio channels and exposes typed
// methods for BGM, looping SFX, and one-shot SFX.
// No singleton. Injected by reference into any system that needs it.
//
// CONSUMERS: GameBootstrapper (wires it), MainMenuState, Coloring
// DEPENDS ON: Nothing — pure audio wrapper
//
// INSPECTOR: Assign all 3 AudioSource components from this
//            same GameObject into their labeled slots.
// ============================================================

using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("Audio Channels — assign each AudioSource component from this GameObject")]
    [Tooltip("BGM channel. Set Loop ON, Play On Awake ON on this AudioSource.")]
    [SerializeField] private AudioSource _bgmSource;

    [Tooltip("Looping SFX channel (e.g. drawing sound). Set Loop ON, Play On Awake OFF.")]
    [SerializeField] private AudioSource _loopSfxSource;

    [Tooltip("One-shot SFX channel (UI clicks, stings, etc.). Set Loop OFF, Play On Awake OFF.")]
    [SerializeField] private AudioSource _oneShotSfxSource;

    // ── BGM ─────────────────────────────────────────────────────────────────

    public void PlayBGM(AudioClip clip)
    {
        if (_bgmSource == null) return;
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
        _bgmSource.clip = clip;
        _bgmSource.Play();
    }

    /// <summary>
    /// WHY: FadeOutBGM uses a coroutine so the fade happens over real time without
    ///      blocking Update or requiring the caller to manage a timer.
    ///      stopAfterFade = true for game exit or scene destruction.
    ///      stopAfterFade = false if you want silence but may resume later.
    /// </summary>
    public void FadeOutBGM(float duration, bool stopAfterFade = true)
    {
        if (_bgmSource == null || !_bgmSource.isPlaying) return;
        StopCoroutine(nameof(FadeOutCoroutine)); // WHY: Prevent overlapping fades stacking
        StartCoroutine(FadeOutCoroutine(_bgmSource, duration, stopAfterFade));
    }

    public void StopBGMImmediate()
    {
        if (_bgmSource == null) return;
        StopCoroutine(nameof(FadeOutCoroutine));
        _bgmSource.Stop();
    }

    // ── Looping SFX ─────────────────────────────────────────────────────────

    public void PlayLoop(AudioClip clip)
    {
        if (_loopSfxSource == null || clip == null) return;
        // WHY: Avoid restarting the same clip mid-play if called redundantly.
        if (_loopSfxSource.clip == clip && _loopSfxSource.isPlaying) return;
        _loopSfxSource.clip = clip;
        _loopSfxSource.Play();
    }

    public void StopLoop()
    {
        if (_loopSfxSource == null || !_loopSfxSource.isPlaying) return;
        _loopSfxSource.Stop();
    }

    // ── One-shot SFX ─────────────────────────────────────────────────────────

    /// <summary>
    /// WHY: PlayOneShot allows the clip to play to completion even if another
    ///      one-shot fires before it ends. The AudioSource is the mixer point,
    ///      not the clip owner — multiple overlapping one-shots are fine on one source.
    /// </summary>
    public void PlayOneShot(AudioClip clip)
    {
        if (_oneShotSfxSource == null || clip == null) return;
        _oneShotSfxSource.PlayOneShot(clip);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private IEnumerator FadeOutCoroutine(AudioSource source, float duration, bool stopAfterFade)
    {
        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // WHY: Lerp from current volume to 0 over duration.
            //      We store startVolume at coroutine entry so a fade that starts
            //      mid-way through a previous fade doesn't jump to full volume first.
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        source.volume = 0f;
        if (stopAfterFade) source.Stop();

        // WHY: Restore volume after stopping so PlayBGM() later starts at full volume.
        //      Without this, the source is permanently silent after one fade.
        source.volume = startVolume;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_bgmSource == null)
            Debug.LogError("[AudioManager] _bgmSource not assigned.");
        if (_loopSfxSource == null)
            Debug.LogError("[AudioManager] _loopSfxSource not assigned.");
        if (_oneShotSfxSource == null)
            Debug.LogError("[AudioManager] _oneShotSfxSource not assigned.");
    }
}
