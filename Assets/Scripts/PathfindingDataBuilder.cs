using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;

[CreateAssetMenu(fileName = "PathfindingData", menuName = "Balance Tool/New Pathfinding Session")]
public class PathfindingDataBuilder : ScriptableObject
{
    [Header("1. Game Instance Metadata")]
    public string instance_id = "level_1_attempt_1";
    public string start_condition = "player enters level";
    public string end_condition = "goal reached or timeout";
    public string success_condition = "player reaches exit";
    public string failure_condition = "timeout or explicit failure";
    public string telemetry_family = "pathfinding_basic";

    [Header("2. Gameplay Events")]
    public List<PathfindingEvent> events = new List<PathfindingEvent>();

    [ContextMenu("Export JSON to Console")]
    public void ExportToConsole()
    {
        string json = JsonUtility.ToJson(new Wrapper { gameplay_instance = this, events = events }, true);
        Debug.Log("<color=cyan><b>Generated JSON:</b></color>\n" + json);
    }

    [ContextMenu("Save to File")]
    public void SaveToFile()
    {
        string json = JsonUtility.ToJson(new Wrapper { gameplay_instance = this, events = events }, true);
        string path = Path.Combine(Application.dataPath, instance_id + ".json");
        File.WriteAllText(path, json);
        Debug.Log($"JSON saved to: {path}");
    }

    // This wrapper ensures the JSON root matches your API's expected format
    [Serializable]
    private class Wrapper
    {
        public PathfindingDataBuilder gameplay_instance;
        public List<PathfindingEvent> events;
    }
}

[Serializable]
public class PathfindingEvent
{
    public string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    public string event_type = "action";
    public int moves;
    public int nodes_expanded;
    public int success_flag;
    // Add other fields from your API template here...
}