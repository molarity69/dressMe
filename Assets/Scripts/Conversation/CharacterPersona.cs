using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public class ExampleExchange
{
    [TextArea(1, 3)] public string PlayerLine;
    [Tooltip("Her reply, including the mood tag, e.g. '[SAD] It's dark. I don't like the dark.'")]
    [TextArea(1, 3)] public string Reply;
}

/// <summary>
/// Who the character is. Identity comes from a profile in the NPC Interactions package's JSON format
/// (read with the package's Profile / ProfileList classes); example exchanges show the model her voice.
/// The situation of a particular scene lives in a ConversationBeat.
/// </summary>
[CreateAssetMenu(fileName = "New Persona", menuName = "Conversation/Character Persona")]
public class CharacterPersona : ScriptableObject
{
    [Header("Profile (NPC Interactions package format)")]
    [SerializeField] private TextAsset _profileJson;
    [SerializeField] private string _characterId = "";

    [Header("Conversation")]
    [SerializeField] private string _playerName = "Carer";
    [Tooltip("Short examples of how she talks. Small models copy examples far better than they follow descriptions.")]
    [SerializeField] private List<ExampleExchange> _exampleExchanges = new List<ExampleExchange>();

    private Profile _cachedProfile;

    public string CharacterName
    {
        get
        {
            Profile profile = GetProfile();
            return profile != null ? profile.name : name;
        }
    }

    public string PlayerName => _playerName;

    /// <summary>Profile fields plus example exchanges. The beat adds the situation and rules.</summary>
    public string BuildIdentityPrompt()
    {
        Profile profile = GetProfile();
        StringBuilder builder = new StringBuilder();

        if (profile != null)
        {
            builder.AppendLine($"You are {profile.name}, {profile.role}.");
            AppendLabelled(builder, "Background", profile.background);
            AppendLabelled(builder, "Personality", profile.personality);
            AppendLabelled(builder, "Goals", profile.goals);
            AppendLabelled(builder, "Dialogue style", profile.dialogue_style);
        }

        if (_exampleExchanges.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Examples of how you talk (only examples; never repeat them word for word):");
            foreach (ExampleExchange example in _exampleExchanges)
            {
                builder.AppendLine($"{_playerName}: {example.PlayerLine.Trim()}");
                builder.AppendLine($"{CharacterName}: {example.Reply.Trim()}");
            }
        }

        return builder.ToString().Trim();
    }

    [ContextMenu("Log Identity Prompt")]
    private void LogIdentityPrompt()
    {
        _cachedProfile = null;
        Debug.Log($"[{name}] Identity prompt:\n{BuildIdentityPrompt()}", this);
    }

    private void OnValidate()
    {
        // Re-read the JSON after inspector edits.
        _cachedProfile = null;
    }

    private Profile GetProfile()
    {
        if (_cachedProfile != null) return _cachedProfile;

        if (_profileJson == null)
        {
            Debug.LogError($"CharacterPersona '{name}': no Profile Json assigned.", this);
            return null;
        }

        ProfileList profileList = JsonUtility.FromJson<ProfileList>(_profileJson.text);
        if (profileList == null || profileList.profiles == null)
        {
            Debug.LogError($"CharacterPersona '{name}': could not read profiles from {_profileJson.name}.", this);
            return null;
        }

        foreach (Profile profile in profileList.profiles)
        {
            if (profile.character_id == _characterId)
            {
                _cachedProfile = profile;
                return _cachedProfile;
            }
        }

        Debug.LogError($"CharacterPersona '{name}': character id '{_characterId}' not found in {_profileJson.name}.", this);
        return null;
    }

    private static void AppendLabelled(StringBuilder builder, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        builder.AppendLine($"{label}: {value.Trim()}");
    }
}
