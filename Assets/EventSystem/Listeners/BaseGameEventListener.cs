using UnityEngine;
using UnityEngine.Events;

public abstract class BaseGameEventListener <T, E, UER> : MonoBehaviour, 
    IGameEventListener<T> where E : BaseGameEvent<T> where UER : UnityEvent<T>
{
    [SerializeField] private E gameEvent;

    public E GameEvent { get { return gameEvent; } set { gameEvent = value; } }

    [SerializeField] private UER unityEventResponse;

    public UER UnityEventResponse { get { return unityEventResponse; } set { unityEventResponse = value; } }

	//private string m_debugEventName = "SwitchStateEvent";

	private void OnEnable()
	{
		if (gameEvent == null)
		{ return; }

		GameEvent.RegisterListener(this);

		//if (string.Compare(gameEvent.name, m_debugEventName) == 0)
		//{
		//	Debug.Log("Registered listener for " + gameEvent.name + " on object with name : " + this.name);
		//}
	}

	private void OnDisable()
	{
		if (gameEvent == null)
		{ return; }

		GameEvent.UnregisterListener(this);

		//if (string.Compare(gameEvent.name, m_debugEventName) == 0)
		//{
		//	Debug.Log("Un-registered listener " + gameEvent.name + " on object with name : " + this.name);
		//}
	}

	public void OnEventRaised(T item)
    {
        if(unityEventResponse != null)
        {
			//if (string.Compare(gameEvent.name, m_debugEventName) == 0)
			//{
			//	Debug.Log("Invoked response for event : " + gameEvent.name + " on object with name : " + this.name);
			//}

			unityEventResponse.Invoke(item);
        }
		else
		{
			Debug.Log("No event handler set for " + " Event name : " + gameEvent.name + " Object name : " + this.gameObject.name);
		}
    }
}
