using UnityEngine;

public struct GameObjectVector3
{
    public GameObject gameobject;
    public Vector3 vector3;
}

[CreateAssetMenu(fileName = "New Game Object Vector3 Event", menuName = "Game Events/ Game Object Vector3 Event")]
public class GameObjectVector3Event : BaseGameEvent<GameObjectVector3> { }
  