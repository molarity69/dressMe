using UnityEngine;

public class ChairRocking : MonoBehaviour
{
    [Header("Assign the two child SpriteRenderers")]
    public SpriteRenderer forwardPose;
    public SpriteRenderer backwardPose;

    [Header("Rocking Settings")]
    [Range(1f, 8f)]
    public float cycleDuration = 3f;

    public GameObject HappyPose;

    public AnimationCurve motionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // Private — we decide when to start/stop
    private bool isRocking = false;
    private float elapsedTime = 0f;

    public void Enable()
    {
        // Validate references once
        if (forwardPose == null || backwardPose == null)
        {
            Debug.LogError("Please assign both forwardPose and backwardPose SpriteRenderers!", this);
            enabled = false;
            return;
        }

        

        // Start in a neutral / backward-pose state (fully visible)
        SetAlpha(forwardPose, 1f);
        SetAlpha(backwardPose, 0f);
    }

    private void Update()
    {
        // Only run if StartRocking() has been called
        if (!isRocking) return;

        // Accumulate time so we can pause/resume cleanly
        elapsedTime += Time.deltaTime;

        float rawT = (Mathf.Sin(elapsedTime * (2f * Mathf.PI / cycleDuration)) + 1f) * 0.5f;
        float t = motionCurve.Evaluate(rawT);

        SetAlpha(forwardPose, t);
        SetAlpha(backwardPose, 1f - t);
    }

    /// <summary> Call this from any other script to start rocking. </summary>
    public void StartRocking()
    {
        if (forwardPose == null || backwardPose == null) return;
        isRocking = true;
    }

    public void StartRockingAgain()
    {
        if (forwardPose == null || backwardPose == null) return;
        isRocking = true;
        forwardPose.gameObject.SetActive(true);
        backwardPose.gameObject.SetActive(true);
        HappyPose.SetActive(false);
    }

    /// <summary> Call this to pause / stop rocking at the current pose. </summary>
    public void StopRocking()
    {
        isRocking = false;
        forwardPose.gameObject.SetActive(false);
        backwardPose.gameObject.SetActive(false);
        HappyPose.SetActive(true);
    }

    /// <summary> Call this to reset to the starting pose (backward) and stop. </summary>
    public void ResetToBackwardPose()
    {
        isRocking = false;
        elapsedTime = 0f;
        SetAlpha(forwardPose, 0f);
        SetAlpha(backwardPose, 1f);
    }

    /// <summary> Call this to set rocking to a specific progress (0.0 = backward, 0.5 = middle, 1.0 = forward). Continues rocking from there. </summary>
    public void SetRockingProgress(float normalizedTime)
    {
        elapsedTime = normalizedTime * cycleDuration;
    }

    private void SetAlpha(SpriteRenderer sr, float alpha)
    {
        if(!backwardPose.gameObject.activeSelf && backwardPose.GetComponent<SpriteRenderer>().color.a >= 0.5)
        {
            backwardPose.gameObject.SetActive(true);
        }
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }
}