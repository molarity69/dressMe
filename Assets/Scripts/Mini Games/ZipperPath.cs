using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ZipperPath : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private ZipperPathData _pathData;
    [SerializeField] private GameStateEvent _gameStateEvent;
    [SerializeField] private GameState _completionState = GameState.Narrative;

    [Header("References")]
    [SerializeField] private SpriteRenderer _zipperSpriteRenderer;
    [SerializeField] private Transform _handleTransform;

    [Header("Line Renderer Style")]
    [SerializeField] private float _lineWidth = 0.1f;
    [SerializeField] private Color _lineColor = Color.white;

    private LineRenderer _lineRenderer;
    private Camera _mainCamera;

    private bool _isDragging = false;
    private float _currentProgress = 0f;
    private float _lastCheckpointProgress = 0f;

    private List<float> _segmentLengths = new List<float>();
    private float _totalLength = 0f;
    private List<float> _segmentStartPercentages = new List<float>();

    public GameObject TheEnd;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _mainCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (_pathData == null)
        {
            Debug.LogWarning("ZipperPath has no ZipperPathData assigned.");
            return;
        }

        BuildPathData();
        DrawPath();
        ResetToStart();
    }

    private void BuildPathData()
    {
        _segmentLengths.Clear();
        _segmentStartPercentages.Clear();
        _totalLength = 0f;

        IReadOnlyList<Vector2> waypoints = _pathData.Waypoints;

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            float length = Vector2.Distance(waypoints[i], waypoints[i + 1]);
            _segmentLengths.Add(length);
            _totalLength += length;
        }

        float accumulated = 0f;
        for (int i = 0; i < _segmentLengths.Count; i++)
        {
            _segmentStartPercentages.Add(accumulated / _totalLength);
            accumulated += _segmentLengths[i];
        }
    }

    private void DrawPath()
    {
        IReadOnlyList<Vector2> waypoints = _pathData.Waypoints;

        _lineRenderer.positionCount = waypoints.Count;
        _lineRenderer.startWidth = _lineWidth;
        _lineRenderer.endWidth = _lineWidth;
        _lineRenderer.startColor = _lineColor;
        _lineRenderer.endColor = _lineColor;
        _lineRenderer.useWorldSpace = true;

        for (int i = 0; i < waypoints.Count; i++)
        {
            Vector3 worldPos = transform.TransformPoint(new Vector3(waypoints[i].x, waypoints[i].y, 0f));
            _lineRenderer.SetPosition(i, worldPos);
        }
    }

    private void Update()
    {
        if (_pathData == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            TryBeginDrag();
        }

        if (Input.GetMouseButton(0) && _isDragging)
        {
            ContinueDrag();
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (_isDragging)
            {
                _isDragging = false;
                HandleFailure();
            }
        }
    }

    private Vector2 ScreenToLocal(Vector3 screenPosition)
    {
        Vector3 worldPosition = _mainCamera.ScreenToWorldPoint(screenPosition);
        return transform.InverseTransformPoint(worldPosition);
    }

    private void TryBeginDrag()
    {
        Vector2 mouseLocal = ScreenToLocal(Input.mousePosition);
        Vector2 startPoint = _pathData.Waypoints[0];

        if (Vector2.Distance(mouseLocal, startPoint) <= _pathData.Tolerance)
        {
            _isDragging = true;
        }
    }

    private void ContinueDrag()
    {
        Vector2 mouseLocal = ScreenToLocal(Input.mousePosition);
        Vector2 closestPoint = GetClosestPointOnPath(mouseLocal, out float progressAtClosest);

        float distanceFromPath = Vector2.Distance(mouseLocal, closestPoint);

        if (distanceFromPath > _pathData.Tolerance)
        {
            _isDragging = false;
            HandleFailure();
            return;
        }

        if (progressAtClosest > _currentProgress)
        {
            _currentProgress = progressAtClosest;
            UpdateCheckpoint(_currentProgress);
            UpdateHandle(_currentProgress);
            UpdateZipperSprite(_currentProgress);

            if (_currentProgress >= 1f)
            {
                OnPathCompleted();
            }
        }
    }

    private int GetCurrentSegmentIndex()
    {
        for (int i = _segmentStartPercentages.Count - 1; i >= 0; i--)
        {
            if (_currentProgress >= _segmentStartPercentages[i])
                return i;
        }
        return 0;
    }

    private Vector2 GetClosestPointOnPath(Vector2 localPoint, out float progress)
    {
        IReadOnlyList<Vector2> waypoints = _pathData.Waypoints;

        Vector2 closestPoint = waypoints[0];
        float closestDistance = float.MaxValue;
        float closestProgress = 0f;

        int startSegment = GetCurrentSegmentIndex();

        for (int i = startSegment; i < waypoints.Count - 1; i++)
        {
            Vector2 segmentStart = waypoints[i];
            Vector2 segmentEnd = waypoints[i + 1];

            Vector2 pointOnSegment = GetClosestPointOnSegment(localPoint, segmentStart, segmentEnd);
            float distance = Vector2.Distance(localPoint, pointOnSegment);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPoint = pointOnSegment;

                float segmentProgress = Vector2.Distance(segmentStart, pointOnSegment) / _segmentLengths[i];
                float segmentContribution = _segmentLengths[i] / _totalLength;
                closestProgress = _segmentStartPercentages[i] + segmentProgress * segmentContribution;
            }
        }

        progress = Mathf.Clamp01(closestProgress);
        return closestPoint;
    }

    private Vector2 GetClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float segmentLengthSq = segment.sqrMagnitude;

        if (segmentLengthSq == 0f) return start;

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSq);
        return start + t * segment;
    }

    private void UpdateCheckpoint(float progress)
    {
        IReadOnlyList<float> checkpoints = _pathData.CheckpointPercentages;

        for (int i = checkpoints.Count - 1; i >= 0; i--)
        {
            if (progress >= checkpoints[i] && checkpoints[i] > _lastCheckpointProgress)
            {
                _lastCheckpointProgress = checkpoints[i];
                break;
            }
        }
    }

    private void UpdateHandle(float progress)
    {
        if (_handleTransform == null) return;

        Vector2 localPos = GetPositionAtProgress(progress);
        _handleTransform.position = transform.TransformPoint(new Vector3(localPos.x, localPos.y, 0f));
    }

    private void UpdateZipperSprite(float progress)
    {
        if (_zipperSpriteRenderer == null) return;

        Sprite sprite = _pathData.GetSpriteForProgress(progress);
        if (sprite != null)
            _zipperSpriteRenderer.sprite = sprite;
    }

    private Vector2 GetPositionAtProgress(float progress)
    {
        IReadOnlyList<Vector2> waypoints = _pathData.Waypoints;
        float targetLength = progress * _totalLength;
        float accumulated = 0f;

        for (int i = 0; i < _segmentLengths.Count; i++)
        {
            if (accumulated + _segmentLengths[i] >= targetLength)
            {
                float t = (targetLength - accumulated) / _segmentLengths[i];
                return Vector2.Lerp(waypoints[i], waypoints[i + 1], t);
            }
            accumulated += _segmentLengths[i];
        }

        return waypoints[waypoints.Count - 1];
    }

    private void HandleFailure()
    {
        switch (_pathData.FailureMode)
        {
            case FailureMode.ReturnToStart:
                ResetToStart();
                break;
            case FailureMode.ReturnToLastCheckpoint:
                ReturnToCheckpoint();
                break;
        }
    }

    private void ResetToStart()
    {
        _currentProgress = 0f;
        _lastCheckpointProgress = 0f;
        UpdateHandle(0f);
        UpdateZipperSprite(0f);
    }

    private void ReturnToCheckpoint()
    {
        _currentProgress = _lastCheckpointProgress;
        UpdateHandle(_currentProgress);
        UpdateZipperSprite(_currentProgress);
    }

    private void OnPathCompleted()
    {
        _isDragging = false;

        if (_gameStateEvent == null)
        {
            Debug.LogWarning("ZipperPath has no GameStateEvent assigned.");
            return;
        }

        Debug.Log("Path end!");

        TheEnd.SetActive(true);

        _gameStateEvent.Raise(new GameStateData(_completionState, GameState.Narrative));
    }
}