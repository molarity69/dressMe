using UnityEngine;
using UnityEngine.EventSystems;

public enum HotspotID
{
    None,
    Window,
    HotspotB = 2,
    HotspotC = 3,
    HotspotD = 4,
    HotspotE = 5
}


public class Hotspot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private HotspotID _hotspotID;
    [SerializeField] private IntEvent _hotspotClickedEvent;
    [SerializeField] private Sprite _alternateSprite;

    private Sprite _originalSprite;
    private SpriteRenderer _spriteRenderer;
    private void Start()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalSprite = _spriteRenderer.sprite;
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_hotspotClickedEvent == null)
        {
            Debug.LogWarning($"Hotspot {_hotspotID} has no IntEvent assigned.");
            return;
        }

        switch(_hotspotID)
        {
            case HotspotID.Window:
                if(_spriteRenderer.sprite == _originalSprite)
                {
                    _spriteRenderer.sprite = null;
                }
                else
                {
                    _spriteRenderer.sprite = _originalSprite;
                }
                break;
        }
        

        Debug.Log("Hotspot Clicked " + _hotspotID);

        _hotspotClickedEvent.Raise((int)_hotspotID);
    }
}