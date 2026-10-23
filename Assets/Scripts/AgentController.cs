using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using TMPro;
using System.IO;

public class AgentMove : Agent
{
    // Pellet Variables
    [SerializeField] private Transform target;
    public int pelletCount;
    public GameObject food;
    [SerializeField] private List<GameObject> spawnedPelletsList = new List<GameObject>();

    // Agent Variables
    [SerializeField] private float movespeed = 4f;
    private Rigidbody rb;
    private Vector3 lastPosition;
    private Vector3 lastVelocityDirection;

    // Environment Variables
    [SerializeField] private Transform environmentLocation;
    private Material envMaterial;
    public GameObject env;

    // Timer and Run Tracking
    private float episodeTimer = 0f;
    private float bestTime = float.MaxValue;
    private bool isEpisodeRunning = false;
    private int runNumber = 0;

    // Episode Counters
    private int pelletsCollected = 0;
    private int wallHits = 0;

    // UI
    public TextMeshProUGUI infoText;

    // --- RENAME SYSTEM VARIABLES TO PREVENT DUPLICATION ERRORS ---
    private string targetJsonPath;
    private AgentTelemetryWrapper balanceTelemetrySession;
    private string globalSessionId;
    private string gameInstanceId;

    // Telemetry aggregators per episode
    private int totalMovesInEpisode = 0;
    private int backtrackCountInEpisode = 0;
    private int decisionCyclesInEpisode = 0;
    private float lastActionTimeStamp = 0f;

    // Predefined Pellet Locations
    private readonly Vector3[] fixedPelletLocations = new Vector3[] {
        new Vector3(3f, 0.03f, 0f),
        new Vector3(-3f, 0.03f, 0f),
        new Vector3(0f, 0.03f, 3.5f),
        new Vector3(0f, 0.03f, -3.5f)
    };

    private void Awake()
    {
        // Output file directly to your Assets folder
        targetJsonPath = Path.Combine(Application.dataPath, "GameBalanceTelemetry.json");

        // Generate unique IDs for this specific game launch
        globalSessionId = "session_" + UnityEngine.Random.Range(1000, 9999);
        gameInstanceId = "level_1_attempt_1";

        balanceTelemetrySession = new AgentTelemetryWrapper();
        balanceTelemetrySession.gameplay_instance = new AgentInstanceData
        {
            instance_id = gameInstanceId,
            start_condition = "player enters level",
            end_condition = "goal reached or timeout",
            success_condition = "player reaches exit",
            failure_condition = "timeout or explicit failure",
            telemetry_family = "pathfinding_basic"
        };
    }

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        envMaterial = env.GetComponent<Renderer>().material;
        bestTime = float.MaxValue;
        UpdateUIText();
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(UnityEngine.Random.Range(-4f, 4f), 0.3f, UnityEngine.Random.Range(-4f, 4f));

        // Reset tracking metrics for the new trial
        episodeTimer = 0f;
        lastActionTimeStamp = 0f;
        isEpisodeRunning = true;
        pelletsCollected = 0;
        wallHits = 0;
        totalMovesInEpisode = 0;
        backtrackCountInEpisode = 0;
        decisionCyclesInEpisode = 0;

        lastPosition = transform.position;
        lastVelocityDirection = transform.forward;

        CreatePellet();
    }

    private void Update()
    {
        if (isEpisodeRunning)
        {
            episodeTimer += Time.deltaTime;
        }
    }

    private void CreatePellet()
    {
        if (spawnedPelletsList.Count != 0)
        {
            RemovePellet(spawnedPelletsList);
        }

        for (int i = 0; i < Mathf.Min(pelletCount, fixedPelletLocations.Length); i++)
        {
            GameObject newPellet = Instantiate(food);
            newPellet.transform.parent = environmentLocation;
            newPellet.transform.localPosition = fixedPelletLocations[i];
            spawnedPelletsList.Add(newPellet);
        }
    }

    private void RemovePellet(List<GameObject> toBeDeletedGameObjectList)
    {
        foreach (GameObject i in toBeDeletedGameObjectList)
        {
            Destroy(i.gameObject);
        }
        toBeDeletedGameObjectList.Clear();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isEpisodeRunning) return;

        float moveRotate = actions.ContinuousActions[0];
        float moveForward = actions.ContinuousActions[1];

        // Calculate Decision Time
        float currentStepTime = episodeTimer;
        lastActionTimeStamp = currentStepTime;

        // Track standard movement outputs
        rb.MovePosition(transform.position + transform.forward * moveForward * movespeed * Time.deltaTime);
        transform.Rotate(0f, moveRotate * movespeed, 0f, Space.Self);

        decisionCyclesInEpisode++;

        // If the agent made a conscious movement input
        if (Mathf.Abs(moveForward) > 0.05f || Mathf.Abs(moveRotate) > 0.05f)
        {
            totalMovesInEpisode++;

            // Backtrack Engine: Check if current velocity vector opposes previous direction
            Vector3 currentVelocityDirection = (transform.position - lastPosition).normalized;
            if (currentVelocityDirection != Vector3.zero && lastVelocityDirection != Vector3.zero)
            {
                float directionalDotProduct = Vector3.Dot(currentVelocityDirection, lastVelocityDirection);
                if (directionalDotProduct < -0.3f)
                {
                    backtrackCountInEpisode = 1;
                }
                lastVelocityDirection = currentVelocityDirection;
            }
        }
        lastPosition = transform.position;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxisRaw("Horizontal");
        continuousActions[1] = Input.GetAxisRaw("Vertical");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Pellet")
        {
            pelletsCollected++;
            spawnedPelletsList.Remove(other.gameObject);
            Destroy(other.gameObject);
            AddReward(10f);

            if (spawnedPelletsList.Count == 0)
            {
                isEpisodeRunning = false;
                AddReward(5f);

                runNumber++;
                string message = $"Last Run: {episodeTimer:F2} s";

                if (episodeTimer < bestTime)
                {
                    bestTime = episodeTimer;
                    message += " - New Best Score!";
                }

                Debug.Log(message);
                UpdateUIText();

                // Compile automated run metrics
                CompileAndRegisterEvent(successFlag: 1);

                StartCoroutine(BlinkEnvironmentColor(Color.green));
            }
        }

        if (other.gameObject.tag == "Wall")
        {
            wallHits++;
            isEpisodeRunning = false;
            AddReward(-15f);
            runNumber++;

            // Compile automated run metrics
            CompileAndRegisterEvent(successFlag: 0);

            StartCoroutine(BlinkEnvironmentColor(Color.red));
        }
    }

    private IEnumerator BlinkEnvironmentColor(Color blinkColor)
    {
        Color originalColor = envMaterial.color;

        for (int i = 0; i < 2; i++)
        {
            envMaterial.color = blinkColor;
            yield return new WaitForSeconds(0.25f);
            envMaterial.color = originalColor;
            yield return new WaitForSeconds(0.25f);
        }

        yield return new WaitForSeconds(1.0f);
        EndEpisode();
    }

    private void UpdateUIText()
    {
        string bestTimeText = bestTime == float.MaxValue ? "N/A" : $"{bestTime:F2} s";
        infoText.text = $"Last Run: {episodeTimer:F2} s\nBest Time: {bestTimeText}";
    }

    private void CompileAndRegisterEvent(int successFlag)
    {
        if (balanceTelemetrySession == null || balanceTelemetrySession.events == null) return;

        int averageDecisionTimeMs = decisionCyclesInEpisode > 0
            ? (int)((episodeTimer * 1000f) / decisionCyclesInEpisode)
            : 0;

        int timeoutFlagValue = (episodeTimer > 30f) ? 1 : 0;

        // Uses unique class names here to stop the red compilation errors
        AgentPathfindingEvent episodeTelemetryRecord = new AgentPathfindingEvent
        {
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            session_id = globalSessionId,
            player_id = "agent_optimal",
            game_id = "pathfinding_demo",
            level_id = "fixed_level",
            instance_id = gameInstanceId,
            event_type = (successFlag == 1) ? "success_end" : "failure_end",
            decision_time_ms = averageDecisionTimeMs,
            was_backtracked = backtrackCountInEpisode,
            completion_time_ms = (int)(episodeTimer * 1000f),
            moves = totalMovesInEpisode,
            timeout_flag = timeoutFlagValue,
            nodes_expanded = decisionCyclesInEpisode,
            success_flag = successFlag
        };

        balanceTelemetrySession.events.Add(episodeTelemetryRecord);
    }

    private void OnDisable()
    {
        if (balanceTelemetrySession != null && balanceTelemetrySession.events.Count > 0)
        {
            string cleanJsonOutput = JsonUtility.ToJson(balanceTelemetrySession, true);

            // Clean up JSON naming keys back to your exact expected schema names
            cleanJsonOutput = cleanJsonOutput.Replace("AgentPathfindingEvent", "PathfindingEvent");

            File.WriteAllText(targetJsonPath, cleanJsonOutput);
            Debug.Log($"<color=lime><b>[Telemetry Complete]</b></color> Saved custom layout directly to: {targetJsonPath}");
        }
    }
}

// --- UNIQUE CLASS NAMES AT THE BOTTOM TO PREVENT CLASHES ---

[Serializable]
public class AgentTelemetryWrapper
{
    public AgentInstanceData gameplay_instance;
    public List<AgentPathfindingEvent> events = new List<AgentPathfindingEvent>();
}

[Serializable]
public class AgentInstanceData
{
    public string instance_id;
    public string start_condition;
    public string end_condition;
    public string success_condition;
    public string failure_condition;
    public string telemetry_family;
}

[Serializable]
public class AgentPathfindingEvent
{
    public string timestamp;
    public string session_id;
    public string player_id;
    public string game_id;
    public string level_id;
    public string instance_id;
    public string event_type;
    public int decision_time_ms;
    public int was_backtracked;
    public int completion_time_ms;
    public int moves;
    public int timeout_flag;
    public int nodes_expanded;
    public int success_flag;
}