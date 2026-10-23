using TMPro;
using UnityEngine;
using UnityTimer;

public class SpeechBox : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _spriteRenderer;
    [Tooltip("Optional. A TextMeshPro (3D) child used for live text on an empty bubble.")]
    [SerializeField] private TMP_Text _text;
    [SerializeField] private float _fadeDuration = 0.3f;

    private Timer _fadeTimer;

    public bool IsVisible => gameObject.activeSelf;

    // Sprite-only bubble (text baked into the image). Clears any live text.
    public void ShowWithFade(Sprite sprite)
    {
        SetText(string.Empty);
        ShowSpriteWithFade(sprite);
    }

    // Empty bubble sprite with live text on top.
    public void ShowTextWithFade(Sprite bubble, string text)
    {
        SetText(text);
        ShowSpriteWithFade(bubble);
    }

    public void SwapSprite(Sprite sprite)
    {
        _spriteRenderer.sprite = sprite;
        SetText(string.Empty);
    }

    public void SetText(string text)
    {
        if (_text != null)
            _text.text = text;
    }

    public void HideWithFade(System.Action onComplete = null)
    {
        CancelFade();

        float startAlpha = _spriteRenderer.color.a;
        float duration = _fadeDuration;

        _fadeTimer = Timer.Register(
            duration,
            onComplete: () =>
            {
                SetAlpha(0f);
                gameObject.SetActive(false);
                onComplete?.Invoke();
            },
            onUpdate: elapsed => SetAlpha(Mathf.Lerp(startAlpha, 0f, elapsed / duration)),
            isLooped: false,
            useRealTime: false
        );
    }

    public void HideImmediate()
    {
        CancelFade();
        SetAlpha(0f);
        gameObject.SetActive(false);
    }

    private void ShowSpriteWithFade(Sprite sprite)
    {
        gameObject.SetActive(true);
        _spriteRenderer.sprite = sprite;

        SetAlpha(0f);
        CancelFade();

        float duration = _fadeDuration;
        _fadeTimer = Timer.Register(
            duration,
            onComplete: () => SetAlpha(1f),
            onUpdate: elapsed => SetAlpha(elapsed / duration),
            isLooped: false,
            useRealTime: false
        );
    }

    private void SetAlpha(float alpha)
    {
        Color color = _spriteRenderer.color;
        color.a = alpha;
        _spriteRenderer.color = color;

        if (_text != null)
            _text.alpha = alpha;
    }

    private void CancelFade()
    {
        if (_fadeTimer != null && !_fadeTimer.isDone)
            _fadeTimer.Cancel();

        _fadeTimer = null;
    }
}
