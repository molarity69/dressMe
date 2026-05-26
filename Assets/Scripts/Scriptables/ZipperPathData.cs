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
    [SerializeField] private List<ZipperSpriteThreshold> _zipperSprites = new List<ZipperSpriteThreshold>();

    public IReadOnlyList<Vector2> Waypoints => _waypoints;
    public float Tolerance => _tolerance;
    public IReadOnlyList<float> CheckpointPercentages => _checkpointPercentages;
    public FailureMode FailureMode => _failureMode;
    public IReadOnlyList<ZipperSpriteThreshold> ZipperSprites => _zipperSprites;
}

public enum FailureMode
{
    ReturnToStart,
    ReturnToLastCheckpoint
}

[System.Serializable]
public class ZipperSpriteThreshold
{
    [Tooltip("Progress value between 0 and 1 at which this sprite becomes active.")]
    public float Threshold;
    public Sprite Sprite;
}