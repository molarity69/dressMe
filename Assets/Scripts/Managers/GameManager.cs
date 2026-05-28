using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityTimer;

public class GameManager : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private GameStateEvent _gameStateEvent;

    [Header("Main Menu")]
    [SerializeField] private GameObject _mainMenuObject;
    [SerializeField] private MainMenuAnimator _mainMenuAnimator;

    [Header("Levels")]
    [SerializeField] private List<LevelData> _levels = new List<LevelData>();
    [SerializeField] private List<GameObject> _miniGames = new List<GameObject>();

    [Header("Audio")]
    [SerializeField] private AudioManager _audioManager;
    [SerializeField] private AudioClip _mainMenuMusic;
    [SerializeField] private AudioClip _citySounds;

    [Header("Day 2")]
    [SerializeField] private GameObject _prepareDay2Image;
    [SerializeField] private GameObject _activateDay2;
    [SerializeField] private ChairRocking _chairRocking;

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
        if (_mainMenuAnimator != null)
        {
            _mainMenuAnimator.Play(OnMainMenuAnimationComplete);
        }
        else
        {
            OnMainMenuAnimationComplete();
        }
        if (_audioManager != null)
            _audioManager.FadeOutBGM(5.0f);
    }

    private void OnMainMenuAnimationComplete()
    {
        if (_mainMenuObject != null)
            _mainMenuObject.SetActive(false);
        if (_audioManager != null)
            _audioManager.PlayLoop(_citySounds);
        TransitionToState(GameState.Narrative);
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
            _chairRocking.StopRocking();
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
            case GameState.MiniGameColoring: OnEnterMiniGame(); break;
            case GameState.Narrative: OnEnterNarrative(); break;
            case GameState.PrepareDay2: PrepareDay2(); break;
            case GameState.MiniGameZipper: OnEnterMiniGameZipper(); break;
        }

        Debug.Log("Current State: " + _currentState);
    }

    private void OnEnterMainMenu()
    {
        if (_mainMenuObject != null)
            _mainMenuObject.SetActive(true);
        if (_audioManager != null)
            _audioManager.PlayBGM(_mainMenuMusic);
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

    private void OnEnterMiniGameZipper()
    {
        DeactivateAllMiniGames();
        ActivateMiniGame(_currentLevelIndex);
    }

    private void OnEnterNarrative()
    {
        DeactivateAllMiniGames();
    }

    private void PrepareDay2()
    {
        _prepareDay2Image.SetActive(true);
        _audioManager.FadeInBGM(3.0f);
        FadeInHoldFadeOut(_prepareDay2Image.GetComponent<SpriteRenderer>(), 3.0f, 2.0f);
        
        _currentLevelIndex++;
    }

    private void FadeInHoldFadeOut(SpriteRenderer sprite, float fadeDuration, float holdDuration)
    {
        FadeIn(sprite, fadeDuration);
        Timer.Register(fadeDuration, onComplete: () =>
        {
            Timer.Register(holdDuration, onComplete: () =>
            {
                _audioManager.FadeOutBGM(3.0f);
                FadeOut(sprite, fadeDuration);
            });
        });
    }

    private void FadeIn(SpriteRenderer sprite, float duration)
    {
        Color color = sprite.color;
        color.a = 0f;
        sprite.color = color;

        Timer.Register(duration, onComplete: () =>
        {
            color.a = 1f;
            sprite.color = color;
            _activateDay2.SetActive(true);
            _chairRocking.StartRockingAgain();
        },
        onUpdate: t =>
        {
            color.a = Mathf.Lerp(0f, 1f, t / duration);
            sprite.color = color;
        },
        isLooped: false,
        useRealTime: false);
    }

    private void FadeOut(SpriteRenderer sprite, float duration)
    {
        Color color = sprite.color;

        Timer.Register(duration, onComplete: () =>
        {
            color.a = 0f;
            sprite.color = color;
            _prepareDay2Image.SetActive(false);
            
            GoToNarrative();

        },
        onUpdate: t =>
        {
            color.a = Mathf.Lerp(1f, 0f, t / duration);
            sprite.color = color;
        },
        isLooped: false,
        useRealTime: false);
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
    public void GoToMiniGame() => TransitionToState(GameState.MiniGameColoring);
    public void GoToNarrative() => TransitionToState(GameState.Narrative);
    public void GoToMainMenu() => TransitionToState(GameState.MainMenu);
    public void GoToState(GameState state) => TransitionToState(state);

    public void ResetLevels()
    {
        DeactivateAllMiniGames();
        _currentLevelIndex = 0;
        _clickedHotspots.Clear();
    }

    public GameState GetCurrentState()
    {
        return _currentState;
    }
}