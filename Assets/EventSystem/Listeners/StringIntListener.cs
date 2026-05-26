using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class StringIntListener : BaseGameEventListener<StringIntStruct, StringIntEvent, UnityStringIntEvent>
{
    public void InitListener(UnityAction<StringIntStruct> responseMethod, StringIntEvent thisEvent)
    {
        GameEvent = thisEvent;
        UnityEventResponse = new UnityStringIntEvent();
        UnityEventResponse.AddListener(responseMethod);

        if (GameEvent == null) { return; }

        GameEvent.RegisterListener(this);
    }
}

