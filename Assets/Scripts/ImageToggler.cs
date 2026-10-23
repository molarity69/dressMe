using UnityEngine;
using UnityEngine.UI;

public class ImageToggler : MonoBehaviour
{
    // Drag your UI Image into this slot in the Inspector
    public GameObject uiImage;

    public void ToggleImage()
    {
        if (uiImage != null)
        {
            // Sets it to the opposite of its current state
            bool isActive = uiImage.activeSelf;
            uiImage.SetActive(!isActive);
        }
    }
}