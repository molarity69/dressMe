using UnityEngine;
using ContainmentProtocol.Game.Events;

[CreateAssetMenu(fileName = "New Game State Event",
    menuName = "Containment Protocol/Events/Game State Event")]
public class GameStateEvent : BaseGameEvent<GameStateData> { }