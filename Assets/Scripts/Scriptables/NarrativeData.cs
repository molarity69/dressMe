using System;
using System.Collections.Generic;
using UnityEngine;
public enum NarrativeCharacter
{
    MrsSula,
    Player,
    None
}

[CreateAssetMenu(fileName = "New Narrative", menuName = "Narrative/Narrative Data")]
public class NarrativeData : ScriptableObject
{
    [SerializeField] private List<NarrativeLine> _lines = new List<NarrativeLine>();
    public IReadOnlyList<NarrativeLine> Lines => _lines;
}

[Serializable]
public class NarrativeLine
{
    public NarrativeCharacter Character;
    public Sprite SpeechSprite;
    public bool HasTransition;
    public GameState TransitionState;
}