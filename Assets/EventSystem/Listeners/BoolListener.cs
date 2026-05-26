using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class BoolListener : BaseGameEventListener<bool, BoolEvent, UnityBoolEvent>
{
    public void InitListener(UnityAction<bool> responseMethod, BoolEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityBoolEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

