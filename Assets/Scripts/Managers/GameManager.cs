using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private GameStateEvent _gameStateEvent;

    [Header("Levels")]
    [SerializeField] private List<HotspotSequenceData> _levels = new List<HotspotSequenceData>();

    private GameState _currentState = GameState.MainMenu;
    private int _currentLevelIndex = 0;
    private readonly List<HotspotID> _clickedHotspots = new List<HotspotID>();

    private void Start()
    {
        TransitionToState(GameState.PointAndClick);
    }

    public void OnHotspotClicked(int hotspotIDValue)
    {

        Debug.Log("Hotspot clicked: " + hotspotIDValue);
        if (_currentState != GameState.PointAndClick)
            return;

        if (_currentLevelIndex >= _levels.Count)
        {
            Debug.LogWarning("GameManager: No more levels to process.");
            return;
        }

        HotspotSequenceData currentLevel = _levels[_currentLevelIndex];
        HotspotID clickedID = (HotspotID)hotspotIDValue;

        if (!currentLevel.RequiredHotspots.Contains(clickedID))
            return;

        if (currentLevel.RequireOrder)
        {
            HotspotID expectedID = currentLevel.RequiredHotspots[_clickedHotspots.Count];

            if (clickedID != expectedID)
            {
                _clickedHotspots.Clear();
                return;
            }
        }

        if (!_clickedHotspots.Contains(clickedID))
            _clickedHotspots.Add(clickedID);

        if (_clickedHotspots.Count >= currentLevel.RequiredHotspots.Count)
        {
            _clickedHotspots.Clear();
            _currentLevelIndex++;
            TransitionToState(currentLevel.TargetState);
        }
    }

    private void TransitionToState(GameState newState)
    {
        GameState previousState = _currentState;
        _currentState = newState;

        if (_gameStateEvent == null)
        {
            Debug.LogWarning("GameManager has no GameStateEvent assigned.");
            return;
        }

        _gameStateEvent.Raise(new GameStateData(_currentState, previousState));

        switch (_currentState)
        {
            case GameState.MainMenu: OnEnterMainMenu(); break;
            case GameState.PointAndClick: OnEnterPointAndClick(); break;
            case GameState.MiniGame: OnEnterMiniGame(); break;
            case GameState.Narrative: OnEnterNarrative(); break;
        }

        Debug.Log("Cuurent State: " + _currentState);
    }

    private void OnEnterMainMenu() { }
    private void OnEnterPointAndClick() { }
    private void OnEnterMiniGame() { }
    private void OnEnterNarrative() { }

    public void GoToPointAndClick() => TransitionToState(GameState.PointAndClick);
    public void GoToMiniGame() => TransitionToState(GameState.MiniGame);
    public void GoToNarrative() => TransitionToState(GameState.Narrative);
    public void GoToMainMenu() => TransitionToState(GameState.MainMenu);
}