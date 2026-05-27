using UnityEngine;
using UnityTimer;

public class SpeechBox : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private float _fadeDuration = 0.3f;

    private Timer _fadeTimer;

    public void ShowWithFade(Sprite sprite)
    {
        gameObject.SetActive(true);
        _spriteRenderer.sprite = sprite;

        Color c = _spriteRenderer.color;
        c.a = 0f;
        _spriteRenderer.color = c;

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

    public void SwapSprite(Sprite sprite)
    {
        _spriteRenderer.sprite = sprite;
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

    private void SetAlpha(float alpha)
    {
        Color c = _spriteRenderer.color;
        c.a = alpha;
        _spriteRenderer.color = c;
    }

    private void CancelFade()
    {
        if (_fadeTimer != null && !_fadeTimer.isDone)
            _fadeTimer.Cancel();

        _fadeTimer = null;
    }
}