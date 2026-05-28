using UnityEngine;
using UnityEngine.EventSystems;

public enum HotspotID
{
    None,
    Window,
    ClosetUntidy,
    ClosetTidy,
    Curtains,
    Clothes1,
    Clothes2,
    Clothes3,
    ClosetUntidy2,
    ClosetTidy2,
}


public class Hotspot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private HotspotID _hotspotID;
    [SerializeField] private IntEvent _hotspotClickedEvent;
    [SerializeField] private Sprite _alternateSprite;
    [SerializeField] private GameManager _gamemanager;
    [SerializeField] private GameObject[] _imagesToActivate;

    private Sprite _originalSprite;
    private SpriteRenderer _spriteRenderer;

    private bool _spotHit = false;

    private void Start()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalSprite = _spriteRenderer.sprite;
    }
    public void OnPointerClick(PointerEventData eventData)
    {

        if (_gamemanager.GetCurrentState() != GameState.PointAndClick || _spotHit)
            return;

        if (_hotspotClickedEvent == null)
        {
            Debug.LogWarning($"Hotspot {_hotspotID} has no IntEvent assigned.");
            return;
        }

        switch(_hotspotID)
        {
            case HotspotID.Window:
                if(_spriteRenderer.sprite == _alternateSprite)
                {
                    _spriteRenderer.sprite = null;
                }
                else
                {
                    _spriteRenderer.sprite = _alternateSprite;
                }
                _spotHit = true;
                break;
            case HotspotID.Curtains:
                _spriteRenderer.sprite = null;
                _imagesToActivate[0].SetActive(true);
                _spotHit = true;
                break;
            case HotspotID.ClosetUntidy:
                gameObject.SetActive(false);
                _imagesToActivate[0].SetActive(true);
                _spotHit = true;
                break;
            case HotspotID.ClosetTidy:
                _spriteRenderer.sprite = _alternateSprite;
                _spotHit = true;
                break;
            case HotspotID.Clothes1:
                gameObject.SetActive(false);
                if (!_imagesToActivate[0].active)
                {
                    _imagesToActivate[0].SetActive(true);
                }
                else if (!_imagesToActivate[1].active)
                {
                    _imagesToActivate[1].SetActive(true);
                }
                else if (!_imagesToActivate[2].active)
                {
                    _imagesToActivate[2].SetActive(true);
                }
                break;
            case HotspotID.Clothes2:
                gameObject.SetActive(false);
                if (!_imagesToActivate[0].active)
                {
                    _imagesToActivate[0].SetActive(true);
                }
                else if (!_imagesToActivate[1].active)
                {
                    _imagesToActivate[1].SetActive(true);
                }
                else if (!_imagesToActivate[2].active)
                {
                    _imagesToActivate[2].SetActive(true);
                }
                break;
            case HotspotID.Clothes3:
                gameObject.SetActive(false);
                if (!_imagesToActivate[0].active)
                {
                    _imagesToActivate[0].SetActive(true);
                }
                else if (!_imagesToActivate[1].active)
                {
                    _imagesToActivate[1].SetActive(true);
                }
                else if (!_imagesToActivate[2].active)
                {
                    _imagesToActivate[2].SetActive(true);
                }
                break;
            case HotspotID.ClosetUntidy2:
                gameObject.SetActive(false);
                _imagesToActivate[0].SetActive(true);
                _spotHit = true;
                break;
            case HotspotID.ClosetTidy2:
                _spriteRenderer.sprite = _alternateSprite;
                _spotHit = true;
                break;
        }
        

        Debug.Log("Hotspot Clicked " + _hotspotID);

        _hotspotClickedEvent.Raise((int)_hotspotID);
    }
}