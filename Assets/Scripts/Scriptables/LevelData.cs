using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Level", menuName = "Point And Click/Level")]
public class LevelData : ScriptableObject
{
    [Header("Hotspot Sequence")]
    [SerializeField] private List<HotspotID> _requiredHotspots = new List<HotspotID>();
    [SerializeField] private bool _requireOrder = true;
    [SerializeField] private GameState _targetState;

    public IReadOnlyList<HotspotID> RequiredHotspots => _requiredHotspots;
    public bool RequireOrder => _requireOrder;
    public GameState TargetState => _targetState;
}