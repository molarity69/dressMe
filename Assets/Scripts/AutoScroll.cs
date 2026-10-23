using UnityEngine;
using UnityEngine.UI;

public class AutoScroll : MonoBehaviour
{
    public ScrollRect scrollRect;

    void LateUpdate()
    {
        // Always force scroll to bottom
        scrollRect.verticalNormalizedPosition = 0f;
    }
}
