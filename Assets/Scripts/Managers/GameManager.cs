using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private GameStateEvent _gameStateEvent;

    [Header("Main Menu")]
    [SerializeField] private GameObject _mainMenuObject;

    [Header("Levels")]
    [SerializeField] private List<LevelData> _levels = new List<LevelData>();
    [SerializeField] private List<GameObject> _miniGames = new List<GameObject>();

    private GameState _currentState = GameState.MainMenu;
    private int _currentLevelIndex = 0;
    private readonly List<HotspotID> _clickedHotspots = new List<HotspotID>();

    private void Start()
    {
        DeactivateAllMiniGames();
        TransitionToState(GameState.MainMenu);
    }

    public void OnPlayPressed()
    {
        if (_mainMenuObject != null)
            _mainMenuObject.SetActive(false);

        TransitionToState(GameState.PointAndClick);
    }

    public void OnHotspotClicked(int hotspotIDValue)
    {
        if (_currentState != GameState.PointAndClick)
            return;

        if (_currentLevelIndex >= _levels.Count)
        {
            Debug.LogWarning("GameManager: No more levels to process.");
            return;
        }

        LevelData currentLevel = _levels[_currentLevelIndex];
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
            TransitionToState(currentLevel.TargetState);
        }
    }

    private void TransitionToState(GameState newState)
    {
        GameState previousState = _currentState;
        _currentState = newState;

        if (_gameStateEvent != null)
            _gameStateEvent.Raise(new GameStateData(_currentState, previousState));
        else
            Debug.LogWarning("GameManager has no GameStateEvent assigned.");

        switch (_currentState)
        {
            case GameState.MainMenu: OnEnterMainMenu(); break;
            case GameState.PointAndClick: OnEnterPointAndClick(); break;
            case GameState.MiniGame: OnEnterMiniGame(); break;
            case GameState.Narrative: OnEnterNarrative(); break;
        }
    }

    private void OnEnterMainMenu()
    {
        if (_mainMenuObject != null)
            _mainMenuObject.SetActive(true);
    }

    private void OnEnterPointAndClick()
    {
        DeactivateAllMiniGames();
    }

    private void OnEnterMiniGame()
    {
        DeactivateAllMiniGames();
        ActivateMiniGame(_currentLevelIndex);
    }

    private void OnEnterNarrative()
    {
        DeactivateAllMiniGames();
        _currentLevelIndex++;
    }

    private void ActivateMiniGame(int index)
    {
        if (index >= _miniGames.Count) return;
        if (_miniGames[index] == null)
        {
            Debug.LogWarning($"GameManager: No mini-game assigned at index {index}.");
            return;
        }

        _miniGames[index].SetActive(true);
    }

    private void DeactivateAllMiniGames()
    {
        foreach (GameObject miniGame in _miniGames)
        {
            if (miniGame != null)
                miniGame.SetActive(false);
        }
    }

    public void GoToPointAndClick() => TransitionToState(GameState.PointAndClick);
    public void GoToMiniGame() => TransitionToState(GameState.MiniGame);
    public void GoToNarrative() => TransitionToState(GameState.Narrative);
    public void GoToMainMenu() => TransitionToState(GameState.MainMenu);

    public void ResetLevels()
    {
        DeactivateAllMiniGames();
        _currentLevelIndex = 0;
        _clickedHotspots.Clear();
    }
}