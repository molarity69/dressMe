using UnityEngine;
using UnityEngine.EventSystems;

public enum HotspotID
{
    None = 0,
    HotspotA = 1,
    HotspotB = 2,
    HotspotC = 3,
    HotspotD = 4,
    HotspotE = 5
}


public class Hotspot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private HotspotID _hotspotID;
    [SerializeField] private IntEvent _hotspotClickedEvent;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_hotspotClickedEvent == null)
        {
            Debug.LogWarning($"Hotspot {_hotspotID} has no IntEvent assigned.");
            return;
        }

        Debug.Log("Hotspot Clicked " + _hotspotID);

        _hotspotClickedEvent.Raise((int)_hotspotID);
    }
}