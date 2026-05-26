public enum GameState
{
    MainMenu,
    PointAndClick,
    MiniGame,
    Narrative
}

public class GameStateData
{
    public GameState State { get; }
    public GameState PreviousState { get; }

    public GameStateData(GameState state, GameState previousState)
    {
        State = state;
        PreviousState = previousState;
    }
}