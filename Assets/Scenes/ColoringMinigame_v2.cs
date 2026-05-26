// ============================================================
// ColoringMinigame.cs (v3)
//
// RESPONSIBILITY: Self-contained coloring minigame simulating
// the motor struggle of painting within lines.
//
// KEY CHANGES FROM v2:
//   [1] _resistanceStrength is now a DIRECT pixel offset (not accumulating).
//       At proximity=1.0 (right on the outline), cursor is displaced exactly
//       _resistanceStrength pixels outward. Predictable. Tweakable.
//   [2] Random-interval impulse jumps are GONE.
//       Replaced with a 4-phase cyclic impulse (Inactive→Waiting→MovingOut→Returning).
//   [3] _impulseSpeed added: the outward travel speed in texture px/s.
//   [4] _returnSpeed is now exclusively the return-phase speed (px/s).
//   [5] v2 single-source-of-truth architecture (_effectiveCursorPos) preserved.
//
// CONSUMERS: Standalone testing (MonoBehaviour).
//            Later: wrap in IState for GameStateMachine integration.
// DEPENDS ON: Unity UI (Canvas, RawImage, Image, Text)
//             Two textures: sketch outline + mask
// ============================================================

using UnityEngine;
using UnityEngine.UI;

public class ColoringMinigame_v2 : MonoBehaviour
{
    // ============================================================
    // CONFIGURATION
    // ============================================================

    [Header("Textures (Required)")]
    [Tooltip("Visible sketch outline. Transparent interior, opaque black outline.")]
    [SerializeField] private Texture2D _sketchTexture;

    [Tooltip("Mask: White pixels = valid paint area. Black = outside/outline.")]
    [SerializeField] private Texture2D _maskTexture;

    [Header("Colors (Cycle with RMB)")]
    [SerializeField]
    private Color[] _availableColors = new Color[]
    {
        new Color(1f, 0.15f, 0.15f, 1f),
        new Color(0.15f, 0.6f,  0.15f, 1f),
        new Color(0.15f, 0.3f,  1f,   1f),
    };

    [Header("Brush — Marker Style")]
    [Tooltip("Radius in texture pixels. 12 = medium marker on a 512px canvas.")]
    [SerializeField] private float _brushRadius = 12f;

    [Range(0.1f, 1f)]
    [Tooltip("1 = hard marker edge. 0.1 = soft airbrush. 0.75 = default marker.")]
    [SerializeField] private float _brushHardness = 0.75f;

    [Header("Zone Detection")]
    [Tooltip("Pixel distance from outline that counts as 'near edge'.")]
    [SerializeField] private float _edgeZoneWidth = 30f;

    [Tooltip("Radial sample count for edge detection. 8 = 45° increments.")]
    [SerializeField] private int _edgeSampleDirections = 8;

    [Header("Middle Zone — Curved Drawing (LMB Held)")]
    [Tooltip("Peak perpendicular wave displacement in texture pixels.")]
    [SerializeField] private float _curveAmplitude = 28f;

    [Tooltip("Wave cycles per pixel traveled. 0.015 ≈ 1 full wave per 67px.")]
    [SerializeField] private float _curveFrequency = 0.015f;

    [Header("Edge Zone — Visual Jitter (LMB Released)")]
    [Tooltip("Peak displacement of the cosmetic-only cursor shake when hovering near edge.")]
    [SerializeField] private float _jitterIntensity = 10f;

    [Tooltip("How fast the Perlin noise cycles. Higher = faster shake.")]
    [SerializeField] private float _jitterSpeed = 18f;

    [Header("Edge Zone — Steady Resistance (LMB Held)")]
    [Tooltip(
        "DIRECT pixel offset applied outward while painting near the edge.\n" +
        "At proximity = 1.0 (right on the outline): cursor displaced by THIS many pixels.\n" +
        "At proximity = 0.5 (mid edge zone):         cursor displaced by HALF this.\n" +
        "At proximity = 0.0 (entering edge zone):    no displacement.\n" +
        "Think of it as ambient pen-weight friction. Always present. Always proportional.\n" +
        "Start at 10–20. Above 40 starts to feel unfair."
    )]
    [SerializeField] private float _resistanceStrength = 15f;

    [Header("Edge Zone — Cyclic Impulse (LMB Held)")]
    [Tooltip("Seconds after entering the edge zone (with LMB held) before the first impulse fires.\n" +
             "Gives the player a grace window at the start of each stroke.")]
    [SerializeField] private float _impulseDelayDuration = 1f;

    [Tooltip("Speed (texture px/s) at which the colored cursor moves AWAY during an impulse.\n" +
             "Low = slow creep. High = fast lurch. Try 40–80.")]
    [SerializeField] private float _impulseSpeed = 55f;

    [Tooltip("Maximum distance (texture px) the impulse can push the cursor from the raw mouse.\n" +
             "The cursor will NOT travel beyond this. Try 25–45.")]
    [SerializeField] private float _impulseMagnitude = 30f;

    [Tooltip("Speed (texture px/s) at which the cursor RETURNS to the mouse after peak impulse.\n" +
             "Higher than _impulseSpeed = fast snap back. Lower = slow crawl back.")]
    [SerializeField] private float _returnSpeed = 80f;

    [Header("Progress & Win / Fail")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _completionThreshold = 0.85f;

    [Range(0.01f, 0.5f)]
    [SerializeField] private float _outOfBoundsFailThreshold = 0.15f;

    [Tooltip("How often (seconds) to sample the canvas for progress. Never sample every frame.")]
    [SerializeField] private float _progressSampleInterval = 0.4f;

    [Header("Debug UI")]
    [SerializeField] private bool _showProgressOnScreen = true;

    // ============================================================
    // PRIVATE STATE
    // ============================================================

    // Canvas & rendering
    private Canvas _canvas;
    private RawImage _paintingDisplay;
    private Image _sketchDisplay;
    private Text _debugText;
    private RectTransform _panelRect;
    private Texture2D _canvasTexture;
    private int _canvasWidth;
    private int _canvasHeight;

    // Cursor visual
    private Image _cursorImage;
    private RectTransform _cursorRect;
    private Texture2D _cursorTexture;
    private Vector2 _textureToScreenScale;

    // Mask data — flat array avoids per-pixel allocation
    private Color[] _maskPixels;
    private int _totalMaskPixels;

    // Input state
    private bool _isPainting;
    private int _currentColorIndex;
    private Color _currentColor;
    private float _distanceTraveledThisStroke;

    // WHY: _effectiveCursorPos is the v2 single-source-of-truth.
    //      Both the painter and the visual cursor read it.
    //      It is updated ONCE per frame in UpdateEffectiveCursorPos().
    private Vector2 _effectiveCursorPos;
    private Vector2 _lastEffectiveCursorPos;
    private bool _effectivePosInitialised;

    // Zone state
    public enum CursorZone { Outside, Middle, NearEdge }
    private CursorZone _currentZone;
    private float _edgeProximity;
    private Vector2 _nearestOutsideDirection;

    // WHY: ImpulsePhase is a mini state machine embedded in the edge system.
    //
    //      Inactive ──► (enter NearEdge + LMB held) ──► Waiting
    //      Waiting  ──► (_impulseDelayDuration elapsed) ──► MovingOut
    //      MovingOut ──► (distance == _impulseMagnitude) ──► Returning
    //      Returning ──► (distance == 0) ──► MovingOut  [cycles indefinitely]
    //
    //      Reset to Inactive: LMB released, zone leaves NearEdge, stroke starts.
    //      The 1-second grace only applies to the FIRST impulse per stroke entry.
    //      Subsequent cycles are immediate — difficulty stays continuous.
    private enum ImpulsePhase { Inactive, Waiting, MovingOut, Returning }
    private ImpulsePhase _impulsePhase = ImpulsePhase.Inactive;
    private float _impulsePhaseTimer = 0f;
    private float _currentImpulseDistance = 0f;
    private Vector2 _currentImpulseDirection;

    // Progress state
    private float _progressSampleTimer;
    private float _paintedInsideFraction;
    private float _paintedOutsideFraction;
    private bool _isComplete;
    private bool _hasFailed;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void Awake()
    {
        BuildUI();

        _maskPixels = _maskTexture.GetPixels();
        _canvasWidth = _maskTexture.width;
        _canvasHeight = _maskTexture.height;

        Vector2 panelSize = _panelRect.rect.size;
        _textureToScreenScale = new Vector2(panelSize.x / _canvasWidth,
                                            panelSize.y / _canvasHeight);

        // Count paintable pixels once — used for completion fraction denominator
        _totalMaskPixels = 0;
        for (int i = 0; i < _maskPixels.Length; i++)
            if (_maskPixels[i].grayscale > 0.5f)
                _totalMaskPixels++;

        if (_totalMaskPixels == 0)
        {
            Debug.LogError("[ColoringMinigame] Mask has zero white pixels! " +
                           "Check your mask — white fill inside the outline.");
            return;
        }

        _canvasTexture = new Texture2D(_canvasWidth, _canvasHeight, TextureFormat.RGBA32, false);
        _canvasTexture.filterMode = FilterMode.Bilinear;
        Color[] clear = new Color[_canvasWidth * _canvasHeight];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        _canvasTexture.SetPixels(clear);
        _canvasTexture.Apply();

        _paintingDisplay.texture = _canvasTexture;
        _sketchDisplay.sprite = TextureToSprite(_sketchTexture);

        _currentColorIndex = 0;
        _currentColor = _availableColors[0];

        _cursorTexture = GenerateCircleCursorTexture(64);
        _cursorImage.sprite = Sprite.Create(_cursorTexture,
            new Rect(0, 0, _cursorTexture.width, _cursorTexture.height),
            new Vector2(0.5f, 0.5f));
        _cursorImage.color = _currentColor;

        Cursor.visible = false;

        Debug.Log($"[ColoringMinigame] Initialised. Canvas: {_canvasWidth}x{_canvasHeight}, " +
                  $"Paintable pixels: {_totalMaskPixels}");
    }

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("ColoringCanvas");
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 0;
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("PaintingPanel");
        panel.transform.SetParent(canvasGO.transform, false);
        _panelRect = panel.AddComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRect.pivot = new Vector2(0.5f, 0.5f);

        float aspect = (float)_sketchTexture.width / _sketchTexture.height;
        float panelHeight = Screen.height * 0.82f;
        float panelWidth = panelHeight * aspect;
        if (panelWidth > Screen.width * 0.75f)
        {
            panelWidth = Screen.width * 0.75f;
            panelHeight = panelWidth / aspect;
        }
        _panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        // White backing — sketch PNG is transparent; camera clear color would show otherwise
        GameObject bgGO = new GameObject("WhiteBackground");
        bgGO.transform.SetParent(panel.transform, false);
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = Color.white;
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        GameObject paintingGO = new GameObject("PaintingLayer");
        paintingGO.transform.SetParent(panel.transform, false);
        _paintingDisplay = paintingGO.AddComponent<RawImage>();
        RectTransform paintRT = paintingGO.GetComponent<RectTransform>();
        paintRT.anchorMin = Vector2.zero; paintRT.anchorMax = Vector2.one;
        paintRT.offsetMin = Vector2.zero; paintRT.offsetMax = Vector2.zero;

        GameObject sketchGO = new GameObject("SketchOverlay");
        sketchGO.transform.SetParent(panel.transform, false);
        _sketchDisplay = sketchGO.AddComponent<Image>();
        _sketchDisplay.preserveAspect = true;
        RectTransform sketchRT = sketchGO.GetComponent<RectTransform>();
        sketchRT.anchorMin = Vector2.zero; sketchRT.anchorMax = Vector2.one;
        sketchRT.offsetMin = Vector2.zero; sketchRT.offsetMax = Vector2.zero;

        // WHY: Cursor is a child of the panel so it inherits scale transformations.
        //      raycastTarget = false ensures it never eats mouse events.
        GameObject cursorGO = new GameObject("CursorVisual");
        cursorGO.transform.SetParent(panel.transform, false);
        _cursorImage = cursorGO.AddComponent<Image>();
        _cursorImage.raycastTarget = false;
        _cursorRect = cursorGO.GetComponent<RectTransform>();
        _cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        _cursorRect.pivot = new Vector2(0.5f, 0.5f);
        _cursorRect.sizeDelta = new Vector2(_brushRadius * 2f, _brushRadius * 2f);

        if (_showProgressOnScreen)
        {
            GameObject textGO = new GameObject("DebugText");
            textGO.transform.SetParent(canvasGO.transform, false);
            _debugText = textGO.AddComponent<Text>();
            _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _debugText.fontSize = 18;
            _debugText.color = Color.black;
            _debugText.alignment = TextAnchor.UpperLeft;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0, 1);
            textRT.anchorMax = new Vector2(0, 1);
            textRT.pivot = new Vector2(0, 1);
            textRT.anchoredPosition = new Vector2(20, -20);
            textRT.sizeDelta = new Vector2(500, 200);
        }
    }

    private Texture2D GenerateCircleCursorTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float halfSize = size / 2f;
        float radius = halfSize - 1.5f;
        float feather = 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - halfSize + 0.5f;
                float dy = y - halfSize + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = 1f - Mathf.Clamp01((dist - radius) / feather);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        tex.Apply();
        return tex;
    }

    private Sprite TextureToSprite(Texture2D tex)
        => Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

    // ============================================================
    // UPDATE LOOP
    // ============================================================

    private void Update()
    {
        if (_totalMaskPixels == 0) return;

        HandleColorCycling();
        HandlePaintingInput();
        UpdateCursorZone();
        UpdateEffectiveCursorPos();  // Single source of truth — updates both paint + visual
        UpdateCursorVisual();
        SampleProgress();
        UpdateDebugText();
    }

    // ============================================================
    // INPUT
    // ============================================================

    private void HandleColorCycling()
    {
        if (Input.GetMouseButtonDown(1))
        {
            _currentColorIndex = (_currentColorIndex + 1) % _availableColors.Length;
            _currentColor = _availableColors[_currentColorIndex];
            _cursorImage.color = _currentColor;
        }
    }

    private void HandlePaintingInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _isPainting = true;
            _distanceTraveledThisStroke = 0f;
            _lastEffectiveCursorPos = _effectiveCursorPos;
            _effectivePosInitialised = true;

            // WHY: Reset impulse on every new stroke so the player always gets
            //      _impulseDelayDuration seconds of grace at the start of any stroke,
            //      even if they immediately begin painting near an edge.
            ResetImpulseState();
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isPainting = false;
            // WHY: Reset impulse on LMB release so _currentImpulseDistance doesn't
            //      persist across strokes. The next stroke starts with zero displacement.
            ResetImpulseState();
            _canvasTexture.Apply();
        }
    }

    // ============================================================
    // EFFECTIVE CURSOR POSITION — SINGLE SOURCE OF TRUTH (v2)
    // ============================================================

    /// <summary>
    /// WHY: One method, one update per frame, one resulting position.
    ///      The visual cursor and the painter BOTH read _effectiveCursorPos.
    ///      Nothing else computes a cursor position anywhere else in this class.
    /// </summary>
    private void UpdateEffectiveCursorPos()
    {
        Vector2 rawTexturePos = ScreenToTexturePosition(Input.mousePosition);

        if (rawTexturePos.x < 0 || rawTexturePos.x >= _canvasWidth ||
            rawTexturePos.y < 0 || rawTexturePos.y >= _canvasHeight)
        {
            _effectiveCursorPos = rawTexturePos;
            return;
        }

        if (!_effectivePosInitialised)
        {
            _effectiveCursorPos = rawTexturePos;
            _lastEffectiveCursorPos = rawTexturePos;
            _effectivePosInitialised = true;
            return;
        }

        Vector2 rawDelta = rawTexturePos - _lastEffectiveCursorPos;
        float rawDeltaMag = rawDelta.magnitude;

        switch (_currentZone)
        {
            case CursorZone.Middle:
                // WHY: Reset impulse when cursor is in the safe zone. This ensures
                //      the delay restarts each time the player re-enters the edge zone,
                //      rewarding them for "escaping" back to the middle.
                ResetImpulseState();

                if (_isPainting && rawDeltaMag > 0.1f)
                {
                    _distanceTraveledThisStroke += rawDeltaMag;
                    _effectiveCursorPos = ComputeCurvedPosition(rawTexturePos);
                }
                else
                {
                    _effectiveCursorPos = rawTexturePos;
                }
                break;

            case CursorZone.NearEdge:
                if (_isPainting)
                {
                    _effectiveCursorPos = ComputeResistancePosition(rawTexturePos);
                }
                else
                {
                    // WHY: No painting = impulse should not run. Reset so the next
                    //      stroke into this zone starts with a fresh delay.
                    ResetImpulseState();
                    _effectiveCursorPos = rawTexturePos;
                }
                break;

            case CursorZone.Outside:
            default:
                ResetImpulseState();
                _effectiveCursorPos = rawTexturePos;
                break;
        }

        // ── Paint along the resolved path ───────────────────────────────────
        // WHY: Painting is a consequence of the position update, not the trigger.
        //      We stamp along the delta from last→current effective pos so fast
        //      mouse movement never leaves gaps in the stroke.
        if (_isPainting)
        {
            float dist = Vector2.Distance(_lastEffectiveCursorPos, _effectiveCursorPos);
            if (dist > 0.01f)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (_brushRadius * 0.5f)));
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    StampBrush(Vector2.Lerp(_lastEffectiveCursorPos, _effectiveCursorPos, t),
                               _currentColor);
                }
            }
            else
            {
                // Stationary LMB: stamp one dot so clicking immediately marks the canvas
                StampBrush(_effectiveCursorPos, _currentColor);
            }
            _canvasTexture.Apply();
        }

        _lastEffectiveCursorPos = _effectiveCursorPos;
    }

    // ============================================================
    // BEHAVIOR MODES
    // ============================================================

    /// <summary>
    /// WHY: Oscillates the brush PERPENDICULAR to the dominant movement axis.
    ///      Driven by _distanceTraveledThisStroke (not Time.time) so the wave
    ///      frequency is consistent regardless of mouse speed.
    ///
    ///      Dominant axis clamping prevents diagonal chaos:
    ///        Vertical movement   → horizontal oscillation
    ///        Horizontal movement → vertical oscillation
    /// </summary>
    private Vector2 ComputeCurvedPosition(Vector2 rawPos)
    {
        Vector2 mouseDelta = rawPos - _lastEffectiveCursorPos;
        if (mouseDelta.magnitude < 0.1f) return rawPos;
        mouseDelta.Normalize();

        Vector2 perpendicular = Mathf.Abs(mouseDelta.y) > Mathf.Abs(mouseDelta.x)
            ? new Vector2(1f, 0f)   // Vertical motion  → horizontal wave
            : new Vector2(0f, 1f);  // Horizontal motion → vertical wave

        float oscillation = Mathf.Sin(_distanceTraveledThisStroke * _curveFrequency * Mathf.PI * 2f)
                            * _curveAmplitude;

        return rawPos + perpendicular * oscillation;
    }

    /// <summary>
    /// WHY: Two forces compose the near-edge displacement:
    ///
    ///   1. STEADY RESISTANCE — a direct proportional offset.
    ///      Always present while painting near the edge. Scales linearly with proximity.
    ///      No accumulation, no memory. Predictable and Inspector-friendly.
    ///
    ///   2. CYCLIC IMPULSE — a phase-driven outward lurch.
    ///      Fires after _impulseDelayDuration, travels to _impulseMagnitude at _impulseSpeed,
    ///      returns at _returnSpeed, then immediately cycles again.
    ///      The direction has ±30° random variation each cycle so it never feels robotic.
    ///
    ///   Total displacement = resistanceOffset + impulseOffset.
    ///   Both are in texture pixels, relative to the raw mouse position.
    /// </summary>
    private Vector2 ComputeResistancePosition(Vector2 rawPos)
    {
        // Steady resistance: direct offset, no state, always proportional
        Vector2 resistanceOffset = _nearestOutsideDirection
                                   * _resistanceStrength
                                   * _edgeProximity;

        // Cyclic impulse: advance the phase machine, read current displacement
        TickImpulsePhase();
        Vector2 impulseOffset = _currentImpulseDirection * _currentImpulseDistance;

        return rawPos + resistanceOffset + impulseOffset;
    }

    /// <summary>
    /// WHY: The state machine that drives the cyclic impulse behavior.
    ///
    ///   Inactive  → Waiting:    Immediately when this method is first called
    ///                           (entry into NearEdge zone while painting).
    ///                           Starts the delay countdown.
    ///
    ///   Waiting   → MovingOut:  After _impulseDelayDuration seconds.
    ///                           Picks a new direction (±30° from nearest-outside).
    ///
    ///   MovingOut → Returning:  _currentImpulseDistance reaches _impulseMagnitude.
    ///                           Cursor is now at max displacement. Begins return.
    ///
    ///   Returning → MovingOut:  _currentImpulseDistance returns to 0.
    ///                           Immediately starts the next cycle — no delay.
    ///                           WHY no delay: the 1-second grace is for the FIRST
    ///                           impulse only. After that the difficulty is continuous.
    /// </summary>
    private void TickImpulsePhase()
    {
        switch (_impulsePhase)
        {
            case ImpulsePhase.Inactive:
                _impulsePhase = ImpulsePhase.Waiting;
                _impulsePhaseTimer = 0f;
                break;

            case ImpulsePhase.Waiting:
                _impulsePhaseTimer += Time.deltaTime;
                if (_impulsePhaseTimer >= _impulseDelayDuration)
                {
                    _impulsePhase = ImpulsePhase.MovingOut;
                    _currentImpulseDistance = 0f;
                    PickImpulseDirection();
                }
                break;

            case ImpulsePhase.MovingOut:
                _currentImpulseDistance += _impulseSpeed * Time.deltaTime;
                if (_currentImpulseDistance >= _impulseMagnitude)
                {
                    _currentImpulseDistance = _impulseMagnitude;
                    _impulsePhase = ImpulsePhase.Returning;
                }
                break;

            case ImpulsePhase.Returning:
                _currentImpulseDistance -= _returnSpeed * Time.deltaTime;
                if (_currentImpulseDistance <= 0f)
                {
                    _currentImpulseDistance = 0f;
                    // WHY: Cycle immediately — no second delay. Difficulty stays constant
                    //      once the impulse has started. The player must keep compensating.
                    _impulsePhase = ImpulsePhase.MovingOut;
                    PickImpulseDirection();
                }
                break;
        }
    }

    /// <summary>
    /// WHY: Picks a new outward direction with ±30° random variation on every new cycle.
    ///      Pure outward (0° variation) feels robotic after 2 repetitions — the player
    ///      learns the exact angle and compensates mechanically. ±30° keeps them guessing
    ///      while still being fundamentally "away from the sketch".
    /// </summary>
    private void PickImpulseDirection()
    {
        if (_nearestOutsideDirection == Vector2.zero)
        {
            // WHY: Fallback guard. Shouldn't happen in NearEdge zone, but if zone
            //      detection races and delivers a zero vector, default to right
            //      rather than producing a (0,0) impulse direction silently.
            _currentImpulseDirection = Vector2.right;
            return;
        }

        float angle = Random.Range(-30f, 30f) * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        _currentImpulseDirection = new Vector2(
            _nearestOutsideDirection.x * cos - _nearestOutsideDirection.y * sin,
            _nearestOutsideDirection.x * sin + _nearestOutsideDirection.y * cos
        ).normalized;
    }

    /// <summary>
    /// WHY: Centralised reset so no caller forgets to zero the distance.
    ///      Called from: HandlePaintingInput (LMB down/up), UpdateEffectiveCursorPos
    ///      (zone transitions), ResetCanvas. One method, one place to update if
    ///      new impulse fields are ever added.
    ///
    ///      NOTE: _currentImpulseDirection is intentionally NOT reset here.
    ///      Since _currentImpulseDistance = 0, the impulse offset = direction * 0 = (0,0).
    ///      The direction will be repicked by PickImpulseDirection() on the next MovingOut entry.
    /// </summary>
    private void ResetImpulseState()
    {
        _impulsePhase = ImpulsePhase.Inactive;
        _impulsePhaseTimer = 0f;
        _currentImpulseDistance = 0f;
    }

    // ============================================================
    // VISUAL CURSOR
    // ============================================================

    /// <summary>
    /// WHY: Reads _effectiveCursorPos directly — guarantees visual and paint always agree.
    ///      The ONE exception is Near Edge + no LMB: we add cosmetic jitter on top.
    ///      Jitter is VISUAL ONLY. It never affects where paint is stamped.
    ///      Applying it while painting would make the brush circle lie about where
    ///      the paint is going, which is disorienting rather than challenging.
    /// </summary>
    private void UpdateCursorVisual()
    {
        Vector2 displayTexturePos = _effectiveCursorPos;

        if (_currentZone == CursorZone.NearEdge && !_isPainting)
        {
            float noiseX = Mathf.PerlinNoise(Time.time * _jitterSpeed, 0f) - 0.5f;
            float noiseY = Mathf.PerlinNoise(0f, Time.time * _jitterSpeed) - 0.5f;
            displayTexturePos += new Vector2(noiseX, noiseY) * _jitterIntensity * 2f;
        }

        if (displayTexturePos.x < 0 || displayTexturePos.x >= _canvasWidth ||
            displayTexturePos.y < 0 || displayTexturePos.y >= _canvasHeight)
        {
            _cursorImage.enabled = false;
            return;
        }

        _cursorImage.enabled = true;
        _cursorRect.anchoredPosition = TextureToPanelLocal(displayTexturePos);

        float cursorSize = _brushRadius * 2f * Mathf.Max(_textureToScreenScale.x,
                                                          _textureToScreenScale.y);
        _cursorRect.sizeDelta = new Vector2(cursorSize, cursorSize);
    }

    // ============================================================
    // ZONE DETECTION
    // ============================================================

    /// <summary>
    /// WHY: Zone is always computed from the RAW mouse position, not _effectiveCursorPos.
    ///      If we computed from the effective pos, an impulse that pushed the cursor
    ///      outside the edge zone would immediately clear the zone and kill the impulse —
    ///      a negative feedback loop that makes the mechanic collapse instantly.
    ///      The player's INTENT (raw mouse) drives zone detection.
    ///      The EXPERIENCE (effective pos) drives what they feel.
    /// </summary>
    private void UpdateCursorZone()
    {
        Vector2 texturePos = ScreenToTexturePosition(Input.mousePosition);

        if (texturePos.x < 0 || texturePos.x >= _canvasWidth ||
            texturePos.y < 0 || texturePos.y >= _canvasHeight)
        {
            _currentZone = CursorZone.Outside;
            _edgeProximity = 0f;
            return;
        }

        int cx = Mathf.RoundToInt(texturePos.x);
        int cy = Mathf.RoundToInt(texturePos.y);
        int centerIndex = cy * _canvasWidth + cx;

        if (centerIndex < 0 || centerIndex >= _maskPixels.Length ||
            _maskPixels[centerIndex].grayscale <= 0.5f)
        {
            _currentZone = CursorZone.Outside;
            _edgeProximity = 0f;
            return;
        }

        float minDist = float.MaxValue;
        Vector2 bestDirection = Vector2.zero;

        for (int i = 0; i < _edgeSampleDirections; i++)
        {
            float angle = (i / (float)_edgeSampleDirections) * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            for (int step = 1; step <= _edgeZoneWidth; step++)
            {
                int sx = cx + Mathf.RoundToInt(dir.x * step);
                int sy = cy + Mathf.RoundToInt(dir.y * step);

                if (sx < 0 || sx >= _canvasWidth || sy < 0 || sy >= _canvasHeight)
                {
                    if (step < minDist) { minDist = step; bestDirection = dir; }
                    break;
                }

                int sampleIndex = sy * _canvasWidth + sx;
                if (_maskPixels[sampleIndex].grayscale < 0.5f)
                {
                    if (step < minDist) { minDist = step; bestDirection = dir; }
                    break;
                }
            }
        }

        if (minDist <= _edgeZoneWidth)
        {
            _currentZone = CursorZone.NearEdge;
            _edgeProximity = 1f - (minDist / _edgeZoneWidth);
            _nearestOutsideDirection = bestDirection;
        }
        else
        {
            _currentZone = CursorZone.Middle;
            _edgeProximity = 0f;
            _nearestOutsideDirection = Vector2.zero;
        }
    }

    // ============================================================
    // COORDINATE UTILITIES
    // ============================================================

    private Vector2 ScreenToTexturePosition(Vector2 screenPos)
    {
        RectTransform rt = _paintingDisplay.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, screenPos, _canvas.worldCamera, out Vector2 localPos))
            return new Vector2(-1f, -1f);

        Vector2 size = rt.rect.size;
        float u = (localPos.x / size.x) + 0.5f;
        float v = (localPos.y / size.y) + 0.5f;
        return new Vector2(u * _canvasWidth, v * _canvasHeight);
    }

    private Vector2 TextureToPanelLocal(Vector2 texturePos)
    {
        Vector2 size = _paintingDisplay.rectTransform.rect.size;
        float u = texturePos.x / _canvasWidth;
        float v = texturePos.y / _canvasHeight;
        return new Vector2((u - 0.5f) * size.x, (v - 0.5f) * size.y);
    }

    // ============================================================
    // BRUSH STAMPING
    // ============================================================

    private void StampBrush(Vector2 center, Color color)
    {
        int cx = Mathf.RoundToInt(center.x);
        int cy = Mathf.RoundToInt(center.y);
        int r = Mathf.CeilToInt(_brushRadius);

        int xMin = Mathf.Max(0, cx - r);
        int xMax = Mathf.Min(_canvasWidth - 1, cx + r);
        int yMin = Mathf.Max(0, cy - r);
        int yMax = Mathf.Min(_canvasHeight - 1, cy + r);

        if (xMax < xMin || yMax < yMin) return;

        Color[] existingPixels = _canvasTexture.GetPixels(xMin, yMin,
                                                          xMax - xMin + 1,
                                                          yMax - yMin + 1);
        for (int py = yMin; py <= yMax; py++)
            for (int px = xMin; px <= xMax; px++)
            {
                float dx = px - cx;
                float dy = py - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > _brushRadius) continue;

                float normalizedDist = dist / _brushRadius;
                float alpha = Mathf.Clamp01(1f - Mathf.Pow(normalizedDist, 1f / _brushHardness));

                int localIndex = (py - yMin) * (xMax - xMin + 1) + (px - xMin);
                Color existing = existingPixels[localIndex];
                Color blended = Color.Lerp(existing, color, alpha);
                blended.a = Mathf.Max(existing.a, alpha);
                existingPixels[localIndex] = blended;
            }

        _canvasTexture.SetPixels(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1, existingPixels);
    }

    // ============================================================
    // PROGRESS TRACKING
    // ============================================================

    private void SampleProgress()
    {
        if (_isComplete || _hasFailed) return;

        _progressSampleTimer -= Time.deltaTime;
        if (_progressSampleTimer > 0f) return;
        _progressSampleTimer = _progressSampleInterval;

        Color[] canvasPixels = _canvasTexture.GetPixels();
        int paintedInside = 0, paintedOutside = 0, totalPainted = 0;
        const int stride = 4;

        for (int y = 0; y < _canvasHeight; y += stride)
            for (int x = 0; x < _canvasWidth; x += stride)
            {
                int idx = y * _canvasWidth + x;
                bool insideMask = _maskPixels[idx].grayscale > 0.5f;
                bool hasPaint = canvasPixels[idx].a > 0.05f;

                if (!hasPaint) continue;
                totalPainted++;
                if (insideMask) paintedInside++;
                else paintedOutside++;
            }

        if (totalPainted > 0)
        {
            _paintedInsideFraction = Mathf.Clamp01((float)paintedInside /
                                          (_totalMaskPixels / (stride * stride)));
            _paintedOutsideFraction = Mathf.Clamp01((float)paintedOutside / totalPainted);
        }
        else
        {
            _paintedInsideFraction = 0f;
            _paintedOutsideFraction = 0f;
        }

        if (_paintedInsideFraction >= _completionThreshold)
        {
            _isComplete = true;
            Debug.Log($"[ColoringMinigame] ✅ COMPLETE! Inside: {_paintedInsideFraction:P1}");
            OnComplete();
        }
        else if (_paintedOutsideFraction >= _outOfBoundsFailThreshold)
        {
            _hasFailed = true;
            Debug.Log($"[ColoringMinigame] ❌ FAILED! Outside: {_paintedOutsideFraction:P1}");
            OnFail();
        }
    }

    private void OnComplete()
    {
        // Stub: in production → _gameManager.ResolveMinigame(MinigameResult.Success)
        Debug.Log("[ColoringMinigame] 🎉 Transitioning to NarrativeState...");
    }

    private void OnFail()
    {
        Debug.Log("[ColoringMinigame] Resetting in 1.5s...");
        Invoke(nameof(ResetCanvas), 1.5f);
    }

    private void ResetCanvas()
    {
        Color[] clear = new Color[_canvasWidth * _canvasHeight];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        _canvasTexture.SetPixels(clear);
        _canvasTexture.Apply();

        _isComplete = false;
        _hasFailed = false;
        _isPainting = false;
        _paintedInsideFraction = 0f;
        _paintedOutsideFraction = 0f;
        _distanceTraveledThisStroke = 0f;
        _effectivePosInitialised = false;
        ResetImpulseState();

        Debug.Log("[ColoringMinigame] Canvas reset. Try again.");
    }

    // ============================================================
    // DEBUG UI
    // ============================================================

    private void UpdateDebugText()
    {
        if (_debugText == null || !_showProgressOnScreen) return;

        string zoneLabel = _currentZone switch
        {
            CursorZone.Middle => "MIDDLE (curved)",
            CursorZone.NearEdge => _isPainting
                                    ? "NEAR EDGE (resistance + impulse)"
                                    : "NEAR EDGE (jitter only)",
            CursorZone.Outside => "OUTSIDE",
            _ => "???"
        };

        string impulseLabel = _impulsePhase switch
        {
            ImpulsePhase.Inactive => "INACTIVE",
            ImpulsePhase.Waiting =>
                $"WAITING  {_impulsePhaseTimer:F1}s / {_impulseDelayDuration:F1}s",
            ImpulsePhase.MovingOut =>
                $"► MOVING OUT  {_currentImpulseDistance:F0}px / {_impulseMagnitude:F0}px",
            ImpulsePhase.Returning =>
                $"◄ RETURNING   {_currentImpulseDistance:F0}px remaining",
            _ => "???"
        };

        float resistNow = _resistanceStrength * _edgeProximity;

        _debugText.text =
            $"<b>Zone:</b> {zoneLabel}  <b>Proximity:</b> {_edgeProximity:F2}\n" +
            $"<b>Impulse:</b> {impulseLabel}\n" +
            $"<b>Resistance offset:</b> {resistNow:F1}px  " +
            $"<b>(max {_resistanceStrength:F0}px at proximity 1)\n</b>" +
            $"<b>Painted Inside:</b>  {_paintedInsideFraction:P1} / {_completionThreshold:P0}\n" +
            $"<b>Painted Outside:</b> {_paintedOutsideFraction:P1} / {_outOfBoundsFailThreshold:P0}\n" +
            $"<b>Status:</b> {(_isComplete ? "✅ COMPLETE" : _hasFailed ? "❌ FAILED" : "Painting...")}";
    }

    // ============================================================
    // PUBLIC API (IState integration)
    // ============================================================

    public bool IsComplete => _isComplete;
    public bool HasFailed => _hasFailed;
    public float PaintedInsideFraction => _paintedInsideFraction;
    public float PaintedOutsideFraction => _paintedOutsideFraction;
    public CursorZone CurrentZone => _currentZone;

    public void Exit()
    {
        Cursor.visible = true;
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_edgeSampleDirections < 4) _edgeSampleDirections = 4;
        if (_brushRadius < 2f) _brushRadius = 2f;
        if (_progressSampleInterval < 0.1f) _progressSampleInterval = 0.1f;
        if (_returnSpeed < 1f) _returnSpeed = 1f;
        if (_impulseSpeed < 1f) _impulseSpeed = 1f;
        if (_impulseMagnitude < 1f) _impulseMagnitude = 1f;
        if (_impulseDelayDuration < 0f) _impulseDelayDuration = 0f;
    }
#endif
}
