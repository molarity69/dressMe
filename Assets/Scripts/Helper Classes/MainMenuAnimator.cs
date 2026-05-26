using UnityEngine;
using UnityEngine.Events;

public class MainMenuAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _backgroundTransform;
    [SerializeField] private CanvasGroup _menuCanvasGroup;

    [Header("Settings")]
    [SerializeField] private float _duration = 3f;
    [SerializeField] private float _backgroundScaleMultiplier = 1.5f;

    private UnityAction _onComplete;
    private float _elapsed = 0f;
    private bool _isPlaying = false;
    private Vector3 _initialScale;

    public void Play(UnityAction onComplete)
    {
        if (_backgroundTransform == null || _menuCanvasGroup == null)
        {
            Debug.LogWarning("MainMenuAnimator: Missing references.");
            onComplete?.Invoke();
            return;
        }

        _onComplete = onComplete;
        _elapsed = 0f;
        _isPlaying = true;
        _initialScale = _backgroundTransform.localScale;

        _menuCanvasGroup.interactable = false;
        _menuCanvasGroup.blocksRaycasts = false;
    }

    private void Update()
    {
        if (!_isPlaying) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        float smoothT = Mathf.SmoothStep(0f, 1f, t);

        _backgroundTransform.localScale = Vector3.Lerp(
            _initialScale,
            _initialScale * _backgroundScaleMultiplier,
            smoothT
        );

        _menuCanvasGroup.alpha = Mathf.Lerp(1f, 0f, smoothT);

        if (t >= 1f)
        {
            _isPlaying = false;
            _onComplete?.Invoke();
        }
    }
}