using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class StringListener : BaseGameEventListener<string, StringEvent, UnityStringEvent>
{
    public void InitListener(UnityAction<string> responseMethod, StringEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityStringEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

