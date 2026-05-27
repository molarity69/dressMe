using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MainMenuAnimator : MonoBehaviour
{
    [Header("Duration")]
    [SerializeField] private float _duration = 3f;

    [Header("Menu Fade")]
    [SerializeField] private CanvasGroup _menuCanvasGroup;

    [Header("Background")]
    [SerializeField] private Transform _backgroundTransform;
    [SerializeField] private Vector3 _backgroundTargetScale = Vector3.one;
    [SerializeField] private Vector3 _backgroundTargetPosition = Vector3.zero;

    [Header("Sprites")]
    [SerializeField] private List<SpriteRenderer> _spriteRenderers = new List<SpriteRenderer>();
    [SerializeField] private Color _spriteTargetColor = Color.white;

    private UnityAction _onComplete;
    private float _elapsed = 0f;
    private bool _isPlaying = false;

    private Vector3 _backgroundStartPosition;
    private Vector3 _backgroundStartScale;
    private float _menuStartAlpha;
    private List<Color> _spriteStartColors = new List<Color>();

    public void Play(UnityAction onComplete)
    {
        if (_menuCanvasGroup == null && _backgroundTransform == null && _spriteRenderers.Count == 0)
        {
            Debug.LogWarning("MainMenuAnimator: No references assigned.");
            onComplete?.Invoke();
            return;
        }

        _onComplete = onComplete;
        _elapsed = 0f;
        _isPlaying = true;

        CaptureStartValues();

        if (_menuCanvasGroup != null)
        {
            _menuCanvasGroup.interactable = false;
            _menuCanvasGroup.blocksRaycasts = false;
        }
    }

    private void CaptureStartValues()
    {
        if (_backgroundTransform != null)
        {
            _backgroundStartPosition = _backgroundTransform.position;
            _backgroundStartScale = _backgroundTransform.localScale;
        }

        if (_menuCanvasGroup != null)
            _menuStartAlpha = _menuCanvasGroup.alpha;

        _spriteStartColors.Clear();
        foreach (SpriteRenderer sr in _spriteRenderers)
        {
            if (sr != null)
                _spriteStartColors.Add(sr.color);
        }
    }

    private void Update()
    {
        if (!_isPlaying) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        float smoothT = Mathf.SmoothStep(0f, 1f, t);

        AnimateMenu(smoothT);
        AnimateBackground(smoothT);
        AnimateSprites(smoothT);

        if (t >= 1f)
        {
            _isPlaying = false;
            _onComplete?.Invoke();
        }
    }

    private void AnimateMenu(float t)
    {
        if (_menuCanvasGroup == null) return;
        _menuCanvasGroup.alpha = Mathf.Lerp(_menuStartAlpha, 0f, t);
    }

    private void AnimateBackground(float t)
    {
        if (_backgroundTransform == null) return;

        _backgroundTransform.position = Vector3.Lerp(_backgroundStartPosition, _backgroundTargetPosition, t);
        _backgroundTransform.localScale = Vector3.Lerp(_backgroundStartScale, _backgroundTargetScale, t);
    }

    private void AnimateSprites(float t)
    {
        for (int i = 0; i < _spriteRenderers.Count; i++)
        {
            if (_spriteRenderers[i] == null) continue;
            _spriteRenderers[i].color = Color.Lerp(_spriteStartColors[i], _spriteTargetColor, t);
        }
    }
}