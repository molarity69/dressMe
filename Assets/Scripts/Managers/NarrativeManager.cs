using UnityEngine;

public class NarrativeManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager _gameManager;
    [SerializeField] private SpeechBox _mrsSulaBox;
    [SerializeField] private SpeechBox _playerBox;

    [Header("Data")]
    [SerializeField] private NarrativeData _narrativeData;

    private int _currentIndex = 0;
    private NarrativeCharacter _currentCharacter;
    private bool _isActive = false;
    private bool _isTransitioning = false;

    public void Activate()
    {
        _isActive = true;
        _isTransitioning = false;

        _mrsSulaBox.HideImmediate();
        _playerBox.HideImmediate();

        ShowCurrentLine();
    }

    public void Deactivate()
    {
        _isActive = false;
        _mrsSulaBox.HideImmediate();
        _playerBox.HideImmediate();
    }

    public void OnGameStateChanged(GameStateData data)
    {
        if (data.State == GameState.Narrative)
            Activate();
        else
            Deactivate();
    }

    private void Update()
    {
        if (!_isActive) return;
        if (_isTransitioning) return;

        if (Input.GetMouseButtonDown(0))
        {
            Advance();
        }
    }

    private void Advance()
    {
        if (_narrativeData == null) return;
        if (_currentIndex >= _narrativeData.Lines.Count) return;

        NarrativeLine currentLine = _narrativeData.Lines[_currentIndex];

        if (currentLine.HasTransition)
        {
            _isTransitioning = true;
            _gameManager.GoToState(currentLine.TransitionState);
            _currentCharacter = NarrativeCharacter.None;
            _currentIndex++;
            return;
        }

        _currentIndex++;

        if (_currentIndex >= _narrativeData.Lines.Count) return;

        ShowCurrentLine();
    }


    private void ShowCurrentLine()
    {
        if (_narrativeData == null) return;
        if (_currentIndex >= _narrativeData.Lines.Count) return;

        NarrativeLine line = _narrativeData.Lines[_currentIndex];
        SpeechBox activeBox = line.Character == NarrativeCharacter.MrsSula ? _mrsSulaBox : _playerBox;
        SpeechBox inactiveBox = line.Character == NarrativeCharacter.MrsSula ? _playerBox : _mrsSulaBox;

        bool sameCharacter = _currentIndex > 0 && line.Character == _currentCharacter;

        if (sameCharacter)
        {
            activeBox.SwapSprite(line.SpeechSprite);
        }
        else
        {
            inactiveBox.HideWithFade();
            activeBox.ShowWithFade(line.SpeechSprite);
        }

        _currentCharacter = line.Character;
    }
}