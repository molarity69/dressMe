using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Vector2Listener : BaseGameEventListener<Vector2, Vector2Event, UnityVector2Event>
{
    public void InitListener(UnityAction<UnityEngine.Vector2> responseMethod, Vector2Event thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityVector2Event();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

