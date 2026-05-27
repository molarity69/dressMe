// ============================================================
// Coloring.cs (v7 — Paint Visibility Fix + Mask Diagnostics + Curve Removed)
//
// BUGS FIXED FROM v6:
//   [1] PAINT INVISIBLE — _paintingDisplay.color is forced to Color.white in Awake.
//       A RawImage with alpha=0 (set accidentally in the Inspector or left from a
//       previous component swap) makes paint invisible even though StampBrush writes
//       pixels correctly — which is why the progress tracker still counted them.
//
//   [2] OUTSIDE DETECTION BROKEN — mask texture compression was the culprit.
//       DXT1/BC1 compression turns hard black edges grey (~0.3–0.6 grayscale),
//       pushing them above the 0.5 threshold. Every pixel reads as "inside",
//       so paintedOutside is always 0 and zone detection never finds an edge.
//       Fix: Awake now logs white/black pixel counts and warns loudly if the mask
//       appears all-white. Set mask Compression to None in Import Settings.
//
//   [3] CURVED DRAWING REMOVED — ComputeCurvedPosition and all related fields
//       (_curveAmplitude, _curveFrequency, _runtimeCurveAmplitude,
//       _distanceTraveledThisStroke) are gone. Middle zone tracks raw mouse 1:1.
//
// ALL v6 FIXES RETAINED:
//   • Runtime scaling (_runtimeBrushRadius etc.) — auto-derived from canvasWidth/512f
//   • LMB-gated jitter and resistance (Fix A + B)
//   • Velocity multiplier decays on LMB release (Fix C)
//   • _runtimeEdgeZoneWidth used as loop limit in UpdateCursorZone (edge loop fix)
//   • StampBrush uses _runtimeBrushRadius (paint regression fix)
//
// MASK TEXTURE IMPORT REQUIREMENTS:
//   • Read/Write Enabled : ON
//   • Compression        : None  (NEVER DXT1/BC1/ETC — see bug [2] above)
//
// PLACE ON: 'Coloring' GameObject
//
// INSPECTOR INJECTIONS (Required):
//   _injectedCanvas          → Minigames (root Canvas)
//   _injectedPaintingDisplay → Color Area (must be RawImage, NOT Image)
//   _injectedSketchDisplay   → Outline (Image component)
//   _injectedPanelRect       → Coloring (its own RectTransform)
// ============================================================

using UnityEngine;
using UnityEngine.UI;

public class Coloring_V2 : MonoBehaviour
{
    // ============================================================
    // SCENE REFERENCES — Injected via Inspector
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

    [Tooltip(
        "Mask: White = valid paint area. Black = outline/outside.\n" +
        "IMPORT REQUIREMENTS (both mandatory):\n" +
        "  • Read/Write Enabled : ON\n" +
        "  • Compression        : None\n" +
        "WHY: DXT1/BC1 compression turns hard black edges grey (~0.3–0.6 grayscale),\n" +
        "pushing them above the 0.5 threshold. The entire mask reads as white,\n" +
        "so zone detection never finds an edge and outside paint is never counted."
    )]
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
    [Tooltip("Radius in texture pixels at 512px reference resolution. Auto-scales to actual canvas size.")]
    [SerializeField] private float _brushRadius = 12f;

    [Range(0.1f, 1f)]
    [Tooltip("1 = hard marker edge. 0.1 = soft airbrush. 0.75 = default marker.")]
    [SerializeField] private float _brushHardness = 0.75f;

    [Header("Zone Detection")]
    [Tooltip("Pixel distance from outline that counts as 'near edge'. Authored at 512px resolution, auto-scaled.")]
    [SerializeField] private float _edgeZoneWidth = 30f;

    [Tooltip("Radial sample count for edge detection. 8 = 45° increments.")]
    [SerializeField] private int _edgeSampleDirections = 8;

    [Header("Edge Zone — Visual Jitter (LMB Held)")]
    [Tooltip(
        "BASE peak displacement of the cursor shake near edges while painting.\n" +
        "Actual intensity = this × _currentVelocityMultiplier.\n" +
        "Only fires while LMB is held — hovering near the edge is calm."
    )]
    [SerializeField] private float _jitterIntensity = 10f;

    [Tooltip("Perlin noise cycle speed. Higher = faster shake.")]
    [SerializeField] private float _jitterSpeed = 18f;

    [Header("Edge Zone — Steady Resistance (LMB Held)")]
    [Tooltip(
        "DIRECT pixel offset outward while painting near the edge.\n" +
        "proximity 1.0 (on outline)  → full offset\n" +
        "proximity 0.5 (mid edge)    → half offset\n" +
        "proximity 0.0 (zone entry)  → zero offset"
    )]
    [SerializeField] private float _resistanceStrength = 15f;

    [Header("Edge Zone — Cyclic Impulse (LMB Held)")]
    [Tooltip("Seconds after entering edge zone before the first impulse fires. Grace window per stroke entry.")]
    [SerializeField] private float _impulseDelayDuration = 1f;

    [Tooltip("BASE outward travel speed (texture px/s). Scales with velocity.")]
    [SerializeField] private float _impulseSpeed = 55f;

    [Tooltip("BASE max push distance from raw mouse (texture px). Scales with velocity.")]
    [SerializeField] private float _impulseMagnitude = 30f;

    [Tooltip("Return speed (texture px/s) after peak impulse. NOT velocity-scaled — constant recovery keeps correction learnable.")]
    [SerializeField] private float _returnSpeed = 80f;

    [Header("Velocity-Based Difficulty")]
    [Tooltip(
        "Cursor speed (texture px/s) below which multiplier = 1.0 (base difficulty).\n" +
        "Authored at 512px resolution, auto-scaled."
    )]
    [SerializeField] private float _minVelocityThreshold = 20f;

    [Tooltip(
        "Cursor speed (texture px/s) at which multiplier reaches its ceiling.\n" +
        "Authored at 512px resolution, auto-scaled."
    )]
    [SerializeField] private float _maxVelocityThreshold = 200f;

    [Tooltip(
        "Multiplier ceiling at peak cursor speed.\n" +
        "Applied to jitter intensity AND impulse speed/magnitude.\n" +
        "1.0 = no scaling. 2.5 = 2.5× harder at max speed."
    )]
    [SerializeField] private float _maxVelocityMultiplier = 2.5f;

    [Tooltip("Exponential smoothing factor for velocity. Higher = snappier. Recommended: 6–10.")]
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
    private bool _useMarkerCursor;

    // Runtime GameObjects we own — tracked for explicit OnDestroy cleanup
    private GameObject _cursorGO;
    private GameObject _debugTextGO;

    // Mask data
    private Color[] _maskPixels;
    private int _totalMaskPixels;

    // Input state
    private bool _isPainting;
    private int _currentColorIndex;
    private Color _currentColor;

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
    //   Inactive  ──► (NearEdge + LMB held)              ──► Waiting
    //   Waiting   ──► (_impulseDelayDuration elapsed)     ──► MovingOut
    //   MovingOut ──► (distance == effectiveMagnitude)    ──► Returning
    //   Returning ──► (distance == 0)                     ──► MovingOut  [cycles]
    //   Reset to Inactive: LMB up, zone exits NearEdge, new stroke, R pressed.
    private enum ImpulsePhase { Inactive, Waiting, MovingOut, Returning }
    private ImpulsePhase _impulsePhase = ImpulsePhase.Inactive;
    private float _impulsePhaseTimer = 0f;
    private float _currentImpulseDistance = 0f;
    private Vector2 _currentImpulseDirection;

    // WHY: Locked at MovingOut entry so mid-cycle velocity changes don't shift
    //      the goalposts while the impulse is already travelling.
    private float _currentEffectiveMagnitude;

    // Velocity tracking
    private Vector2 _prevRawTexturePos;
    private bool _prevRawPosInitialised;
    private float _smoothedVelocity;
    private float _currentVelocityMultiplier = 1f;

    // Progress state
    private float _progressSampleTimer;
    private float _paintedInsideFraction;
    private float _paintedOutsideFraction;
    private bool _isComplete;
    private bool _hasFailed;

    // WHY: All Inspector pixel-unit values are authored at 512px reference resolution.
    //      These runtime versions are multiplied by (canvasWidth / 512f) once in Awake
    //      so every downstream system automatically handles any texture size.
    private float _runtimeBrushRadius;
    private float _runtimeEdgeZoneWidth;
    private float _runtimeImpulseMagnitude;
    private float _runtimeResistanceStrength;
    private float _runtimeMinVelocityThreshold;
    private float _runtimeMaxVelocityThreshold;

    // ============================================================
    // INITIALIZATION
    // ============================================================

    private void Awake()
    {
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

        _canvas = _injectedCanvas;
        _paintingDisplay = _injectedPaintingDisplay;
        _sketchDisplay = _injectedSketchDisplay;
        _panelRect = _injectedPanelRect;

        // ── FIX [1]: Force RawImage color to opaque white ────────────────────
        // WHY: A RawImage with color.a == 0 (set accidentally in the Inspector,
        //      or left over from a previous Image component) renders the texture
        //      as fully transparent. StampBrush still writes pixels and the progress
        //      tracker still reads them — so paint "works" but is invisible.
        //      Forcing white here guarantees the texture is always visible regardless
        //      of what the Inspector value was.
        Color prevColor = _paintingDisplay.color;
        _paintingDisplay.color = Color.white;
        if (prevColor != Color.white)
            Debug.LogWarning($"[ColoringMinigame] ⚠️ RawImage color was {prevColor} — " +
                             "forced to Color.white. If paint was invisible, this was the cause.");

        BuildCursorVisual();

        if (_showProgressOnScreen)
            BuildDebugText();

        // ── Mask setup ───────────────────────────────────────────────────────
        _maskPixels = _maskTexture.GetPixels();
        _canvasWidth = _maskTexture.width;
        _canvasHeight = _maskTexture.height;

        // WHY: Single auto-derived scale factor. No Inspector float to go stale.
        float scale = _canvasWidth / 512f;
        _runtimeBrushRadius = _brushRadius * scale;
        _runtimeEdgeZoneWidth = _edgeZoneWidth * scale;
        _runtimeImpulseMagnitude = _impulseMagnitude * scale;
        _runtimeResistanceStrength = _resistanceStrength * scale;
        _runtimeMinVelocityThreshold = _minVelocityThreshold * scale;
        _runtimeMaxVelocityThreshold = _maxVelocityThreshold * scale;

        Vector2 panelSize = _panelRect.rect.size;
        _textureToScreenScale = new Vector2(panelSize.x / _canvasWidth,
                                            panelSize.y / _canvasHeight);

        // ── FIX [2]: Mask diagnostics ────────────────────────────────────────
        // WHY: Count white AND black pixels separately so a compressed mask is
        //      immediately visible in the console rather than silently breaking
        //      zone detection and outside-paint tracking.
        _totalMaskPixels = 0;
        int blackMaskPixels = 0;
        for (int i = 0; i < _maskPixels.Length; i++)
        {
            if (_maskPixels[i].grayscale > 0.5f) _totalMaskPixels++;
            else blackMaskPixels++;
        }

        Debug.Log($"[ColoringMinigame] Mask: {_totalMaskPixels} white px, " +
                  $"{blackMaskPixels} black px, {_maskPixels.Length} total. " +
                  $"Canvas {_canvasWidth}×{_canvasHeight}, scale={scale:F2}.");

        if (blackMaskPixels == 0)
        {
            Debug.LogError(
                "[ColoringMinigame] ⚠️ Mask has ZERO black pixels — the entire mask reads as white.\n" +
                "Root cause: texture compression (DXT1/BC1/ETC) turns hard black edges grey,\n" +
                "pushing them above the 0.5 grayscale threshold.\n" +
                "FIX: Select the mask texture → Import Settings → Compression → None."
            );
        }

        if (_totalMaskPixels == 0)
        {
            Debug.LogError("[ColoringMinigame] Mask has zero white pixels! " +
                           "Check: (1) Read/Write enabled, (2) white fill inside the outline.");
            return;
        }

        // ── Canvas texture ───────────────────────────────────────────────────
        _canvasTexture = new Texture2D(_canvasWidth, _canvasHeight, TextureFormat.RGBA32, false);
        _canvasTexture.filterMode = FilterMode.Bilinear;
        Color[] clear = new Color[_canvasWidth * _canvasHeight];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        _canvasTexture.SetPixels(clear);
        _canvasTexture.Apply();

        // WHY: This is the line that makes paint visible. Without it, StampBrush
        //      writes to _canvasTexture but nothing is ever displayed.
        _paintingDisplay.texture = _canvasTexture;

        if (_sketchTexture != null && _sketchDisplay.sprite == null)
            _sketchDisplay.sprite = TextureToSprite(_sketchTexture);

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
        _currentEffectiveMagnitude = _runtimeImpulseMagnitude;

        Cursor.visible = false;

        Debug.Log($"[ColoringMinigame] Initialised. " +
                  $"Cursor: {(_useMarkerCursor ? "Marker sprite" : "Generated circle")}.");
    }

    private void BuildCursorVisual()
    {
        _cursorGO = new GameObject("CursorVisual");
        _cursorGO.transform.SetParent(_panelRect, false);

        _cursorImage = _cursorGO.AddComponent<Image>();
        // WHY: raycastTarget = false — cursor visual must never eat mouse events.
        _cursorImage.raycastTarget = false;

        _cursorRect = _cursorGO.GetComponent<RectTransform>();
        _cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorRect.anchorMax = new Vector2(0.5f, 0.5f);

        if (_markerCursorSprite != null)
        {
            _cursorImage.sprite = _markerCursorSprite;
            _cursorRect.pivot = _markerCursorPivot;
            _cursorRect.sizeDelta = _markerCursorSize;
            _useMarkerCursor = true;
        }
        else
        {
            _cursorRect.pivot = new Vector2(0.5f, 0.5f);
            _cursorRect.sizeDelta = new Vector2(_brushRadius * 2f, _brushRadius * 2f);
            _useMarkerCursor = false;
        }
    }

    private void BuildDebugText()
    {
        _debugTextGO = new GameObject("DebugText");
        _debugTextGO.transform.SetParent(_canvas.transform, false);

        _debugText = _debugTextGO.AddComponent<Text>();
        _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _debugText.color = Color.black;
        _debugText.alignment = TextAnchor.UpperLeft;
        _debugText.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);

        RectTransform textRT = _debugTextGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 1);
        textRT.anchorMax = new Vector2(0, 1);
        textRT.pivot = new Vector2(0, 1);
        textRT.anchoredPosition = new Vector2(20, -20);
        textRT.sizeDelta = new Vector2(Screen.width * 0.3f, Screen.height * 0.35f);
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
        // WHY: Velocity before zone/cursor so _currentVelocityMultiplier is fresh
        //      when jitter and impulse read it this same frame.
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
            _cursorImage.color = _currentColor;
        }
    }

    private void HandlePaintingInput()
    {
        // WHY: Block new strokes while failed or complete — prevents held LMB during
        //      the 1.5s fail countdown from re-dirtying the canvas before ResetCanvas fires.
        if (Input.GetMouseButtonDown(0) && !_isComplete && !_hasFailed)
        {
            _isPainting = true;
            _lastEffectiveCursorPos = _effectiveCursorPos;
            _effectivePosInitialised = true;
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

    // WHY: CancelInvoke prevents double-reset when R is pressed during the
    //      auto-reset countdown — without it ResetCanvas fires twice.
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
    ///      average to prevent single-frame delta spikes from jarring mid-stroke jumps.
    ///
    ///      FIX C: Multiplier only escalates while _isPainting.
    ///      Decays toward 1.0 when not painting so hover speed cannot pre-load
    ///      difficulty before the player clicks.
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
            //      elevated when the cursor leaves and re-enters. Without this,
            //      a fast exit-and-re-entry computes a huge outside→inside delta
            //      as a false speed spike on the first frame back inside.
            _smoothedVelocity = Mathf.Lerp(_smoothedVelocity, 0f,
                                                Time.deltaTime * _velocitySmoothing);
            _prevRawPosInitialised = false;
        }

        if (_isPainting)
        {
            // WHY: Use runtime-scaled thresholds so authored values stay proportional
            //      at any canvas resolution without manual recalculation.
            float t = Mathf.InverseLerp(_runtimeMinVelocityThreshold,
                                         _runtimeMaxVelocityThreshold, _smoothedVelocity);
            _currentVelocityMultiplier = Mathf.Lerp(1f, _maxVelocityMultiplier, t);
        }
        else
        {
            // WHY: FIX C — decay toward baseline when not painting so hover speed
            //      cannot pre-load difficulty before the player clicks. Escalation
            //      is earned stroke by stroke, not accumulated while hovering.
            _currentVelocityMultiplier = Mathf.Lerp(_currentVelocityMultiplier, 1f,
                                                     Time.deltaTime * _velocitySmoothing);
        }
    }


    // ============================================================
    // EFFECTIVE CURSOR POSITION — SINGLE SOURCE OF TRUTH
    // ============================================================

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

        switch (_currentZone)
        {
            case CursorZone.Middle:
                // WHY: Impulse resets in the safe zone so the delay restarts next time
                //      the player enters the edge — rewards retreating to the middle.
                //      Curved drawing removed — middle zone tracks raw mouse 1:1.
                ResetImpulseState();
                _effectiveCursorPos = rawTexturePos;
                break;

            case CursorZone.NearEdge:
                if (_isPainting)
                {
                    // WHY: FIX B — full resistance + impulse stack only while LMB held.
                    //      Hover near edge = calm cursor for planning. LMB = edge fights back.
                    _effectiveCursorPos = ComputeResistancePosition(rawTexturePos);
                }
                else
                {
                    // WHY: FIX B — hover near edge = cursor tracks raw mouse 1:1.
                    //      Grace countdown only begins on LMB down, not on proximity.
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

        // WHY: Guard _hasFailed and _isComplete — prevents paint being stamped
        //      during the fail countdown or after the minigame is already won.
        if (_isPainting && !_hasFailed && !_isComplete)
        {
            float dist = Vector2.Distance(_lastEffectiveCursorPos, _effectiveCursorPos);
            if (dist > 0.01f)
            {
                // WHY: _runtimeBrushRadius used here — step count and stamp radius
                //      must use the same scaled value or gaps appear at large canvas sizes.
                int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (_runtimeBrushRadius * 0.5f)));
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    StampBrush(Vector2.Lerp(_lastEffectiveCursorPos, _effectiveCursorPos, t),
                               _currentColor);
                }
            }
            else
            {
                // Stationary click: one dot so clicking immediately marks the canvas.
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
    /// WHY: Two composited forces — steady resistance (proportional, always present
    ///      while painting) + cyclic impulse (velocity-scaled speed and magnitude).
    ///      Total = rawPos + resistanceOffset + impulseOffset.
    /// </summary>
    private Vector2 ComputeResistancePosition(Vector2 rawPos)
    {
        Vector2 resistanceOffset = _nearestOutsideDirection
                                   * _runtimeResistanceStrength
                                   * _edgeProximity;

        TickImpulsePhase();
        Vector2 impulseOffset = _currentImpulseDirection * _currentImpulseDistance;

        return rawPos + resistanceOffset + impulseOffset;
    }

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
                    // WHY: Lock velocity-scaled magnitude at impulse fire moment.
                    //      Moving fast when it triggers = it reaches further.
                    _currentEffectiveMagnitude = _runtimeImpulseMagnitude * _currentVelocityMultiplier;
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
                // WHY: Return speed constant — player learns "after the lurch I have
                //      exactly this much time to correct." Variable return breaks that rhythm.
                _currentImpulseDistance -= _returnSpeed * Time.deltaTime;
                if (_currentImpulseDistance <= 0f)
                {
                    _currentImpulseDistance = 0f;
                    _impulsePhase = ImpulsePhase.MovingOut;
                    _currentEffectiveMagnitude = _runtimeImpulseMagnitude * _currentVelocityMultiplier;
                    PickImpulseDirection();
                }
                break;
        }
    }

    private void PickImpulseDirection()
    {
        if (_nearestOutsideDirection == Vector2.zero)
        {
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

    private void ResetImpulseState()
    {
        _impulsePhase = ImpulsePhase.Inactive;
        _impulsePhaseTimer = 0f;
        _currentImpulseDistance = 0f;
        _currentEffectiveMagnitude = _runtimeImpulseMagnitude;
    }

    // ============================================================
    // VISUAL CURSOR
    // ============================================================

    private void UpdateCursorVisual()
    {
        Vector2 displayTexturePos = _effectiveCursorPos;

        // WHY: FIX A — jitter gates on _isPainting. Hovering near the edge is calm
        //      so the player can plan their stroke. The moment LMB is held, it kicks in.
        if (_currentZone == CursorZone.NearEdge && _isPainting)
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

        if (!_useMarkerCursor)
        {
            float cursorSize = _runtimeBrushRadius * 2f *
                               Mathf.Max(_textureToScreenScale.x, _textureToScreenScale.y);
            _cursorRect.sizeDelta = new Vector2(cursorSize, cursorSize);
        }
    }

    // ============================================================
    // ZONE DETECTION
    // ============================================================

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
        // WHY: _runtimeEdgeZoneWidth as loop limit — raw _edgeZoneWidth diverges from
        //      all other scaled values on any canvas that isn't exactly 512px wide.
        int edgeStepLimit = Mathf.CeilToInt(_runtimeEdgeZoneWidth);

        for (int i = 0; i < _edgeSampleDirections; i++)
        {
            float angle = (i / (float)_edgeSampleDirections) * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            for (int step = 1; step <= edgeStepLimit; step++)
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

        if (minDist <= _runtimeEdgeZoneWidth)
        {
            _currentZone = CursorZone.NearEdge;
            _edgeProximity = 1f - (minDist / _runtimeEdgeZoneWidth);
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
        // WHY: _runtimeBrushRadius — raw _brushRadius ignores canvas scale,
        //      causing mismatched coordinate spaces on non-512px canvases.
        int r = Mathf.CeilToInt(_runtimeBrushRadius);

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

                if (dist > _runtimeBrushRadius) continue;

                float normalizedDist = dist / _runtimeBrushRadius;
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
        Debug.Log("[ColoringMinigame] 🎉 Transitioning to NarrativeState...");
    }

    private void OnFail()
    {
        // WHY: Force _isPainting false immediately so held LMB during the 1.5s
        //      countdown doesn't keep stamping paint and immediately re-trigger fail.
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
        _effectivePosInitialised = false;
        _smoothedVelocity = 0f;
        _currentVelocityMultiplier = 1f;
        _prevRawPosInitialised = false;
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
            CursorZone.Middle => "MIDDLE (raw)",
            CursorZone.NearEdge => _isPainting
                                    ? "NEAR EDGE (resistance + impulse + jitter)"
                                    : "NEAR EDGE (calm — hover)",
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

        float resistNow = _runtimeResistanceStrength * _edgeProximity;

        _debugText.text =
            $"<b>Zone:</b> {zoneLabel}  <b>Proximity:</b> {_edgeProximity:F2}\n" +
            $"<b>Speed:</b> {_smoothedVelocity:F0} px/s  " +
            $"<b>Velocity ×:</b> {_currentVelocityMultiplier:F2}  " +
            $"(range {_runtimeMinVelocityThreshold:F0}–{_runtimeMaxVelocityThreshold:F0} px/s)\n" +
            $"<b>Impulse:</b> {impulseLabel}\n" +
            $"<b>Resistance offset:</b> {resistNow:F1}px  " +
            $"(max {_runtimeResistanceStrength:F0}px at proximity 1.0)\n" +
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
        if (_cursorGO != null) Destroy(_cursorGO);
        if (_debugTextGO != null) Destroy(_debugTextGO);
        if (_canvasTexture != null) Destroy(_canvasTexture);
        if (_cursorTexture != null) Destroy(_cursorTexture);

        Cursor.visible = true;
    }

    // ============================================================
    // PUBLIC API
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
