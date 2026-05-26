using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameObjectVector3Listener : BaseGameEventListener<GameObjectVector3, GameObjectVector3Event, UnityGameObjectVector3Event>
{
    public void InitListener(UnityAction<GameObjectVector3> responseMethod, GameObjectVector3Event thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityGameObjectVector3Event();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

