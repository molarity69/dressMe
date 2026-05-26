using UnityEngine;
using UnityEngine.Events;

public class GameStateListener : BaseGameEventListener<GameStateData, GameStateEvent, UnityGameStateEvent>
{
    public void InitListener(UnityAction<GameStateData> responseMethod, GameStateEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityGameStateEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}