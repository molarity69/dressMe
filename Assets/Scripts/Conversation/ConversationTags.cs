using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public enum CharacterMood
{
    Neutral,
    Happy,
    Sad,
    Upset,
    Confused
}

/// <summary>
/// Tag format shared by the prompt and the reply parser, so the rules the LLM is given
/// and the rules the game reads can never drift apart.
/// The parser is lenient, because small models don't follow the format exactly:
/// - anything in [square brackets] is removed (she never says those out loud);
/// - stage directions in (parentheses), {braces} or *asterisks* are removed;
/// - a bare tag at the start ("HAPPY: ...") is removed;
/// - mood words found in those are counted, including common invented ones ([UNCOMFORTABLE] counts as Sad).
/// </summary>
public static class ConversationTags
{
    public const string GoalTag = "AGREED";

    // The mood list is built from the CharacterMood enum, so adding a mood there is all the prompt needs.
    private static readonly string MoodRule =
        "Start every reply with exactly one mood tag in square brackets that matches how you feel: " + BuildMoodTagList() + ".";

    // Moods the model invents instead of the ones it was given, mapped to the closest real mood.
    private static readonly Dictionary<string, CharacterMood> MoodSynonyms = new Dictionary<string, CharacterMood>(StringComparer.OrdinalIgnoreCase)
    {
        { "content", CharacterMood.Happy }, { "calm", CharacterMood.Happy }, { "relaxed", CharacterMood.Happy },
        { "comfortable", CharacterMood.Happy }, { "glad", CharacterMood.Happy }, { "cheerful", CharacterMood.Happy },
        { "pleased", CharacterMood.Happy }, { "better", CharacterMood.Happy }, { "excited", CharacterMood.Happy },
        { "joyful", CharacterMood.Happy }, { "grateful", CharacterMood.Happy }, { "thankful", CharacterMood.Happy },

        { "uncomfortable", CharacterMood.Sad }, { "tired", CharacterMood.Sad }, { "lonely", CharacterMood.Sad },
        { "worried", CharacterMood.Sad }, { "anxious", CharacterMood.Sad }, { "nervous", CharacterMood.Sad },
        { "down", CharacterMood.Sad }, { "unhappy", CharacterMood.Sad }, { "sleepy", CharacterMood.Sad },

        { "angry", CharacterMood.Upset }, { "annoyed", CharacterMood.Upset }, { "frustrated", CharacterMood.Upset },
        { "scared", CharacterMood.Upset }, { "afraid", CharacterMood.Upset }, { "stressed", CharacterMood.Upset },
        { "overwhelmed", CharacterMood.Upset }, { "distressed", CharacterMood.Upset }, { "agitated", CharacterMood.Upset },

        { "unsure", CharacterMood.Confused }, { "puzzled", CharacterMood.Confused }, { "lost", CharacterMood.Confused },
        { "uncertain", CharacterMood.Confused },

        { "okay", CharacterMood.Neutral }, { "ok", CharacterMood.Neutral }, { "fine", CharacterMood.Neutral }
    };

    // Words a model adds around a tag, e.g. [Mood: Happy] or (feeling happy).
    private static readonly HashSet<string> FillerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mood", "feeling", "feels", "feel", "emotion", "tag", "state", "i", "am", "is", "very", "a", "bit", "little", "slightly", "more"
    };

    private static readonly HashSet<string> TagWords = BuildTagWords();

    // [ ... ] : always removed. ( ... ), { ... }, * ... * : stage directions, always removed.
    private static readonly Regex SquareChunkRegex = new Regex(@"\[([^\[\]]{0,80})\]", RegexOptions.Compiled);
    private static readonly Regex StageDirectionRegex = new Regex(
        @"\(([^\(\)]*)\)|\{([^\{\}]*)\}|\*+([^\*]+?)\*+",
        RegexOptions.Compiled);

    // A bare tag at the very start, e.g. "HAPPY: Hello" or "Mood - Sad\nHello".
    private static readonly Regex LeadingBareTagRegex = new Regex(
        @"^\s*(?:mood\s*[:\-]\s*)?([A-Za-z]+)\s*(?:[:\-\u2013\u2014]|\r?\n)\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Something still being streamed at the end: an opened bracket or asterisk that isn't closed yet.
    private static readonly Regex UnclosedAtEndRegex = new Regex(@"(?:\[[^\]]*|\([^\)]*|\{[^\}]*|\*[^\*]{0,40})$", RegexOptions.Compiled);
    private static readonly Regex EmptyAsterisksRegex = new Regex(@"\*+\s*\*+", RegexOptions.Compiled);

    private static readonly Regex WordRegex = new Regex(@"[A-Za-z]+", RegexOptions.Compiled);
    private static readonly Regex LeftoverMarksRegex = new Regex(@"\*+|\[\s*\]|\(\s*\)|\{\s*\}", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex BlankLinesRegex = new Regex(@"\s*\n\s*", RegexOptions.Compiled);
    private static readonly Regex LeadingPunctuationRegex = new Regex(@"^[\s:\-\u2013\u2014,]+", RegexOptions.Compiled);

    /// <summary>
    /// Tag rules for the prompt. agreementCondition is optional, e.g. "you feel comfortable and agree to get dressed";
    /// when empty, the [AGREED] rule is left out.
    /// </summary>
    public static string BuildFormatRules(string agreementCondition)
    {
        if (string.IsNullOrWhiteSpace(agreementCondition))
            return MoodRule;

        return MoodRule + " Only when " + agreementCondition.Trim() + ", add [" + GoalTag + "] at the very end of your reply.";
    }

    /// <summary>Removes all tags and stage directions, including a half-streamed one at the end, for display.</summary>
    public static string StripTags(string text)
    {
        List<string> foundTags = new List<string>();
        return Parse(text, foundTags);
    }

    /// <summary>Finds the first tag that names a mood (or a known synonym). Small models sometimes skip it, so this can fail.</summary>
    public static bool TryGetMood(string text, out CharacterMood mood)
    {
        mood = CharacterMood.Neutral;
        List<string> foundTags = new List<string>();
        Parse(text, foundTags);

        foreach (string tag in foundTags)
        {
            if (TryMapMood(tag, out mood))
                return true;
        }
        mood = CharacterMood.Neutral;
        return false;
    }

    public static bool HasGoalTag(string text)
    {
        List<string> foundTags = new List<string>();
        Parse(text, foundTags);

        foreach (string tag in foundTags)
        {
            if (string.Equals(tag, GoalTag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryMapMood(string word, out CharacterMood mood)
    {
        if (Enum.TryParse(word, true, out mood) && Enum.IsDefined(typeof(CharacterMood), mood))
            return true;
        return MoodSynonyms.TryGetValue(word, out mood);
    }

    // Returns the display text and fills foundTags (mood words / AGREED) in the order they appear.
    private static string Parse(string text, List<string> foundTags)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string remaining = text;

        // Bare tag at the start, e.g. "Happy: ..." or "UNCOMFORTABLE: ...". Synonyms only count when shouted,
        // so natural openings like "Okay - I'll try" or "Better, thank you" are left alone.
        Match leadingMatch = LeadingBareTagRegex.Match(remaining);
        if (leadingMatch.Success)
        {
            string word = leadingMatch.Groups[1].Value;
            bool isMainTag = string.Equals(word, GoalTag, StringComparison.OrdinalIgnoreCase)
                || (Enum.TryParse(word, true, out CharacterMood parsedMood) && Enum.IsDefined(typeof(CharacterMood), parsedMood));
            bool isShoutedTag = word.Length >= 3 && word == word.ToUpperInvariant();
            if (isMainTag || isShoutedTag)
            {
                if (TagWords.Contains(word)) foundTags.Add(word);
                remaining = remaining.Substring(leadingMatch.Length);
            }
        }

        // Square brackets: always removed; any tag word inside counts.
        remaining = SquareChunkRegex.Replace(remaining, match =>
        {
            foreach (Match word in WordRegex.Matches(match.Groups[1].Value))
            {
                if (TagWords.Contains(word.Value))
                    foundTags.Add(word.Value);
            }
            return string.Empty;
        });

        // Asterisks left around a removed tag, e.g. "**[HAPPY]**" -> "****".
        remaining = EmptyAsterisksRegex.Replace(remaining, string.Empty);

        // Stage directions: always removed; counted only when they are just a mood, e.g. (Happy) or *feeling sad*.
        remaining = StageDirectionRegex.Replace(remaining, match =>
        {
            List<string> tagsInChunk = new List<string>();
            if (IsTagContent(GetChunkContent(match), tagsInChunk))
                foundTags.AddRange(tagsInChunk);
            return " ";
        });

        // While streaming: hide anything still being written.
        remaining = UnclosedAtEndRegex.Replace(remaining, string.Empty);
        if (IsStartOfBareTag(remaining))
            return string.Empty;

        remaining = LeftoverMarksRegex.Replace(remaining, string.Empty);
        remaining = MultiSpaceRegex.Replace(remaining, " ");
        remaining = BlankLinesRegex.Replace(remaining, "\n");
        remaining = LeadingPunctuationRegex.Replace(remaining, string.Empty);
        return remaining.Trim();
    }

    private static string GetChunkContent(Match match)
    {
        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success)
                return match.Groups[i].Value;
        }
        return string.Empty;
    }

    // Content is a tag when every word is a tag word or filler, and at least one is a tag word.
    private static bool IsTagContent(string content, List<string> tagsInChunk)
    {
        MatchCollection words = WordRegex.Matches(content);
        if (words.Count == 0) return false;

        foreach (Match word in words)
        {
            string value = word.Value;
            if (TagWords.Contains(value))
                tagsInChunk.Add(value);
            else if (!FillerWords.Contains(value))
                return false;
        }
        return tagsInChunk.Count > 0;
    }

    // The whole reply so far is one unfinished word that could become a bare tag, e.g. "HAPP".
    private static bool IsStartOfBareTag(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 14) return false;

        foreach (char character in trimmed)
        {
            if (!char.IsLetter(character)) return false;
        }

        foreach (string tagWord in TagWords)
        {
            if (tagWord.Length > trimmed.Length && tagWord.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static HashSet<string> BuildTagWords()
    {
        HashSet<string> words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string moodName in Enum.GetNames(typeof(CharacterMood)))
            words.Add(moodName);
        foreach (string synonym in MoodSynonyms.Keys)
            words.Add(synonym);
        words.Add(GoalTag);
        return words;
    }

    private static string BuildMoodTagList()
    {
        string[] moodNames = Enum.GetNames(typeof(CharacterMood));
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < moodNames.Length; i++)
        {
            if (i > 0)
                builder.Append(i == moodNames.Length - 1 ? " or " : ", ");
            builder.Append('[').Append(moodNames[i].ToUpperInvariant()).Append(']');
        }
        return builder.ToString();
    }
}
