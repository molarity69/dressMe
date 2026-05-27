using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Zipper Path", menuName = "Point And Click/Zipper Path")]
public class ZipperPathData : ScriptableObject
{
    [Header("Path")]
    [SerializeField] private List<Vector2> _waypoints = new List<Vector2>();
    [SerializeField] private float _tolerance = 0.5f;

    [Header("Checkpoints")]
    [Tooltip("Values between 0 and 1 representing how far along the path each checkpoint is.")]
    [SerializeField] private List<float> _checkpointPercentages = new List<float>();

    [Header("Failure")]
    [SerializeField] private FailureMode _failureMode = FailureMode.ReturnToStart;

    [Header("Zipper Sprites")]
    [Tooltip("Add sprites in order from least zipped to fully zipped. Thresholds are calculated automatically.")]
    [SerializeField] private List<Sprite> _zipperSprites = new List<Sprite>();

    public IReadOnlyList<Vector2> Waypoints => _waypoints;
    public float Tolerance => _tolerance;
    public IReadOnlyList<float> CheckpointPercentages => _checkpointPercentages;
    public FailureMode FailureMode => _failureMode;
    public IReadOnlyList<Sprite> ZipperSprites => _zipperSprites;

    public Sprite GetSpriteForProgress(float progress)
    {
        if (_zipperSprites == null || _zipperSprites.Count == 0)
            return null;

        int index = Mathf.FloorToInt(progress * _zipperSprites.Count);
        index = Mathf.Clamp(index, 0, _zipperSprites.Count - 1);
        return _zipperSprites[index];
    }
}

public enum FailureMode
{
    ReturnToStart,
    ReturnToLastCheckpoint
}

