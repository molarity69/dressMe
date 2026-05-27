// ============================================================
// ColoringMinigame_v2.cs (v5 — Velocity Scaling + Marker Cursor + Restart Fix)
//
// RESPONSIBILITY: Self-contained coloring minigame simulating
// the motor struggle of painting within lines.
//
// KEY CHANGES FROM v4 (Scene-Wired):
//   [1] VELOCITY TRACKING — cursor speed (texture px/s) drives _currentVelocityMultiplier
//       which scales jitter intensity AND impulse speed/magnitude in real time.
//       Drawing fast = harder to control. Rewards slow, deliberate movement.
//   [2] MARKER CURSOR — serialized Sprite with configurable tip pivot replaces
//       the generated circle. Falls back to circle if no sprite assigned.
//       Color cycling tints the sprite via Image.color — sprite must be white/greyscale.
//   [3] FAIL RESTART FIXED — _isPainting forced false in OnFail() prevents held LMB
//       from re-dirtying the canvas during the 1.5s countdown.
//       _progressSampleTimer reset in ResetCanvas() prevents immediate re-trigger.
//   [4] R KEY RESET — ResetCanvas() callable at any time. CancelInvoke() prevents
//       double-reset when R is pressed during the auto-reset countdown.
//
// PLACE ON:  'Coloring' GameObject
//
// INSPECTOR INJECTIONS (Required):
//   _injectedCanvas          → Minigames (root Canvas)
//   _injectedPaintingDisplay → Color Area (must be RawImage, NOT Image)
//   _injectedSketchDisplay   → Outline (Image component)
//   _injectedPanelRect       → Coloring (its own RectTransform)
// ============================================================

using UnityEngine;
using UnityEngine.UI;

public class Coloring : MonoBehaviour
{
    // ============================================================
    // SCENE REFERENCES — Injected via Inspector
    // WHY: Script owns zero UI construction. It receives the pre-built
    //      hierarchy and operates on it. All dependencies are explicit
    //      and fail loudly in Awake() if missing.
    // ============================================================

    [Header("Scene References (Required)")]
    [Tooltip("Root Canvas (Minigames). Required for ScreenPointToLocalPointInRectangle world camera lookup.")]
    [SerializeField] private Canvas _injectedCanvas;

    [Tooltip("RawImage on 'Color Area'. MUST be RawImage — receives paint Texture2D at runtime.")]
    [SerializeField] private RawImage _injectedPaintingDisplay;

    [Tooltip("Image on 'Outline'. Receives sketch sprite only if no sprite is already assigned.")]
    [SerializeField] private Image _injectedSketchDisplay;

    [Tooltip("RectTransform of 'Coloring' (this GameObject). Panel bounds for coordinate mapping.")]
    [SerializeField] private RectTransform _injectedPanelRect;

    // ============================================================
    // CONFIGURATION
    // ============================================================

    [Header("Textures (Required)")]
    [Tooltip("Visible sketch outline. Transparent interior, opaque lines.")]
    [SerializeField] private Texture2D _sketchTexture;

    [Tooltip("Mask: White = valid paint area. Black = outline/outside.")]
    [SerializeField] private Texture2D _maskTexture;

    [Header("Cursor Visual")]
    [Tooltip(
        "Optional marker sprite. If assigned, replaces the generated circle.\n" +
        "CRITICAL: Sprite MUST be white or greyscale — color cycling applies via Image.color tint.\n" +
        "A pre-coloured sprite will mix its colour with the selected paint colour."
    )]
    [SerializeField] private Sprite _markerCursorSprite;

    [Tooltip(
        "Normalized UV of the marker tip on the sprite.\n" +
        "(0.5, 0.0) = bottom-center tip  |  (0.0, 0.0) = bottom-left tip\n" +
        "This pivot is WHERE PAINT IS APPLIED — match it to the physical tip on your artwork."
    )]
    [SerializeField] private Vector2 _markerCursorPivot = new Vector2(0.5f, 0f);

    [Tooltip("Display size of the marker in panel pixels. Only used when a marker sprite is assigned.")]
    [SerializeField] private Vector2 _markerCursorSize = new Vector2(30f, 60f);

    [Header("Colors (Cycle with RMB)")]
    [SerializeField]
    private Color[] _availableColors = new Color[]
    {
        new Color(1f,    0.15f, 0.15f, 1f),
        new Color(0.15f, 0.6f,  0.15f, 1f),
        new Color(0.15f, 0.3f,  1f,    1f),
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
    [Tooltip(
        "BASE peak displacement of the cosmetic cursor shake near edges.\n" +
        "Actual intensity = this × _currentVelocityMultiplier.\n" +
        "Move fast near an edge → cursor shakes harder."
    )]
    [SerializeField] private float _jitterIntensity = 10f;

    [Tooltip("Perlin noise cycle speed. Higher = faster shake.")]
    [SerializeField] private float _jitterSpeed = 18f;

    [Header("Edge Zone — Steady Resistance (LMB Held)")]
    [Tooltip(
        "DIRECT pixel offset outward while painting near the edge.\n" +
        "proximity 1.0 (on outline)  → full offset\n" +
        "proximity 0.5 (mid edge)    → half offset\n" +
        "proximity 0.0 (zone entry)  → zero offset\n" +
        "Not velocity-scaled — keeps resistance predictable and learnable."
    )]
    [SerializeField] private float _resistanceStrength = 15f;

    [Header("Edge Zone — Cyclic Impulse (LMB Held)")]
    [Tooltip("Seconds after entering edge zone before the first impulse fires. Grace window per stroke entry.")]
    [SerializeField] private float _impulseDelayDuration = 1f;

    [Tooltip(
        "BASE outward travel speed (texture px/s).\n" +
        "Scales with velocity — move fast → impulse lurches outward faster."
    )]
    [SerializeField] private float _impulseSpeed = 55f;

    [Tooltip(
        "BASE max push distance from raw mouse (texture px).\n" +
        "Scales with velocity — move fast → impulse reaches further out."
    )]
    [SerializeField] private float _impulseMagnitude = 30f;

    [Tooltip(
        "Return speed (texture px/s) after peak impulse.\n" +
        "NOT velocity-scaled — constant recovery keeps correction learnable."
    )]
    [SerializeField] private float _returnSpeed = 80f;

    [Header("Velocity-Based Difficulty")]
    [Tooltip(
        "Cursor speed (texture px/s) below which multiplier = 1.0 (base difficulty).\n" +
        "On a 512px canvas, ~20 px/s is a slow deliberate stroke."
    )]
    [SerializeField] private float _minVelocityThreshold = 20f;

    [Tooltip(
        "Cursor speed (texture px/s) at which multiplier reaches its ceiling.\n" +
        "On a 512px canvas, ~200 px/s is a fast sweep across 40% of the image."
    )]
    [SerializeField] private float _maxVelocityThreshold = 200f;

    [Tooltip(
        "Multiplier ceiling at peak cursor speed.\n" +
        "Applied to jitter intensity AND impulse speed/magnitude.\n" +
        "1.0 = no scaling. 2.5 = 2.5× harder at max speed.\n" +
        "Recommended: 2.0–3.0. Above 4.0 tends to feel unfair."
    )]
    [SerializeField] private float _maxVelocityMultiplier = 2.5f;

    [Tooltip(
        "Exponential smoothing factor for velocity. Higher = snappier but noisier.\n" +
        "Recommended: 6–10."
    )]
    [SerializeField] private float _velocitySmoothing = 8f;

    [Header("Progress & Win / Fail")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _completionThreshold = 0.85f;

    [Range(0.01f, 0.5f)]
    [SerializeField] private float _outOfBoundsFailThreshold = 0.15f;

    [Tooltip("Seconds between canvas progress samples. Never sample every frame.")]
    [SerializeField] private float _progressSampleInterval = 0.4f;

    [Header("Debug UI")]
    [SerializeField] private bool _showProgressOnScreen = true;

    // ============================================================
    // PRIVATE STATE
    // ============================================================

    // Scene-wired rendering references
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
    private Texture2D _cursorTexture;     // Only allocated for circle fallback
    private Vector2 _textureToScreenScale;
    // WHY: Cached at Awake so UpdateCursorVisual never tries to resize the marker.
    //      Resizing a marker every frame based on brushRadius would distort artist artwork.
    private bool _useMarkerCursor;

    // Runtime GameObjects we own — tracked for explicit OnDestroy cleanup
    private GameObject _cursorGO;
    private GameObject _debugTextGO;

    // Mask data — flat array avoids per-pixel allocation in Update
    private Color[] _maskPixels;
    private int _totalMaskPixels;

    // Input state
    private bool _isPainting;
    private int _currentColorIndex;
    private Color _currentColor;
    private float _distanceTraveledThisStroke;

    // WHY: _effectiveCursorPos is the single source of truth.
    //      The painter and the visual cursor BOTH read it.
    //      It is written ONCE per frame in UpdateEffectiveCursorPos().
    private Vector2 _effectiveCursorPos;
    private Vector2 _lastEffectiveCursorPos;
    private bool _effectivePosInitialised;

    // Zone state
    public enum CursorZone { Outside, Middle, NearEdge }
    private CursorZone _currentZone;
    private float _edgeProximity;
    private Vector2 _nearestOutsideDirection;

    // WHY: ImpulsePhase is a mini state machine inside the edge system.
    //
    //   Inactive  ──► (NearEdge + LMB held)              ──► Waiting
    //   Waiting   ──► (_impulseDelayDuration elapsed)     ──► MovingOut
    //   MovingOut ──► (distance == effectiveMagnitude)    ──► Returning
    //   Returning ──► (distance == 0)                     ──► MovingOut  [cycles]
    //
    //   Reset to Inactive: LMB up, zone exits NearEdge, new stroke, R pressed.
    //   Delay only applies to the FIRST impulse per edge-zone entry.
    //   Subsequent cycles are immediate — difficulty stays continuous.
    private enum ImpulsePhase { Inactive, Waiting, MovingOut, Returning }
    private ImpulsePhase _impulsePhase = ImpulsePhase.Inactive;
    private float _impulsePhaseTimer = 0f;
    private float _currentImpulseDistance = 0f;
    private Vector2 _currentImpulseDirection;

    // WHY: Captured ONCE at the start of each MovingOut phase.
    //      Locking the target magnitude means mid-cycle velocity changes
    //      don't shift the goalposts while the impulse is already travelling.
    //      New velocity is picked up on the NEXT cycle entry.
    private float _currentEffectiveMagnitude;

    // Velocity tracking
    private Vector2 _prevRawTexturePos;
    private bool _prevRawPosInitialised;
    private float _smoothedVelocity;
    // WHY: Single output of UpdateVelocity(). Both jitter and impulse read this.
    //      Centralised so both systems see the identical value within one frame.
    private float _currentVelocityMultiplier = 1f;

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
        // WHY: Validate all four injections immediately. A NullRef buried inside
        //      Update() costs 20 minutes to diagnose. A clear error here costs 20 seconds.
        if (_injectedCanvas == null ||
            _injectedPaintingDisplay == null ||
            _injectedSketchDisplay == null ||
            _injectedPanelRect == null)
        {
            Debug.LogError(
                "[ColoringMinigame] Missing scene references. Assign in Inspector:\n" +
                "  • Injected Canvas           → Minigames\n" +
                "  • Injected Painting Display → Color Area (RawImage)\n" +
                "  • Injected Sketch Display   → Outline (Image)\n" +
                "  • Injected Panel Rect       → Coloring (RectTransform)"
            );
            enabled = false;
            return;
        }

        // Wire injected references into the private fields used by all logic below.
        // WHY: Keeping injected and internal fields separate means we could support
        //      re-initialisation with a different hierarchy at runtime if needed.
        _canvas = _injectedCanvas;
        _paintingDisplay = _injectedPaintingDisplay;
        _sketchDisplay = _injectedSketchDisplay;
        _panelRect = _injectedPanelRect;

        // Cursor is still procedural — it doesn't belong in the designed hierarchy.
        BuildCursorVisual();

        if (_showProgressOnScreen)
            BuildDebugText();

        // ── Mask & canvas texture setup ──────────────────────────────────────
        _maskPixels = _maskTexture.GetPixels();
        _canvasWidth = _maskTexture.width;
        _canvasHeight = _maskTexture.height;

        Vector2 panelSize = _panelRect.rect.size;
        _textureToScreenScale = new Vector2(panelSize.x / _canvasWidth,
                                            panelSize.y / _canvasHeight);

        _totalMaskPixels = 0;
        for (int i = 0; i < _maskPixels.Length; i++)
            if (_maskPixels[i].grayscale > 0.5f)
                _totalMaskPixels++;

        if (_totalMaskPixels == 0)
        {
            Debug.LogError("[ColoringMinigame] Mask has zero white pixels! " +
                           "Check mask texture — white fill = paintable area.");
            return;
        }

        _canvasTexture = new Texture2D(_canvasWidth, _canvasHeight, TextureFormat.RGBA32, false);
        _canvasTexture.filterMode = FilterMode.Bilinear;
        Color[] clear = new Color[_canvasWidth * _canvasHeight];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        _canvasTexture.SetPixels(clear);
        _canvasTexture.Apply();

        _paintingDisplay.texture = _canvasTexture;

        // WHY: Guard prevents overwriting a sprite the artist set directly in the scene.
        //      To have the script own the sketch exclusively, clear the Outline sprite
        //      in the Inspector and assign _sketchTexture here instead.
        if (_sketchTexture != null && _sketchDisplay.sprite == null)
            _sketchDisplay.sprite = TextureToSprite(_sketchTexture);

        // Circle fallback only — marker sprite was already wired in BuildCursorVisual()
        if (!_useMarkerCursor)
        {
            _cursorTexture = GenerateCircleCursorTexture(64);
            _cursorImage.sprite = Sprite.Create(
                _cursorTexture,
                new Rect(0, 0, _cursorTexture.width, _cursorTexture.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        _currentColorIndex = 0;
        _currentColor = _availableColors[0];
        _cursorImage.color = _currentColor;
        _currentEffectiveMagnitude = _impulseMagnitude;

        Cursor.visible = false;

        Debug.Log($"[ColoringMinigame] Initialised. {_canvasWidth}×{_canvasHeight}, " +
                  $"{_totalMaskPixels} paintable px. " +
                  $"Cursor: {(_useMarkerCursor ? "Marker sprite" : "Generated circle")}.");
    }

    // WHY: Isolated so swapping between marker and circle only touches this method.
    //      Nothing else in Awake() changes if the cursor implementation changes.
    private void BuildCursorVisual()
    {
        _cursorGO = new GameObject("CursorVisual");
        _cursorGO.transform.SetParent(_panelRect, false);

        _cursorImage = _cursorGO.AddComponent<Image>();
        // WHY: raycastTarget = false ensures the cursor visual never eats mouse events
        //      that should reach the panel or hotspots beneath it.
        _cursorImage.raycastTarget = false;

        _cursorRect = _cursorGO.GetComponent<RectTransform>();
        _cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorRect.anchorMax = new Vector2(0.5f, 0.5f);

        if (_markerCursorSprite != null)
        {
            // WHY: Setting pivot to the marker tip means anchoredPosition maps DIRECTLY
            //      to the paint application point — zero offset math required downstream.
            //      The sprite hangs naturally above/around the tip.
            _cursorImage.sprite = _markerCursorSprite;
            _cursorRect.pivot = _markerCursorPivot;
            _cursorRect.sizeDelta = _markerCursorSize;
            _useMarkerCursor = true;
        }
        else
        {
            // WHY: Fallback circle for development without art assets.
            //      Pivot at center — anchoredPosition = brush center.
            //      Sprite is assigned later in Awake() after texture generation.
            _cursorRect.pivot = new Vector2(0.5f, 0.5f);
            _cursorRect.sizeDelta = new Vector2(_brushRadius * 2f, _brushRadius * 2f);
            _useMarkerCursor = false;
        }
    }

    // WHY: Parented to root Canvas, not the panel — renders above all panel content
    //      and is never clipped by the panel's RectTransform bounds.
    private void BuildDebugText()
    {
        _debugTextGO = new GameObject("DebugText");
        _debugTextGO.transform.SetParent(_canvas.transform, false);

        _debugText = _debugTextGO.AddComponent<Text>();
        _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _debugText.fontSize = 18;
        _debugText.color = Color.black;
        _debugText.alignment = TextAnchor.UpperLeft;

        RectTransform textRT = _debugTextGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 1);
        textRT.anchorMax = new Vector2(0, 1);
        textRT.pivot = new Vector2(0, 1);
        textRT.anchoredPosition = new Vector2(20, -20);
        textRT.sizeDelta = new Vector2(540, 280);
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
        HandleResetInput();
        // WHY: Velocity MUST update before zone/cursor methods so _currentVelocityMultiplier
        //      is fresh when jitter and impulse read it this same frame.
        UpdateVelocity();
        UpdateCursorZone();
        UpdateEffectiveCursorPos();
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
            // WHY: Image.color tints both the generated sprite AND the marker sprite.
            //      This is why the marker sprite must be white/greyscale — any
            //      pre-existing colour on the sprite will multiply with the paint colour.
            _cursorImage.color = _currentColor;
        }
    }

    private void HandlePaintingInput()
    {
        // WHY: Block new strokes while failed or complete. Without this guard, a held
        //      LMB during the 1.5s fail countdown re-dirties the canvas before
        //      ResetCanvas() fires, causing it to immediately re-trigger the fail.
        if (Input.GetMouseButtonDown(0) && !_isComplete && !_hasFailed)
        {
            _isPainting = true;
            _distanceTraveledThisStroke = 0f;
            _lastEffectiveCursorPos = _effectiveCursorPos;
            _effectivePosInitialised = true;
            // WHY: Fresh grace period every new stroke, even when immediately near edge.
            ResetImpulseState();
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isPainting = false;
            ResetImpulseState();
            if (_canvasTexture != null)
                _canvasTexture.Apply();
        }
    }

    // WHY: CancelInvoke is critical. Without it, pressing R during the 1.5s auto-reset
    //      countdown lets the Invoke fire anyway — calling ResetCanvas() a second time
    //      and clearing a canvas the player may have already begun repainting.
    private void HandleResetInput()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            CancelInvoke(nameof(ResetCanvas));
            ResetCanvas();
        }
    }

    // ============================================================
    // VELOCITY TRACKING
    // ============================================================

    /// <summary>
    /// WHY: Tracks raw cursor speed in texture px/s, smoothed via exponential moving
    ///      average to prevent single-frame delta spikes from causing jarring
    ///      instantaneous difficulty changes mid-stroke.
    ///
    ///      Outside-panel handling: velocity decays toward zero and prev position is
    ///      invalidated. Without this, re-entering the panel would compute a large
    ///      outside→inside delta as a false speed spike.
    ///
    ///      Output: _currentVelocityMultiplier — read by jitter and impulse only.
    ///      Both systems read the same value so there is zero divergence between
    ///      how hard jitter hits vs. how hard the impulse hits within a frame.
    /// </summary>
    private void UpdateVelocity()
    {
        Vector2 rawPos = ScreenToTexturePosition(Input.mousePosition);
        bool insidePanel = rawPos.x >= 0 && rawPos.x < _canvasWidth &&
                           rawPos.y >= 0 && rawPos.y < _canvasHeight;

        if (insidePanel)
        {
            if (_prevRawPosInitialised && Time.deltaTime > 0f)
            {
                float speed = Vector2.Distance(rawPos, _prevRawTexturePos) / Time.deltaTime;
                _smoothedVelocity = Mathf.Lerp(_smoothedVelocity, speed,
                                               Time.deltaTime * _velocitySmoothing);
            }
            _prevRawTexturePos = rawPos;
            _prevRawPosInitialised = true;
        }
        else
        {
            // WHY: Decay to zero outside the panel so the multiplier doesn't stay
            //      elevated when the cursor leaves and re-enters.
            _smoothedVelocity = Mathf.Lerp(_smoothedVelocity, 0f,
                                                 Time.deltaTime * _velocitySmoothing);
            _prevRawPosInitialised = false;
        }

        float t = Mathf.InverseLerp(_minVelocityThreshold, _maxVelocityThreshold, _smoothedVelocity);
        _currentVelocityMultiplier = Mathf.Lerp(1f, _maxVelocityMultiplier, t);
    }

    // ============================================================
    // EFFECTIVE CURSOR POSITION — SINGLE SOURCE OF TRUTH
    // ============================================================

    /// <summary>
    /// WHY: One method, one update per frame, one output position.
    ///      The visual cursor and the painter both read _effectiveCursorPos.
    ///      Nothing else in this class computes a cursor position.
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
                // WHY: Reset impulse in the safe zone so the delay timer restarts
                //      next time the player enters the edge zone — rewards retreat.
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
                    _effectiveCursorPos = ComputeResistancePosition(rawTexturePos);
                else
                {
                    // WHY: Not painting → impulse must not run. Reset so the next
                    //      LMB-down in this zone starts with a fresh grace countdown.
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

        // WHY: Guard _hasFailed and _isComplete here — prevents paint being stamped
        //      during the fail countdown (would re-dirty the canvas before ResetCanvas
        //      fires) or after the minigame is already won.
        if (_isPainting && !_hasFailed && !_isComplete)
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
                // Stationary click: stamp one dot so a click immediately marks the canvas.
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
    /// WHY: Oscillates PERPENDICULAR to the dominant movement axis.
    ///      Driven by _distanceTraveledThisStroke (not Time.time) so the wave
    ///      frequency stays consistent regardless of mouse speed — the wobble
    ///      pattern is purely spatial, not temporal.
    ///
    ///      Axis-clamping prevents diagonal chaos:
    ///        Vertical motion   → horizontal oscillation
    ///        Horizontal motion → vertical oscillation
    /// </summary>
    private Vector2 ComputeCurvedPosition(Vector2 rawPos)
    {
        Vector2 mouseDelta = rawPos - _lastEffectiveCursorPos;
        if (mouseDelta.magnitude < 0.1f) return rawPos;
        mouseDelta.Normalize();

        Vector2 perpendicular = Mathf.Abs(mouseDelta.y) > Mathf.Abs(mouseDelta.x)
            ? new Vector2(1f, 0f)
            : new Vector2(0f, 1f);

        float oscillation = Mathf.Sin(_distanceTraveledThisStroke * _curveFrequency * Mathf.PI * 2f)
                            * _curveAmplitude;

        return rawPos + perpendicular * oscillation;
    }

    /// <summary>
    /// WHY: Two composited forces make up near-edge displacement:
    ///
    ///   STEADY RESISTANCE — direct proportional outward offset.
    ///     Always present while painting. Scales with proximity. Not velocity-scaled.
    ///     Predictable — the player always knows how hard to push back.
    ///
    ///   CYCLIC IMPULSE — phase-driven outward lurch.
    ///     Speed AND magnitude both scale with _currentVelocityMultiplier.
    ///     Draw fast → impulse travels outward faster AND further.
    ///     Recovery speed (_returnSpeed) is NOT scaled — predictable correction window.
    ///
    ///   Total = rawPos + resistanceOffset + impulseOffset.
    /// </summary>
    private Vector2 ComputeResistancePosition(Vector2 rawPos)
    {
        Vector2 resistanceOffset = _nearestOutsideDirection
                                   * _resistanceStrength
                                   * _edgeProximity;

        TickImpulsePhase();
        Vector2 impulseOffset = _currentImpulseDirection * _currentImpulseDistance;

        return rawPos + resistanceOffset + impulseOffset;
    }

    /// <summary>
    /// WHY: Velocity scaling applied to:
    ///   • _impulseSpeed            → impulse travels outward faster at high cursor speed
    ///   • _currentEffectiveMagnitude → captured ONCE at MovingOut entry, not re-read mid-cycle
    ///
    ///   The magnitude lock is a key design decision: if magnitude were re-read every frame,
    ///   slowing down mid-impulse would shrink the target and could invert cursor direction.
    ///   Capturing it at phase entry keeps the impulse deterministic and gives the player
    ///   something stable to fight against. New velocity is applied on the NEXT cycle.
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
                    // WHY: Lock velocity-scaled magnitude at the moment the impulse fires.
                    //      Moving fast when the impulse triggers = it reaches further.
                    _currentEffectiveMagnitude = _impulseMagnitude * _currentVelocityMultiplier;
                    PickImpulseDirection();
                }
                break;

            case ImpulsePhase.MovingOut:
                // WHY: Speed scales with current velocity — faster cursor = impulse
                //      travels outward quicker, leaving less reaction time.
                _currentImpulseDistance += _impulseSpeed * _currentVelocityMultiplier * Time.deltaTime;
                if (_currentImpulseDistance >= _currentEffectiveMagnitude)
                {
                    _currentImpulseDistance = _currentEffectiveMagnitude;
                    _impulsePhase = ImpulsePhase.Returning;
                }
                break;

            case ImpulsePhase.Returning:
                // WHY: Return speed is constant — the player must learn "after the lurch,
                //      I have exactly this much time to correct." Variable return speed
                //      breaks that learned rhythm and makes the mechanic feel unfair.
                _currentImpulseDistance -= _returnSpeed * Time.deltaTime;
                if (_currentImpulseDistance <= 0f)
                {
                    _currentImpulseDistance = 0f;
                    // WHY: Cycle immediately — no second delay. After the first impulse,
                    //      difficulty is continuous. The player must stay slow.
                    _impulsePhase = ImpulsePhase.MovingOut;
                    // WHY: Re-capture magnitude each cycle so sustained fast movement
                    //      keeps escalating. Drawing slowly is the only way to reduce it.
                    _currentEffectiveMagnitude = _impulseMagnitude * _currentVelocityMultiplier;
                    PickImpulseDirection();
                }
                break;
        }
    }

    /// <summary>
    /// WHY: ±30° random variation per cycle prevents mechanical compensation.
    ///      Pure outward (0° variation) = player learns the exact angle in 2 reps.
    ///      ±30° keeps it unpredictable while remaining fundamentally "away from sketch."
    /// </summary>
    private void PickImpulseDirection()
    {
        if (_nearestOutsideDirection == Vector2.zero)
        {
            // WHY: Fallback guard. Zero vector → zero impulse offset silently.
            //      Default to right so feedback is visible if zone detection races.
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
    ///      _currentImpulseDirection intentionally NOT reset — distance is 0 so
    ///      offset = direction × 0 = (0,0). Direction is repicked on next MovingOut.
    ///      _currentEffectiveMagnitude reset to base so next entry starts fresh.
    /// </summary>
    private void ResetImpulseState()
    {
        _impulsePhase = ImpulsePhase.Inactive;
        _impulsePhaseTimer = 0f;
        _currentImpulseDistance = 0f;
        _currentEffectiveMagnitude = _impulseMagnitude;
    }

    // ============================================================
    // VISUAL CURSOR
    // ============================================================

    /// <summary>
    /// WHY: Reads _effectiveCursorPos — visual and paint always agree.
    ///      EXCEPTION: NearEdge + LMB released → cosmetic jitter added on top.
    ///      Jitter is VISUAL ONLY. Never applied while painting — the cursor would
    ///      lie about where paint is going, which is disorienting, not challenging.
    ///
    ///      Velocity scaling: jitter × _currentVelocityMultiplier.
    ///      Hover fast near an edge → cursor shakes more violently.
    ///      Punishes hasty, nervous hovering. Rewards stillness.
    /// </summary>
    private void UpdateCursorVisual()
    {
        Vector2 displayTexturePos = _effectiveCursorPos;

        if (_currentZone == CursorZone.NearEdge && !_isPainting)
        {
            float noiseX = Mathf.PerlinNoise(Time.time * _jitterSpeed, 0f) - 0.5f;
            float noiseY = Mathf.PerlinNoise(0f, Time.time * _jitterSpeed) - 0.5f;
            float effectiveJitter = _jitterIntensity * _currentVelocityMultiplier;
            displayTexturePos += new Vector2(noiseX, noiseY) * effectiveJitter * 2f;
        }

        if (displayTexturePos.x < 0 || displayTexturePos.x >= _canvasWidth ||
            displayTexturePos.y < 0 || displayTexturePos.y >= _canvasHeight)
        {
            _cursorImage.enabled = false;
            return;
        }

        _cursorImage.enabled = true;
        _cursorRect.anchoredPosition = TextureToPanelLocal(displayTexturePos);

        // WHY: Circle cursor scales with brushRadius — always shows the true paint
        //      footprint. Marker cursor uses fixed artist-set size — resizing it every
        //      frame would distort the artwork and misrepresent the actual brush tip.
        if (!_useMarkerCursor)
        {
            float cursorSize = _brushRadius * 2f * Mathf.Max(_textureToScreenScale.x,
                                                                    _textureToScreenScale.y);
            _cursorRect.sizeDelta = new Vector2(cursorSize, cursorSize);
        }
    }

    // ============================================================
    // ZONE DETECTION
    // ============================================================

    /// <summary>
    /// WHY: Always computed from RAW mouse position, never _effectiveCursorPos.
    ///      If computed from effective pos, an impulse pushing the cursor outside
    ///      the edge zone would immediately clear the zone and kill the impulse —
    ///      a negative feedback loop that collapses the mechanic in one frame.
    ///      Raw mouse = player INTENT. Effective pos = player EXPERIENCE.
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
        // WHY: Force _isPainting false immediately. Without this, a held LMB during
        //      the 1.5s countdown keeps stamping paint through UpdateEffectiveCursorPos,
        //      re-dirtying the canvas before ResetCanvas() fires and instantly
        //      re-triggering the fail condition on the next SampleProgress() call.
        _isPainting = false;
        Debug.Log("[ColoringMinigame] ❌ Failed. Auto-resetting in 1.5s. Press R to reset now.");
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
        _smoothedVelocity = 0f;
        _currentVelocityMultiplier = 1f;
        _prevRawPosInitialised = false;

        // WHY: Reset the sample timer to a full interval so SampleProgress() doesn't
        //      fire on the very next frame after reset and immediately see a clean
        //      canvas as 0% painted — which it is, but an instant re-evaluation
        //      could race with the state the player left on the previous attempt.
        _progressSampleTimer = _progressSampleInterval;

        ResetImpulseState();

        Debug.Log("[ColoringMinigame] Canvas cleared. Draw slowly — R to reset at any time.");
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
                $"► OUT  {_currentImpulseDistance:F0}px / {_currentEffectiveMagnitude:F0}px",
            ImpulsePhase.Returning =>
                $"◄ RETURN  {_currentImpulseDistance:F0}px remaining",
            _ => "???"
        };

        float resistNow = _resistanceStrength * _edgeProximity;

        _debugText.text =
            $"<b>Zone:</b> {zoneLabel}  <b>Proximity:</b> {_edgeProximity:F2}\n" +
            $"<b>Speed:</b> {_smoothedVelocity:F0} px/s  " +
            $"<b>Velocity ×:</b> {_currentVelocityMultiplier:F2}  " +
            $"(range {_minVelocityThreshold:F0}–{_maxVelocityThreshold:F0} px/s)\n" +
            $"<b>Impulse:</b> {impulseLabel}\n" +
            $"<b>Resistance offset:</b> {resistNow:F1}px  " +
            $"<b>(max {_resistanceStrength:F0}px at proximity 1.0)</b>\n" +
            $"<b>Painted Inside:</b>  {_paintedInsideFraction:P1} / {_completionThreshold:P0}\n" +
            $"<b>Painted Outside:</b> {_paintedOutsideFraction:P1} / {_outOfBoundsFailThreshold:P0}\n" +
            $"<b>Status:</b> {(_isComplete ? "✅ COMPLETE" : _hasFailed ? "❌ FAILED (auto-reset...)" : "Painting...")}  " +
            $"<b>[R]</b> reset at any time";
    }

    // ============================================================
    // LIFECYCLE — CLEANUP
    // ============================================================

    private void OnDestroy()
    {
        // WHY: We created these GameObjects — we destroy them. The scene hierarchy
        //      owns everything else. Null checks guard against Awake() early-exit path.
        if (_cursorGO != null) Destroy(_cursorGO);
        if (_debugTextGO != null) Destroy(_debugTextGO);

        // WHY: Texture2D assets created at runtime via new Texture2D() are NOT
        //      managed by Unity's asset system and must be explicitly destroyed
        //      to prevent GPU memory leaks across scene loads.
        if (_canvasTexture != null) Destroy(_canvasTexture);
        if (_cursorTexture != null) Destroy(_cursorTexture);

        Cursor.visible = true;
    }

    // ============================================================
    // PUBLIC API (IState integration)
    // ============================================================

    public bool IsComplete => _isComplete;
    public bool HasFailed => _hasFailed;
    public float PaintedInsideFraction => _paintedInsideFraction;
    public float PaintedOutsideFraction => _paintedOutsideFraction;
    public CursorZone CurrentZone => _currentZone;
    public float CurrentVelocityMultiplier => _currentVelocityMultiplier;

    public void Exit()
    {
        Cursor.visible = true;
        CancelInvoke(nameof(ResetCanvas));
        // WHY: We no longer own the Canvas — the scene hierarchy does.
        //      Destroying it here would nuke the entire Minigames hierarchy.
        //      The MinigameState or scene lifecycle is responsible for teardown.
    }

    // ============================================================
    // EDITOR VALIDATION
    // ============================================================

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
        if (_maxVelocityMultiplier < 1f) _maxVelocityMultiplier = 1f;
        if (_maxVelocityThreshold <= _minVelocityThreshold)
            _maxVelocityThreshold = _minVelocityThreshold + 1f;
        if (_velocitySmoothing < 0.1f) _velocitySmoothing = 0.1f;
    }
#endif
}
