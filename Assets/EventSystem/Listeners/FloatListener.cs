using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class FloatListener : BaseGameEventListener<float, FloatEvent, UnityFloatEvent>
{
    public void InitListener(UnityAction<float> responseMethod, FloatEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityFloatEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

