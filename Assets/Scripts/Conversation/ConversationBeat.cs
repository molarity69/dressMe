using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class MoodComfortDelta
{
    public CharacterMood Mood;
    public int Delta;
}

[Serializable]
public class ComfortBand
{
    [Tooltip("This band applies from this comfort value upwards (0–100).")]
    public int MinComfort;
    [Tooltip("How she feels at this level. Added to her prompt, so write it to her as 'You ...'.")]
    [TextArea(2, 4)] public string Feeling;
}

[Serializable]
public class RoomIssue
{
    public HotspotID Hotspot;
    [Tooltip("A subtle hint about how this makes her feel, without naming the object. Sent to her when the game decides she should hint, e.g. 'the room feels too bright and busy, and it makes you tired'.")]
    [FormerlySerializedAs("Worry")]
    [TextArea(2, 3)] public string Hint;
    [Tooltip("Sent to her when the player fixes it, e.g. '(The carer opens the curtains.)'")]
    [TextArea(1, 2)] public string ActionNote;
    public int ComfortGain = 15;
    [Tooltip("Shown on the feedback card if the player never fixed it.")]
    [TextArea(1, 2)] public string MissedFeedback;
}

/// <summary>
/// One conversation moment in the story (e.g. Day 1 "what's bothering her"): who it is with,
/// the situation, what in the room matters, how comfort works, and how it ends.
/// </summary>
[CreateAssetMenu(fileName = "New Conversation Beat", menuName = "Conversation/Conversation Beat")]
public class ConversationBeat : ScriptableObject
{
    [Header("Character")]
    [SerializeField] private CharacterPersona _persona;

    [Header("Scene")]
    [TextArea(3, 8)] [SerializeField] private string _scenario = "";
    [TextArea(2, 4)] [SerializeField] private string _openingLine = "";

    [Header("Goal")]
    [SerializeField] private int _startComfort = 20;
    [SerializeField] private int _calmThreshold = 80;
    [Tooltip("Optional. If set, she must also add the agreement tag, e.g. 'you feel comfortable and agree to get dressed'. Leave empty when calm alone ends the beat.")]
    [TextArea(1, 3)] [SerializeField] private string _agreementCondition = "";

    [Header("Comfort")]
    [SerializeField] private List<MoodComfortDelta> _moodDeltas = new List<MoodComfortDelta>();
    [SerializeField] private List<ComfortBand> _comfortBands = new List<ComfortBand>();

    [Header("Room")]
    [SerializeField] private List<RoomIssue> _roomIssues = new List<RoomIssue>();

    [Header("What She Talks About")]
    [Tooltip("Chance (0–1) that her reply to a player line hints at an unfixed room issue. Otherwise she shares one of her random thoughts.")]
    [Range(0f, 1f)] [SerializeField] private float _hintChance = 0.4f;
    [Tooltip("Her first reply always hints, so the player has somewhere to start.")]
    [SerializeField] private bool _firstReplyHints = true;
    [Tooltip("Things on her mind that have nothing to do with the room, e.g. 'the cartoon with the funny dog you were watching'.")]
    [SerializeField] private List<string> _randomThoughts = new List<string>();

    [Header("Flow")]
    [SerializeField] private int _maxTurns = 10;
    [SerializeField] private GameState _nextState = GameState.Narrative;
    [Tooltip("On success, end like the original level: stop rocking and show the chair's happy pose.")]
    [SerializeField] private bool _stopRockingOnCalm = true;

    [Header("Fallback")]
    [Tooltip("Said by her when the AI gives no usable reply (timeout, error or an empty answer). Counts as no change in comfort.")]
    [SerializeField] private List<string> _fallbackLines = new List<string>
    {
        "Mmm...",
        "I don't know...",
        "Hmm. I'm a bit tired.",
        "Mm-hm."
    };

    [Header("Feedback")]
    [TextArea(2, 3)] [SerializeField] private string _calmByTalkingFeedback = "";
    [TextArea(2, 3)] [SerializeField] private string _calmByRoomFeedback = "";
    [TextArea(1, 2)] [SerializeField] private string _allIssuesFixedFeedback = "";
    [TextArea(1, 2)] [SerializeField] private string _missedIssuesHeader = "";
    [TextArea(2, 3)] [SerializeField] private string _outOfTurnsFeedback = "";

    public CharacterPersona Persona => _persona;
    public string OpeningLine => _openingLine;
    public int StartComfort => _startComfort;
    public int CalmThreshold => _calmThreshold;
    public bool RequiresAgreement => !string.IsNullOrWhiteSpace(_agreementCondition);
    public IReadOnlyList<RoomIssue> RoomIssues => _roomIssues;
    public float HintChance => _hintChance;
    public bool FirstReplyHints => _firstReplyHints;
    public IReadOnlyList<string> RandomThoughts => _randomThoughts;
    public int MaxTurns => _maxTurns;
    public GameState NextState => _nextState;
    public bool StopRockingOnCalm => _stopRockingOnCalm;

    /// <summary>A random fallback line, avoiding the previous one when there is a choice.</summary>
    public string GetFallbackLine(string previousLine)
    {
        if (_fallbackLines.Count == 0) return "Mmm...";
        if (_fallbackLines.Count == 1) return _fallbackLines[0];

        string line = _fallbackLines[UnityEngine.Random.Range(0, _fallbackLines.Count)];
        while (line == previousLine)
            line = _fallbackLines[UnityEngine.Random.Range(0, _fallbackLines.Count)];
        return line;
    }

    public int GetMoodDelta(CharacterMood mood)
    {
        foreach (MoodComfortDelta moodDelta in _moodDeltas)
        {
            if (moodDelta.Mood == mood)
                return moodDelta.Delta;
        }
        return 0;
    }

    /// <summary>The band with the highest MinComfort that the value has reached, or null if none.</summary>
    public ComfortBand GetBand(int comfort)
    {
        ComfortBand bestBand = null;
        foreach (ComfortBand band in _comfortBands)
        {
            if (comfort >= band.MinComfort && (bestBand == null || band.MinComfort > bestBand.MinComfort))
                bestBand = band;
        }
        return bestBand;
    }

    /// <summary>
    /// System prompt for the whole beat. It never changes during the conversation, so the model can reuse
    /// what it has already read; changes reach her as notes in parentheses instead (see BuildFeelingNote).
    /// </summary>
    public string BuildPrompt()
    {
        StringBuilder builder = new StringBuilder();

        if (_persona != null)
        {
            builder.AppendLine(_persona.BuildIdentityPrompt());
            builder.AppendLine();
            builder.AppendLine($"You are talking with your {_persona.PlayerName.ToLowerInvariant()}.");
        }

        AppendSection(builder, _scenario);

        ComfortBand startBand = GetBand(_startComfort);
        if (startBand != null)
            AppendSection(builder, $"How you feel at the start: {startBand.Feeling.Trim()}");

        if (_roomIssues.Count > 0)
            AppendSection(builder, "Some things about your room are upsetting you today, but you find it hard to say exactly what they are.");

        AppendSection(builder,
            "Notes in parentheses tell you what the carer does, how you feel, or what is on your mind right now, " +
            "for example (The carer sits down next to you.). Follow them naturally in your own words. " +
            "Never mention the notes, never repeat them word for word, and never write notes in parentheses yourself. " +
            "Never tell the carer exactly what to do or name the thing that needs changing; only say how things make you feel.");
        AppendSection(builder, ConversationTags.BuildFormatRules(_agreementCondition));
        return builder.ToString().Trim();
    }

    /// <summary>Hidden note asking her to hint, subtly, at one unfixed issue.</summary>
    public string BuildHintNote(RoomIssue issue)
    {
        return $"(Something still bothers you: {issue.Hint.Trim()}. Show how it makes you feel, but don't say what to do or name the thing.)";
    }

    /// <summary>Hidden note giving her something else to talk about.</summary>
    public string BuildThoughtNote(string thought)
    {
        return $"(Right now you are thinking about {thought.Trim()}. Talk about that, or answer the carer and then mention it.)";
    }

    /// <summary>Hidden note sent when the player fixes something: react to it, and nothing else.</summary>
    public string BuildActionNote(RoomIssue issue)
    {
        return $"{issue.ActionNote.Trim()} (Just react to this. Don't talk about anything else that bothers you right now.)";
    }

    /// <summary>Note sent to her when her comfort band changes, e.g. "(You are starting to feel a little better.)".</summary>
    public string BuildFeelingNote(ComfortBand band)
    {
        if (band == null || string.IsNullOrWhiteSpace(band.Feeling)) return string.Empty;
        return $"({band.Feeling.Trim()})";
    }

    /// <summary>Feedback card text for the end of the beat.</summary>
    public string BuildFeedback(bool reachedCalm, int comfortFromTalking, int comfortFromRoom, IReadOnlyList<RoomIssue> openIssues)
    {
        StringBuilder builder = new StringBuilder();

        if (reachedCalm)
            builder.AppendLine(comfortFromTalking >= comfortFromRoom ? _calmByTalkingFeedback : _calmByRoomFeedback);
        else
            builder.AppendLine(_outOfTurnsFeedback);

        if (_roomIssues.Count > 0)
        {
            builder.AppendLine();
            if (openIssues.Count == 0)
            {
                builder.AppendLine(_allIssuesFixedFeedback);
            }
            else
            {
                builder.AppendLine(_missedIssuesHeader);
                foreach (RoomIssue issue in openIssues)
                    builder.AppendLine($"• {issue.MissedFeedback}");
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendSection(StringBuilder builder, string section)
    {
        if (string.IsNullOrWhiteSpace(section)) return;
        builder.AppendLine(section.Trim());
        builder.AppendLine();
    }
}
