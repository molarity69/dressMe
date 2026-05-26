using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class IntListener : BaseGameEventListener<int, IntEvent, UnityIntEvent>
{
    public void InitListener(UnityAction<int> responseMethod, IntEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityIntEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

