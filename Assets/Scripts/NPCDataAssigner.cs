using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using LLMUnity;

[Serializable]
public class Profile
{
    public string character_id;
    public string name;
    public string role;
    public string background;
    public string personality;
    public string goals;
    public string dialogue_style;
}

[Serializable]
public class ProfileList
{
    public Profile[] profiles;
}

public class NPCDataAssigner : MonoBehaviour
{
    [Header("JSON File")]
    public TextAsset jsonFile;

    [Header("NPC GameObjects (drag in order)")]
    public List<GameObject> npcObjects;

    void Start()
    {
        if (jsonFile == null)
        {
            Debug.LogError("JSON file not assigned in the Inspector!");
            return;
        }

        if (npcObjects == null || npcObjects.Count == 0)
        {
            Debug.LogError("No NPC GameObjects assigned in the Inspector!");
            return;
        }

        // Deserialize JSON
        ProfileList profileList = JsonConvert.DeserializeObject<ProfileList>(jsonFile.text);

        int count = Mathf.Min(profileList.profiles.Length, npcObjects.Count);

        for (int i = 0; i < count; i++)
        {
            Profile profile = profileList.profiles[i];
            GameObject npcGO = npcObjects[i];

            // Build a paragraph for this NPC
            string paragraph = $"{profile.character_id} ->Name: {profile.name},Role: {profile.role}, Background: {profile.background}, Personality: {profile.personality}, Goals: {profile.goals}, Dialogue Style: {profile.dialogue_style}";

            // Safely assign LLMCharacter info
            LLMCharacter llmChar = npcGO.GetComponent<LLMCharacter>();
            if (llmChar != null)
            {
                llmChar.name = profile.name;
                llmChar.AIName = profile.name;
                llmChar.SetPrompt(paragraph);
            }
            else
            {
                Debug.LogWarning($"{npcGO.name} is missing an LLMCharacter component!");
            }
            //Debug.Log(paragraph);
        }
    }
}
