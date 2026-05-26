namespace ContainmentProtocol.Game.Events
{
    public enum GameState
    {
        Idle,
        Playing,
        BetweenRounds,
        GameOver
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
}