using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class StringVectorListener : BaseGameEventListener<StringVectorStruct, StringVectorEvent, UnityStringVectorEvent>
{
    public void InitListener(UnityAction<StringVectorStruct> responseMethod, StringVectorEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityStringVectorEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

