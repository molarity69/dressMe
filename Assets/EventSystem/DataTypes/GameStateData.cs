public enum GameState
{
    MainMenu,
    PointAndClick,
    MiniGameColoring,
    Narrative,
    MiniGameZipper,
    PrepareDay2
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