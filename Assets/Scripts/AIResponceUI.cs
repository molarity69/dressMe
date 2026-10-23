using UnityEngine;
using LLMUnity;
using TMPro;  // Import TextMesh Pro namespace
using UnityEngine.UI;

namespace LLMUnitySamples
{
    public class AIResponseUI : MonoBehaviour
    {
        public LLMCharacter llmCharacter;   // Reference to the LLMCharacter component
        public TMP_InputField playerText;    // TextMesh Pro input field where the player types
        public TMP_Text AIText;              // TextMesh Pro UI element where AI's response is displayed

        void Start()
        {
            // Set up listener for when the player submits their message
            playerText.onSubmit.AddListener(onInputFieldSubmit);
            playerText.Select();  // Auto-select the input field
        }

        // This function is called when the player submits their input via Enter key
        void onInputFieldSubmit(string message)
        {
            // Disable the input field while processing the message
            playerText.interactable = false;

            // Display a loading indicator or placeholder text
            AIText.text = "...";

            // Send the player’s message to the LLM for a response
            _ = llmCharacter.Chat(message, SetAIText, AIReplyComplete);
        }

        // This method is called when the AI returns its response
        public void SetAIText(string text)
        {
            // Update the AI's response in the UI
            AIText.text = text;
        }

        // This method is called when the AI has finished replying and the input field is ready again
        public void AIReplyComplete()
        {
            // Re-enable the input field to allow further interaction
            playerText.interactable = true;
            playerText.Select();  // Re-select the input field
            playerText.text = "";  // Clear the input field after a response
        }

        // This method is used to cancel ongoing requests if needed
        public void CancelRequests()
        {
            llmCharacter.CancelRequests();
            AIReplyComplete();  // Complete the AI's reply handling
        }

        // Function for handling the exit button in the UI
        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        // This function checks if the LLMCharacter is correctly configured and shows a warning if not
        bool onValidateWarning = true;
        void OnValidate()
        {
            if (onValidateWarning && !llmCharacter.remote && llmCharacter.llm != null && llmCharacter.llm.model == "")
            {
                Debug.LogWarning($"Please select a model in the {llmCharacter.llm.gameObject.name} GameObject!");
                onValidateWarning = false;
            }
        }
    }
}
