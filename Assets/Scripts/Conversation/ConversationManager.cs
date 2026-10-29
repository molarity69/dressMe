using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using LLMUnity;
using TMPro;
using UnityEngine;
using UnityTimer;

[Serializable]
public class ComfortVisual
{
    [Tooltip("This look applies from this comfort value upwards (0–100).")]
    public int MinComfort;
    [Tooltip("Applied to every Character Renderer (the rocking poses). Leave empty to keep the original sprite.")]
    public Sprite Sprite;
    [Tooltip("Optional standalone pose, e.g. MG04_WOMAN HAPPY. When set, rocking pauses and only this pose is shown.")]
    public GameObject PoseObject;
    [Tooltip("Optional. Plays when her comfort moves into this band (up or down), and for her starting band when the conversation opens.")]
    public AudioClip EnterClip;
}

/// <summary>
/// Care conversation: the player talks to the character and can fix things in the room at the same time.
/// A hidden comfort meter, driven by her mood tags and by room fixes, decides her look and when she is calm.
/// Her replies use her speech box; the player types in a canvas input field that replaces the player's box.
/// Enter it with a NarrativeLine transition to GameState.Conversation, or GameManager.GoToConversation().
/// </summary>
public class ConversationManager : MonoBehaviour, IGameEventListener<int>
{
    [Header("References")]
    [SerializeField] private GameManager _gameManager;
    [SerializeField] private AudioManager _audioManager;
    [SerializeField] private LLMCharacter _llmCharacter;
    [SerializeField] private ConversationBeat _beat;
    [Tooltip("The existing HotspotEvent, so room fixes reach the conversation.")]
    [SerializeField] private IntEvent _hotspotEvent;

    [Header("Speech Boxes")]
    [SerializeField] private SpeechBox _characterBox;
    [SerializeField] private Sprite _characterBubbleSprite;
    [Tooltip("Hidden during the conversation; the Player Input takes its place.")]
    [SerializeField] private SpeechBox _playerBox;

    [Header("Input")]
    [SerializeField] private TMP_InputField _playerInput;
    [Tooltip("Shown in her bubble, one after another, until the first words of her reply arrive.")]
    [SerializeField] private string[] _thinkingFrames = { ".", "..", "..." };
    [SerializeField] private float _thinkingFrameTime = 0.4f;

    [Header("Reliability")]
    [Tooltip("Seconds without any reply text before giving up on her FIRST reply. Longer, because the model may still be reading her prompt on a slow PC.")]
    [SerializeField] private float _firstReplyTimeout = 75f;
    [Tooltip("Seconds without any new reply text before giving up on a later reply.")]
    [SerializeField] private float _replyTimeout = 25f;
    [Tooltip("After this many failed replies in a row, the beat ends with the feedback card so the story can continue.")]
    [SerializeField] private int _maxFailuresBeforeEnding = 2;

    [Header("Room")]
    [Tooltip("The room stays locked until her first AI reply. When ticked, it also locks while she is replying, so clicks can't stack up.")]
    [SerializeField] private bool _lockRoomWhileReplying = true;

    [Header("Character Look")]
    [Tooltip("All renderers that make up her default look, e.g. both rocking poses.")]
    [SerializeField] private List<SpriteRenderer> _characterRenderers = new List<SpriteRenderer>();
    [SerializeField] private ChairRocking _chairRocking;
    [Tooltip("Her look and sound by comfort level. Mood tags only move comfort; this list decides what the player sees and hears.")]
    [SerializeField] private List<ComfortVisual> _comfortVisuals = new List<ComfortVisual>();

    [Header("Feedback Card")]
    [SerializeField] private GameObject _feedbackPanel;
    [SerializeField] private TMP_Text _feedbackText;
    [SerializeField] private float _feedbackDelay = 2f;
    [SerializeField] private float _feedbackMinimumTime = 0.75f;
    [Tooltip("Shrinks the feedback text to fit the card, so long 'missed' lists don't overflow.")]
    [SerializeField] private bool _feedbackAutoSize = true;
    [SerializeField] private float _feedbackMinFontSize = 18f;
    [SerializeField] private float _feedbackMaxFontSize = 32f;

    [Header("Debug")]
    [SerializeField] private bool _logComfort = true;

    private bool _isActive = false;
    private bool _isWaitingForReply = false;
    private bool _isFinishing = false;
    private bool _isShowingFeedback = false;
    private bool _reachedCalm = false;
    private bool _hasAgreed = false;
    private bool _currentRequestIsPlayerLine = false;
    private bool _hasHadFirstReply = false;
    private int _turnCount = 0;
    private int _comfortFromTalking = 0;
    private int _comfortFromRoom = 0;
    private float _feedbackShownTime = 0f;
    private string _latestReply = string.Empty;
    private ComfortMeter _comfort;
    private ComfortVisual _currentVisual;
    private ComfortBand _toldBand;
    private string _pendingFeelingNote = string.Empty;
    private RoomIssue _lastHintedIssue;
    private string _lastThought = string.Empty;
    private readonly List<RoomIssue> _openIssues = new List<RoomIssue>();
    private readonly Queue<string> _pendingNotes = new Queue<string>();
    private Timer _finishTimer;
    private Coroutine _pendingActivation;

    // Reply tracking: each request gets an id so late callbacks from a cancelled request are ignored.
    private int _requestId = 0;
    private Task<string> _currentChatTask;
    private float _lastProgressTime = 0f;
    private float _currentTimeout = 0f;
    private bool _hasVisibleReplyText = false;
    private int _consecutiveFailures = 0;
    private string _lastFallbackLine = string.Empty;
    private Coroutine _thinkingRoutine;

    // Snapshot of her look on entry, restored on exit so later story beats play as designed.
    private readonly List<Sprite> _savedSprites = new List<Sprite>();
    private readonly List<bool> _savedRendererActive = new List<bool>();
    private readonly List<GameObject> _poseObjects = new List<GameObject>();
    private readonly List<bool> _savedPoseActive = new List<bool>();
    private bool _savedIsRocking = false;
    private bool _hasSnapshot = false;

    // ── Unity ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_playerInput != null)
        {
            _playerInput.onSubmit.AddListener(OnPlayerSubmit);
            _playerInput.gameObject.SetActive(false);
        }

        if (_feedbackPanel != null)
            _feedbackPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (_hotspotEvent != null)
            _hotspotEvent.RegisterListener(this);
    }

    private void OnDisable()
    {
        if (_hotspotEvent != null)
            _hotspotEvent.UnregisterListener(this);
    }

    private void Start()
    {
        if (_llmCharacter == null || _beat == null || _beat.Persona == null)
        {
            Debug.LogError("ConversationManager: LLM Character, Beat or the Beat's Persona is not assigned.");
            return;
        }

        CharacterPersona persona = _beat.Persona;
        _llmCharacter.AIName = persona.CharacterName;
        _llmCharacter.playerName = persona.PlayerName;

        // Stop the model from writing the player's lines for them.
        string playerStop = persona.PlayerName + ":";
        if (!_llmCharacter.stop.Contains(playerStop))
            _llmCharacter.stop.Add(playerStop);

        // Read the (fixed) prompt in the background now, during the menu and intro,
        // so the first reply isn't slow. The same prompt is reused for the whole beat.
        _llmCharacter.SetPrompt(_beat.BuildPrompt());
        _ = _llmCharacter.Warmup();
    }

    private void OnDestroy()
    {
        if (_playerInput != null)
            _playerInput.onSubmit.RemoveListener(OnPlayerSubmit);
    }

    private void Update()
    {
        CheckReplyHealth();

        if (!_isShowingFeedback) return;
        if (Time.unscaledTime - _feedbackShownTime < _feedbackMinimumTime) return;

        bool continuePressed = Input.GetMouseButtonDown(0)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.Space);

        if (continuePressed)
        {
            _isShowingFeedback = false;
            // Leave the state next frame so this click doesn't also advance the narrative.
            StartCoroutine(CompleteNextFrame());
        }
    }

    // ── Game state ───────────────────────────────────────────────────────────

    public void OnGameStateChanged(GameStateData data)
    {
        if (data.State == GameState.Conversation)
        {
            // NarrativeManager hides the shared speech boxes on this same state change,
            // so wait a frame before showing them.
            CancelPendingActivation();
            _pendingActivation = StartCoroutine(ActivateNextFrame());
        }
        else
        {
            CancelPendingActivation();
            if (_isActive)
                Deactivate();
        }
    }

    private IEnumerator ActivateNextFrame()
    {
        yield return null;
        _pendingActivation = null;
        Activate();
    }

    private void Activate()
    {
        if (_llmCharacter == null || _beat == null) return;

        _isActive = true;
        _isWaitingForReply = false;
        _isFinishing = false;
        _isShowingFeedback = false;
        _reachedCalm = false;
        _hasAgreed = false;
        _turnCount = 0;
        _hasHadFirstReply = false;
        _comfortFromTalking = 0;
        _comfortFromRoom = 0;
        _latestReply = string.Empty;
        _pendingNotes.Clear();
        _openIssues.Clear();
        _openIssues.AddRange(_beat.RoomIssues);
        _comfort = new ComfortMeter(_beat.StartComfort);
        _currentVisual = null;
        _toldBand = _beat.GetBand(_comfort.Value);
        _pendingFeelingNote = string.Empty;
        _lastHintedIssue = null;
        _lastThought = string.Empty;
        _consecutiveFailures = 0;
        _currentChatTask = null;
        _lastFallbackLine = string.Empty;

        SaveCharacterLook();
        // Same text as the warm-up, so the model reuses what it already read instead of starting over.
        _llmCharacter.SetPrompt(_beat.BuildPrompt(), true);
        LogComfort(0, "start");

        if (_playerBox != null)
            _playerBox.HideImmediate();

        ShowOrUpdate(_characterBox, _characterBubbleSprite, _beat.OpeningLine);
        ApplyComfortVisual();

        // No room clicks until she has given her first real reply.
        SetRoomLocked(true);

        _playerInput.gameObject.SetActive(true);
        EnableInput(true);
    }

    // Left the Conversation state from outside (not our own ending).
    private void Deactivate()
    {
        _isActive = false;
        _isShowingFeedback = false;
        CancelFinishTimer();
        StopThinking();

        if (_isWaitingForReply)
        {
            _requestId++;
            _llmCharacter.CancelRequests();
            _isWaitingForReply = false;
            _currentChatTask = null;
        }

        _pendingNotes.Clear();
        _playerInput.gameObject.SetActive(false);
        if (_feedbackPanel != null)
            _feedbackPanel.SetActive(false);
        HideBoxes();
        RestoreCharacterLook();
        SetRoomLocked(false);
    }

    // ── Talking ──────────────────────────────────────────────────────────────

    private void OnPlayerSubmit(string message)
    {
        if (!_isActive || _isWaitingForReply || _isFinishing) return;

        string trimmedMessage = message.Trim();
        if (trimmedMessage.Length == 0)
        {
            _playerInput.ActivateInputField();
            return;
        }

        // Keep the sent line visible (greyed out) in the input while she answers.
        _playerInput.text = trimmedMessage;

        // The game decides what kind of reply she gives (a subtle hint or a random thought); the model decides how.
        string directorNote = ChooseDirectorNote();
        string playerMessage = directorNote.Length > 0 ? trimmedMessage + "\n" + directorNote : trimmedMessage;
        SendToCharacter(playerMessage, true);
    }

    // Room fixes arrive here from the HotspotEvent.
    public void OnEventRaised(int hotspotValue)
    {
        if (!_isActive || _isFinishing) return;

        HotspotID hotspot = (HotspotID)hotspotValue;
        RoomIssue fixedIssue = _openIssues.Find(issue => issue.Hotspot == hotspot);
        if (fixedIssue == null) return;

        _openIssues.Remove(fixedIssue);
        int gained = _comfort.Change(fixedIssue.ComfortGain);
        _comfortFromRoom += gained;
        LogComfort(gained, $"fixed {hotspot}");

        QueueFeelingNoteIfChanged();
        ApplyComfortVisual();

        if (string.IsNullOrWhiteSpace(fixedIssue.ActionNote)) return;

        string actionNote = _beat.BuildActionNote(fixedIssue);
        if (_isWaitingForReply)
            _pendingNotes.Enqueue(actionNote);
        else
            SendToCharacter(actionNote, false);
    }

    private void SendToCharacter(string message, bool isPlayerLine)
    {
        _isWaitingForReply = true;
        _currentRequestIsPlayerLine = isPlayerLine;
        _latestReply = string.Empty;
        _playerInput.interactable = false;

        if (_lockRoomWhileReplying)
            SetRoomLocked(true);

        // A change in how she feels travels with the next message instead of rewriting her prompt.
        string outgoingMessage = message;
        if (_pendingFeelingNote.Length > 0)
        {
            outgoingMessage = _pendingFeelingNote + "\n" + message;
            _pendingFeelingNote = string.Empty;
        }

        ShowOrUpdate(_characterBox, _characterBubbleSprite, GetThinkingFrame(_thinkingFrames.Length - 1));
        StartThinking();

        _requestId++;
        int requestId = _requestId;
        _hasVisibleReplyText = false;
        _lastProgressTime = Time.unscaledTime;
        _currentTimeout = _hasHadFirstReply ? _replyTimeout : _firstReplyTimeout;
        _currentChatTask = _llmCharacter.Chat(outgoingMessage,
            reply => OnReplyUpdated(requestId, reply),
            () => OnReplyCompleted(requestId));
    }

    // Called repeatedly while streaming; receives the full reply so far.
    private void OnReplyUpdated(int requestId, string reply)
    {
        if (!_isActive || requestId != _requestId || !_isWaitingForReply) return;

        _latestReply = reply;
        _lastProgressTime = Time.unscaledTime;

        string displayText = CleanForDisplay(reply);
        if (displayText.Length == 0) return; // keep the thinking dots until real words arrive

        if (!_hasVisibleReplyText)
        {
            _hasVisibleReplyText = true;
            StopThinking();
        }
        _characterBox.SetText(displayText);
    }

    private void OnReplyCompleted(int requestId)
    {
        if (!_isActive || requestId != _requestId || !_isWaitingForReply) return;

        string replyText = RemoveNamePrefix(_latestReply);
        if (CleanForDisplay(replyText).Length == 0)
        {
            HandleReplyFailure("empty reply");
            return;
        }

        _consecutiveFailures = 0;
        _characterBox.SetText(CleanForDisplay(replyText));
        CompleteReply(replyText);
    }

    // Timeouts, errors, or an empty answer: she says a scripted line instead and the conversation carries on.
    private void HandleReplyFailure(string reason)
    {
        Debug.LogWarning($"ConversationManager: no usable reply ({reason}). Using a fallback line.");

        _requestId++; // ignore anything the cancelled request still sends
        _llmCharacter.CancelRequests();
        _consecutiveFailures++;

        _lastFallbackLine = _beat.GetFallbackLine(_lastFallbackLine);
        StopThinking();
        _characterBox.SetText(_lastFallbackLine);

        if (_consecutiveFailures >= _maxFailuresBeforeEnding)
        {
            Debug.LogWarning($"ConversationManager: {_consecutiveFailures} failed replies in a row, ending the beat.");
            _isWaitingForReply = false;
            _currentChatTask = null;
            _hasHadFirstReply = true;
            Finish(false);
            return;
        }

        // No mood tag, so comfort doesn't change.
        CompleteReply(string.Empty);
    }

    // Checked every frame while waiting: a crashed request, a finished one that never called back, or silence.
    private void CheckReplyHealth()
    {
        if (!_isActive || !_isWaitingForReply || _isFinishing) return;

        if (_currentChatTask != null && _currentChatTask.IsCompleted)
        {
            if (_currentChatTask.IsFaulted || _currentChatTask.IsCanceled)
            {
                HandleReplyFailure(_currentChatTask.IsFaulted ? "error: " + _currentChatTask.Exception?.GetBaseException().Message : "cancelled");
                return;
            }

            // Finished without calling back: use whatever it returned.
            if (string.IsNullOrEmpty(_latestReply) && _currentChatTask.Result != null)
                _latestReply = _currentChatTask.Result;
            OnReplyCompleted(_requestId);
            return;
        }

        if (Time.unscaledTime - _lastProgressTime > _currentTimeout)
            HandleReplyFailure($"no reply text for {_currentTimeout:0} s");
    }

    private void CompleteReply(string replyText)
    {
        _isWaitingForReply = false;
        _currentChatTask = null;
        StopThinking();

        _hasHadFirstReply = true;
        if (_currentRequestIsPlayerLine)
            _turnCount++;

        if (ConversationTags.TryGetMood(replyText, out CharacterMood mood))
        {
            int gained = _comfort.Change(_beat.GetMoodDelta(mood));
            if (_currentRequestIsPlayerLine)
                _comfortFromTalking += gained;
            else
                _comfortFromRoom += gained;

            LogComfort(gained, $"mood {mood}");
            QueueFeelingNoteIfChanged();
        }

        if (ConversationTags.HasGoalTag(replyText))
            _hasAgreed = true;

        ApplyComfortVisual();

        bool isCalm = _comfort.Value >= _beat.CalmThreshold && (!_beat.RequiresAgreement || _hasAgreed);
        if (isCalm)
        {
            Finish(true);
            return;
        }

        if (_turnCount >= _beat.MaxTurns)
        {
            Finish(false);
            return;
        }

        if (_pendingNotes.Count > 0)
        {
            SendToCharacter(_pendingNotes.Dequeue(), false);
            return;
        }

        // Her reply is done: the room opens up (from her first reply onwards).
        SetRoomLocked(false);

        // Only clear the input after the player's own line, so a half-typed line survives a room fix.
        EnableInput(_currentRequestIsPlayerLine);
    }

    // ── Ending ───────────────────────────────────────────────────────────────

    private void Finish(bool reachedCalm)
    {
        _isFinishing = true;
        _reachedCalm = reachedCalm;
        StopThinking();
        _pendingNotes.Clear();
        _playerInput.interactable = false;
        SetRoomLocked(true);

        Debug.Log($"ConversationManager: finished after {_turnCount} turns. Calm: {reachedCalm}, comfort {_comfort.Value} " +
                  $"(talking +{_comfortFromTalking}, room +{_comfortFromRoom}), unfixed issues: {_openIssues.Count}.");

        CancelFinishTimer();
        _finishTimer = Timer.Register(_feedbackDelay, onComplete: ShowFeedback);
    }

    private void ShowFeedback()
    {
        if (!_isActive) return;

        _playerInput.gameObject.SetActive(false);

        if (_feedbackPanel == null || _feedbackText == null)
        {
            StartCoroutine(CompleteNextFrame());
            return;
        }

        if (_feedbackAutoSize)
        {
            _feedbackText.enableAutoSizing = true;
            _feedbackText.fontSizeMin = _feedbackMinFontSize;
            _feedbackText.fontSizeMax = _feedbackMaxFontSize;
        }

        _feedbackText.text = _beat.BuildFeedback(_reachedCalm, _comfortFromTalking, _comfortFromRoom, _openIssues);
        _feedbackPanel.SetActive(true);
        _feedbackShownTime = Time.unscaledTime;
        _isShowingFeedback = true;
    }

    private IEnumerator CompleteNextFrame()
    {
        yield return null;

        _isActive = false;
        if (_feedbackPanel != null)
            _feedbackPanel.SetActive(false);
        _playerInput.gameObject.SetActive(false);
        HideBoxes();

        // Put her renderers back the way they were, then end like the original level if she is calm.
        RestoreCharacterLook();
        if (_reachedCalm && _beat.StopRockingOnCalm && _chairRocking != null)
            _chairRocking.StopRocking();

        SetRoomLocked(false);

        if (_gameManager != null)
            _gameManager.GoToState(_beat.NextState);
        else
            Debug.LogWarning("ConversationManager: no GameManager assigned.");
    }

    // ── Prompt and comfort ───────────────────────────────────────────────────

    // Hint at one unfixed issue, or share a random thought. Her first reply hints, if the beat says so.
    private string ChooseDirectorNote()
    {
        bool canHint = _openIssues.Count > 0;
        bool canThink = _beat.RandomThoughts.Count > 0;
        if (!canHint && !canThink) return string.Empty;

        bool isFirstReply = _turnCount == 0;
        bool giveHint;
        if (!canHint)
            giveHint = false;
        else if (!canThink || (isFirstReply && _beat.FirstReplyHints))
            giveHint = true;
        else
            giveHint = UnityEngine.Random.value < _beat.HintChance;

        if (giveHint)
        {
            RoomIssue issue = PickDifferent(_openIssues, _lastHintedIssue);
            _lastHintedIssue = issue;
            return _beat.BuildHintNote(issue);
        }

        string thought = PickDifferent(_beat.RandomThoughts, _lastThought);
        _lastThought = thought;
        return _beat.BuildThoughtNote(thought);
    }

    // Random item, avoiding the previous one when there is a choice.
    private static T PickDifferent<T>(IReadOnlyList<T> items, T previous)
    {
        if (items.Count == 1) return items[0];

        T picked = items[UnityEngine.Random.Range(0, items.Count)];
        while (EqualityComparer<T>.Default.Equals(picked, previous))
            picked = items[UnityEngine.Random.Range(0, items.Count)];
        return picked;
    }

    // When comfort crosses into another band, tell her with her next message.
    private void QueueFeelingNoteIfChanged()
    {
        ComfortBand band = _beat.GetBand(_comfort.Value);
        if (band == _toldBand) return;

        _toldBand = band;
        _pendingFeelingNote = _beat.BuildFeelingNote(band);
    }

    private void LogComfort(int change, string reason)
    {
        if (!_logComfort) return;
        Debug.Log($"[Comfort] {_comfort.Value} ({change:+0;-0;0}) — {reason}");
    }

    // ── Look and sound ───────────────────────────────────────────────────────

    private void ApplyComfortVisual()
    {
        ComfortVisual visual = GetComfortVisual(_comfort.Value);
        if (visual == _currentVisual) return;
        _currentVisual = visual;

        if (visual != null && visual.PoseObject != null)
            ShowPose(visual.PoseObject);
        else
            ShowDefaultLook(visual != null ? visual.Sprite : null);

        if (visual != null && visual.EnterClip != null && _audioManager != null)
            _audioManager.PlayOneShot(visual.EnterClip);
    }

    private ComfortVisual GetComfortVisual(int comfort)
    {
        ComfortVisual bestVisual = null;
        foreach (ComfortVisual visual in _comfortVisuals)
        {
            if (comfort >= visual.MinComfort && (bestVisual == null || visual.MinComfort > bestVisual.MinComfort))
                bestVisual = visual;
        }
        return bestVisual;
    }

    // Standalone pose: pause rocking, hide the default renderers and every other pose.
    private void ShowPose(GameObject pose)
    {
        if (_chairRocking != null)
            _chairRocking.PauseRocking();

        foreach (SpriteRenderer characterRenderer in _characterRenderers)
        {
            if (characterRenderer != null)
                characterRenderer.gameObject.SetActive(false);
        }

        foreach (GameObject poseObject in _poseObjects)
            poseObject.SetActive(poseObject == pose);
    }

    // Default look: hide standalone poses, show the renderers with the band's sprite (or the original one),
    // and resume rocking only if she was rocking when the conversation started.
    private void ShowDefaultLook(Sprite sprite)
    {
        foreach (GameObject poseObject in _poseObjects)
            poseObject.SetActive(false);

        for (int i = 0; i < _characterRenderers.Count; i++)
        {
            SpriteRenderer characterRenderer = _characterRenderers[i];
            if (characterRenderer == null) continue;

            characterRenderer.gameObject.SetActive(true);
            characterRenderer.sprite = sprite != null ? sprite : _savedSprites[i];
        }

        if (_chairRocking != null && _savedIsRocking)
            _chairRocking.StartRocking();
    }

    private void SaveCharacterLook()
    {
        _savedSprites.Clear();
        _savedRendererActive.Clear();
        foreach (SpriteRenderer characterRenderer in _characterRenderers)
        {
            _savedSprites.Add(characterRenderer != null ? characterRenderer.sprite : null);
            _savedRendererActive.Add(characterRenderer != null && characterRenderer.gameObject.activeSelf);
        }

        _poseObjects.Clear();
        _savedPoseActive.Clear();
        foreach (ComfortVisual visual in _comfortVisuals)
        {
            if (visual.PoseObject == null || _poseObjects.Contains(visual.PoseObject)) continue;
            _poseObjects.Add(visual.PoseObject);
            _savedPoseActive.Add(visual.PoseObject.activeSelf);
        }

        _savedIsRocking = _chairRocking != null && _chairRocking.IsRocking;
        _hasSnapshot = true;
    }

    private void RestoreCharacterLook()
    {
        if (!_hasSnapshot) return;
        _hasSnapshot = false;

        for (int i = 0; i < _characterRenderers.Count; i++)
        {
            SpriteRenderer characterRenderer = _characterRenderers[i];
            if (characterRenderer == null) continue;
            characterRenderer.sprite = _savedSprites[i];
            characterRenderer.gameObject.SetActive(_savedRendererActive[i]);
        }

        for (int i = 0; i < _poseObjects.Count; i++)
            _poseObjects[i].SetActive(_savedPoseActive[i]);

        if (_chairRocking == null) return;
        if (_savedIsRocking)
            _chairRocking.StartRocking();
        else
            _chairRocking.PauseRocking();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ShowOrUpdate(SpeechBox box, Sprite bubble, string text)
    {
        if (box == null) return;

        if (box.IsVisible)
        {
            box.SwapSprite(bubble);
            box.SetText(text);
        }
        else
        {
            box.ShowTextWithFade(bubble, text);
        }
    }

    private void HideBoxes()
    {
        if (_characterBox != null)
            _characterBox.HideImmediate();
        if (_playerBox != null)
            _playerBox.HideImmediate();
    }

    private string CleanForDisplay(string reply)
    {
        // Name first, so "Mrs Sula: HAPPY: ..." loses its bare tag too; then again for "[HAPPY] Mrs Sula: ...".
        string cleaned = ConversationTags.StripTags(RemoveNamePrefix(reply));
        return RemoveNamePrefix(cleaned);
    }

    // Small models sometimes prefix their own name, e.g. "Mrs Sula: ..."
    private string RemoveNamePrefix(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string trimmed = text.TrimStart();
        string namePrefix = _beat.Persona.CharacterName + ":";
        if (trimmed.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(namePrefix.Length).TrimStart();
        return text;
    }

    private void StartThinking()
    {
        StopThinking();
        if (_thinkingFrames != null && _thinkingFrames.Length > 1)
            _thinkingRoutine = StartCoroutine(AnimateThinking());
    }

    private void StopThinking()
    {
        if (_thinkingRoutine == null) return;
        StopCoroutine(_thinkingRoutine);
        _thinkingRoutine = null;
    }

    private IEnumerator AnimateThinking()
    {
        int frameIndex = 0;
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(Mathf.Max(0.05f, _thinkingFrameTime));
        while (true)
        {
            if (_characterBox != null)
                _characterBox.SetText(GetThinkingFrame(frameIndex));
            frameIndex = (frameIndex + 1) % _thinkingFrames.Length;
            yield return wait;
        }
    }

    private string GetThinkingFrame(int index)
    {
        if (_thinkingFrames == null || _thinkingFrames.Length == 0) return "...";
        return _thinkingFrames[Mathf.Clamp(index, 0, _thinkingFrames.Length - 1)];
    }

    private void EnableInput(bool clearText)
    {
        _playerInput.interactable = true;
        if (clearText)
            _playerInput.text = string.Empty;
        _playerInput.ActivateInputField();
    }

    // Room clicks are blocked before her first reply, and optionally while she replies.
    private void SetRoomLocked(bool locked)
    {
        if (_gameManager == null) return;
        bool shouldLock = locked || (_isActive && !_isFinishing && !_hasHadFirstReply);
        _gameManager.SetRoomInteractionLocked(shouldLock);
    }

    private void CancelFinishTimer()
    {
        if (_finishTimer != null && !_finishTimer.isDone)
            _finishTimer.Cancel();
        _finishTimer = null;
    }

    private void CancelPendingActivation()
    {
        if (_pendingActivation != null)
        {
            StopCoroutine(_pendingActivation);
            _pendingActivation = null;
        }
    }
}
