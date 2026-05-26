using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Vector3Listener : BaseGameEventListener<Vector3, Vector3Event, UnityVector3Event>
{
    public void InitListener(UnityAction<UnityEngine.Vector3> responseMethod, Vector3Event thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityVector3Event();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

