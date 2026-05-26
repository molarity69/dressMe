using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Hotspot Sequence", menuName = "Point And Click/Hotspot Sequence")]
public class HotspotSequenceData : ScriptableObject
{
    [SerializeField] private List<HotspotID> _requiredHotspots = new List<HotspotID>();
    [SerializeField] private bool _requireOrder = true;
    [SerializeField] private GameState _targetState;

    public IReadOnlyList<HotspotID> RequiredHotspots => _requiredHotspots;
    public bool RequireOrder => _requireOrder;
    public GameState TargetState => _targetState;
}