using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class VoidListener : BaseGameEventListener<Void, VoidEvent, UnityVoidEvent>
{

    public void InitListener(UnityAction<Void> responseMethod, VoidEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityVoidEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }

}

