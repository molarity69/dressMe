using UnityEngine;
using System.Collections.Generic;
using LLMUnity;
using System.Collections;


public class NPCPromptAssigner : MonoBehaviour
{
    [Header("Character JSON Settings")]
    public TextAsset characterJSON;

    [Header("NPC Objects (order matters)")]
    public GameObject[] npcObjects;

    [System.Serializable]
    public class CharacterData
    {
        public string id;
        public string characterName;
        public string role;
        public string world;
        public string background;
        public string personality;
        public string motivation;
    }

    [System.Serializable]
    public class CharacterList
    {
        public List<CharacterData> characters;
    }

    void Start()
    {
        if (characterJSON == null) { Debug.LogError("[NPCPromptAssigner] No JSON file assigned!"); return; }

        CharacterList characterList = JsonUtility.FromJson<CharacterList>(characterJSON.text);
        if (characterList == null || characterList.characters == null) { Debug.LogError("[NPCPromptAssigner] Failed to parse JSON!"); return; }

        StartCoroutine(AssignPrompts(characterList));
    }

    private IEnumerator AssignPrompts(CharacterList characterList)
    {
        // Wait one frame to make sure all LLMCharacter components are initialized
        yield return null;

        int count = Mathf.Min(characterList.characters.Count, npcObjects.Length);

        for (int i = 0; i < count; i++)
        {
            GameObject npc = npcObjects[i];
            if (npc == null) { Debug.LogWarning($"NPC slot {i + 1} is empty."); continue; }

            LLMCharacter llmChar = npc.GetComponent<LLMCharacter>();
            if (llmChar == null) { Debug.LogWarning($"No LLMCharacter on {npc.name}"); continue; }

            CharacterData data = characterList.characters[i];
            llmChar.AIName = data.characterName;
            llmChar.SetPrompt(BuildPrompt(data));

            Debug.Log($"Updated prompt for {npc.name} ({data.characterName})");
        }
    }

    private string BuildPrompt(CharacterData data)
    {
        return $"{data.characterName} is a {data.role} in the world of {data.world}. " +
               $"Background: {data.background}. " +
               $"Personality: {data.personality}. " +
               $"Motivation: {data.motivation}. " +
               $"Interact naturally with the player and stay in character.";
    }
}
