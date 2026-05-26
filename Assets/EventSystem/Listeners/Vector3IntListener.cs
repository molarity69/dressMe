using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Vector3IntListener : BaseGameEventListener<Vector3IntStruct, Vector3IntEvent, UnityVector3IntEvent>
{
    public void InitListener(UnityAction<Vector3IntStruct> responseMethod, Vector3IntEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityVector3IntEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

