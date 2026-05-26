using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameObjectListener : BaseGameEventListener<GameObject, GameObjectEvent, UnityGameObjectEvent>
{
    public void InitListener(UnityAction<GameObject> responseMethod, GameObjectEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityGameObjectEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

