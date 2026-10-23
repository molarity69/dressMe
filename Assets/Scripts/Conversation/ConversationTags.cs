using System;
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
/// </summary>
public static class ConversationTags
{
    public const string GoalTag = "AGREED";

    // The mood list is built from the CharacterMood enum, so adding a mood there is all the prompt needs.
    private static readonly string MoodRule =
        "Start every reply with exactly one mood tag in square brackets that matches how you feel: " + BuildMoodTagList() + ".";

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

    private static readonly Regex CompleteTagRegex = new Regex(@"\[\s*([A-Za-z]+)\s*\]", RegexOptions.Compiled);
    private static readonly Regex PartialTagAtEndRegex = new Regex(@"\[[A-Za-z\s]*$", RegexOptions.Compiled);

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

    /// <summary>Removes all tags, including a half-streamed one at the end, for display.</summary>
    public static string StripTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string withoutTags = CompleteTagRegex.Replace(text, string.Empty);
        withoutTags = PartialTagAtEndRegex.Replace(withoutTags, string.Empty);
        return withoutTags.Trim();
    }

    /// <summary>Finds the first tag that names a mood. Small models sometimes skip it, so this can fail.</summary>
    public static bool TryGetMood(string text, out CharacterMood mood)
    {
        mood = CharacterMood.Neutral;
        if (string.IsNullOrEmpty(text)) return false;

        foreach (Match match in CompleteTagRegex.Matches(text))
        {
            string tagName = match.Groups[1].Value;
            if (Enum.TryParse(tagName, true, out CharacterMood parsedMood))
            {
                mood = parsedMood;
                return true;
            }
        }
        return false;
    }

    public static bool HasGoalTag(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        foreach (Match match in CompleteTagRegex.Matches(text))
        {
            if (string.Equals(match.Groups[1].Value, GoalTag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
