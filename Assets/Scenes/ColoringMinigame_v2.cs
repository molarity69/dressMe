// ============================================================
// Coloring.cs (v6 — Unified Fix: Paint Regression + LMB-Gated Effects)
//
// RESPONSIBILITY: Self-contained coloring minigame simulating
// the motor struggle of painting within lines.
//
// CHANGES FROM v5:
//   [1] PAINT REGRESSION FIXED — StampBrush and step calculation now both use
//       _runtimeBrushRadius (canvas-scale-corrected). v5 used raw _brushRadius
//       in StampBrush while zone detection used scaled values, causing zone
//       misclassification on non-512px canvases that silently suppressed painting.
//   [2] EDGE ZONE LOOP FIXED — UpdateCursorZone now uses _runtimeEdgeZoneWidth
//       as the step limit instead of raw _edgeZoneWidth. These diverged on any
//       canvas not exactly 512px wide, making zone detection inconsistent.
//   [3] FIX A — Jitter now fires on _isPainting == true (LMB held), not on hover.
//       Hovering near the edge is calm. Pressing LMB is when the cursor fights back.
//   [4] FIX B — NearEdge hover path explicitly confirmed: cursor tracks raw mouse
//       1:1 when not painting. Grace countdown only begins on LMB down.
//   [5] FIX C — Velocity multiplier only escalates while _isPainting. Decays toward
//       1.0 when not painting so hover speed cannot pre-load difficulty before a stroke.
//   [6] _canvasScale Inspector field removed. Scale is always auto-derived from
//       _canvasWidth / 512f. One source of truth, no stale Inspector values.
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

public class ColoringMinigame_v2 : MonoBehaviour
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
    [Tooltip("Visible sketch outline. Transparent background, opaque black lines.")]
    [SerializeField] private Texture2D _sketchTexture;

    [Tooltip(
        "Mask: White = valid paint area (T-shirt interior). Black = outline/outside.\n" +
        "MUST have Read/Write enabled in Import Settings.\n" +
        "LEAVE NULL to auto-derive from _sketchTexture at runtime (recommended for 4K assets).\n" +
        "Auto-derive uses a flood-fill from the T-shirt's bounding-box center — works as long\n" +
        "as the outline is a single closed shape with no interior disconnected blobs."
    )]
    [SerializeField] private Texture2D _maskTexture;

    [Tooltip(
        "Paper boundary mask. White = on the A4 paper. Black = outside.\n" +
        "Paint is blocked (not applied, not penalised) outside white pixels.\n" +
        "LEAVE NULL to skip paper boundary enforcement (paint allowed everywhere).\n" +
        "MUST have Read/Write enabled in Import Settings."
    )]
    [SerializeField] private Texture2D _paperMaskTexture;

    [Header("Resolution")]
    [Tooltip(
        "Reference resolution that all Inspector pixel-unit values (brush radius, edge zone, etc.) are authored at.\n" +
        "512 = legacy default. For 4K assets author your brush/edge values at 4096 and set this to 4096.\n" +
        "Recommended: match this to your actual texture width so Inspector values are literal pixel counts."
    )]
    [SerializeField] private float _referenceResolution = 512f;


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

    [Header("Middle Zone — Curved Drawing (LMB Held)")]
    [Tooltip("Peak perpendicular wave displacement in texture pixels at 512px resolution.")]
    [SerializeField] private float _curveAmplitude = 28f;

    [Tooltip("Wave cycles per pixel traveled. 0.015 ≈ 1 full wave per 67px.")]
    [SerializeField] private float _curveFrequency = 0.015f;

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

    [Header("Audio")]
    [Tooltip("Injected AudioManager. Provides the loop SFX channel for drawing sound.")]
    [SerializeField] private AudioManager _audioManager;

    [Tooltip("Sound that plays while LMB is held and painting.")]
    [SerializeField] private AudioClip _drawingSound;

    [SerializeField] private GameManager _gameManager;

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
    private Texture2D _cursorTexture;     // Only allocated for circle fallback
    private Vector2 _textureToScreenScale;
    private bool _useMarkerCursor;

    // Runtime GameObjects we own — tracked for explicit OnDestroy cleanup
    private GameObject _cursorGO;
    private GameObject _debugTextGO;

    // Mask data
    private Color[] _maskPixels;
    private int _totalMaskPixels;
    // WHY: Kept separate from _maskPixels so paper-boundary blocking and
    //      T-shirt-interior scoring are two independent queries with zero coupling.
    //      Paper block = silent (no paint applied, no fail tick).
    //      T-shirt outside = loud (contributes to _paintedOutsideFraction → fail).
    private Color[] _paperMaskPixels;
    private bool _hasPaperMask;


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

    // WHY: Locked at MovingOut entry. Mid-cycle velocity changes don't shift the
    //      goalposts while the impulse is already travelling. New velocity on next cycle.
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
    //      StampBrush, step calculations, and zone detection ALL use runtime values —
    //      this is the root fix for the v5 paint regression.
    private float _runtimeBrushRadius;
    private float _runtimeEdgeZoneWidth;
    private float _runtimeCurveAmplitude;
    private float _runtimeImpulseMagnitude;
    private float _runtimeResistanceStrength;
    private float _runtimeMinVelocityThreshold;
    private float _runtimeMaxVelocityThreshold;

    // Add to PRIVATE STATE section, alongside _canvasWidth/_canvasHeight:
    private int _contentOffsetX;   // left edge of detected content in original texture
    private int _contentOffsetY;   // bottom edge of detected content in original texture


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

        BuildCursorVisual();

        if (_showProgressOnScreen)
            BuildDebugText();

        // Add to the existing null check group — not a hard stop, just a warning
        if (_audioManager == null)
            Debug.LogWarning("[ColoringMinigame] _audioManager not assigned — drawing audio disabled.");

        // ── Resolve mask texture ─────────────────────────────────────────────────
        // WHY: If the designer supplies a hand-painted mask we use it directly.
        //      If not (null), we flood-fill the outline texture at runtime.
        //      This removes a manual asset step and stays correct if the outline art changes —
        //      regenerate is free, re-painting a mask by hand is error-prone at 4K.
        if (_maskTexture == null)
        {
            if (_sketchTexture == null)
            {
                Debug.LogError("[ColoringMinigame] Both _maskTexture and _sketchTexture are null. " +
                               "Assign at least _sketchTexture so the interior mask can be derived.");
                enabled = false;
                return;
            }
            _maskTexture = DeriveMaskFromOutline(_sketchTexture);
            if (_maskTexture == null)
            {
                Debug.LogError("[ColoringMinigame] Flood-fill mask derivation failed. " +
                               "Ensure _sketchTexture has Read/Write enabled and contains a closed outline.");
                enabled = false;
                return;
            }
            Debug.Log("[ColoringMinigame] Interior mask auto-derived from outline texture.");
        }

        _maskPixels = _maskTexture.GetPixels();
        _canvasWidth = _maskTexture.width;
        _canvasHeight = _maskTexture.height;

        // ── Paper boundary mask ──────────────────────────────────────────────────
        // WHY: Optional. When assigned, StampBrush silently skips pixels outside white area.
        //      We validate dimensions match so coordinate lookups stay in the same space.
        if (_paperMaskTexture != null)
        {
            if (_paperMaskTexture.width != _canvasWidth || _paperMaskTexture.height != _canvasHeight)
            {
                Debug.LogError(
                    $"[ColoringMinigame] _paperMaskTexture size ({_paperMaskTexture.width}×{_paperMaskTexture.height}) " +
                    $"does not match _maskTexture size ({_canvasWidth}×{_canvasHeight}). " +
                    "Both textures must be identical resolution. Paper boundary disabled."
                );
                _hasPaperMask = false;
            }
            else
            {
                _paperMaskPixels = _paperMaskTexture.GetPixels();
                _hasPaperMask = true;
                Debug.Log("[ColoringMinigame] Paper boundary mask loaded.");
            }
        }
        else
        {
            _hasPaperMask = false;
            Debug.Log("[ColoringMinigame] No paper mask assigned — paint unrestricted by paper bounds.");
        }

        // ── Scale all authored pixel values to actual canvas resolution ──────────
        // WHY: _referenceResolution is the resolution the designer thought in when
        //      setting Inspector values. Scale once here; every downstream system reads
        //      a _runtime* value. One source of truth, zero per-system manual conversion.
        float scale = _canvasWidth / _referenceResolution;
        _runtimeBrushRadius = _brushRadius * scale;
        _runtimeEdgeZoneWidth = _edgeZoneWidth * scale;
        _runtimeCurveAmplitude = _curveAmplitude * scale;
        _runtimeImpulseMagnitude = _impulseMagnitude * scale;
        _runtimeResistanceStrength = _resistanceStrength * scale;
        _runtimeMinVelocityThreshold = _minVelocityThreshold * scale;
        _runtimeMaxVelocityThreshold = _maxVelocityThreshold * scale;

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
                           "Check: (1) mask texture Read/Write enabled, " +
                           "(2) white fill inside the outline.");
            return;
        }

        _canvasTexture = new Texture2D(_canvasWidth, _canvasHeight, TextureFormat.RGBA32, false);
        _canvasTexture.filterMode = FilterMode.Bilinear;
        Color[] clear = new Color[_canvasWidth * _canvasHeight];
        for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
        _canvasTexture.SetPixels(clear);
        _canvasTexture.Apply();

        // WHY: This is the line that makes paint visible. If _paintingDisplay.texture
        //      is null, SetPixels/Apply work but nothing ever renders.
        _paintingDisplay.texture = _canvasTexture;

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
        _currentEffectiveMagnitude = _runtimeImpulseMagnitude;

        Cursor.visible = false;

        Debug.Log($"[ColoringMinigame] Initialised. {_canvasWidth}×{_canvasHeight}, " +
                  $"reference={_referenceResolution}px, scale={scale:F2}, " +
                  $"{_totalMaskPixels} paintable px, paper mask={_hasPaperMask}. " +
                  $"Cursor: {(_useMarkerCursor ? "Marker sprite" : "Generated circle")}.");
    }


    // WHY: Isolated so swapping cursor implementation only touches this method.
    private void BuildCursorVisual()
    {
        _cursorGO = new GameObject("CursorVisual");
        _cursorGO.transform.SetParent(_panelRect.parent, false);

        _cursorImage = _cursorGO.AddComponent<Image>();
        // WHY: raycastTarget = false — cursor visual must never eat mouse events
        //      that should reach the panel beneath it.
        _cursorImage.raycastTarget = false;

        _cursorRect = _cursorGO.GetComponent<RectTransform>();
        _cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorRect.anchorMax = new Vector2(0.5f, 0.5f);

        if (_markerCursorSprite != null)
        {
            // WHY: Pivot at the marker tip means anchoredPosition maps DIRECTLY to the
            //      paint application point — zero offset math required downstream.
            _cursorImage.sprite = _markerCursorSprite;
            _cursorRect.pivot = _markerCursorPivot;
            _cursorRect.sizeDelta = _markerCursorSize;
            _useMarkerCursor = true;
        }
        else
        {
            // WHY: Fallback circle for development without art assets.
            //      sizeDelta is updated every frame in UpdateCursorVisual to match brushRadius.
            _cursorRect.pivot = new Vector2(0.5f, 0.5f);
            _cursorRect.sizeDelta = new Vector2(_brushRadius * 2f, _brushRadius * 2f);
            _useMarkerCursor = false;
        }
    }

    // WHY: Parented to root Canvas so it renders above all panel content
    //      and is never clipped by the panel's RectTransform bounds.
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

    /// <summary>
    /// WHY: Eliminates the manual "paint the interior white" asset step.
    ///      The outline texture already encodes everything we need — black pixels ARE the boundary.
    ///      We flood-fill from the bounding-box center outward, treating any pixel whose alpha > 0
    ///      as a wall (outline pixel). Everything the fill reaches = interior = white in the output mask.
    ///
    ///      Edge case handled: if the center pixel happens to land on an outline pixel (unlucky bounding
    ///      box on an asymmetric shape), we spiral outward from center until we find a transparent pixel.
    ///      If the shape is not a closed outline, the fill leaks to canvas edges — logged as a warning.
    /// </summary>
    private Texture2D DeriveMaskFromOutline(Texture2D outlineTexture)
    {
        int w = outlineTexture.width;
        int h = outlineTexture.height;
        Color[] src = outlineTexture.GetPixels();

        // ── Find bounding box of all opaque (outline) pixels ────────────────────
        int bboxMinX = w, bboxMaxX = 0, bboxMinY = h, bboxMaxY = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (src[y * w + x].a > 0.1f)
                {
                    if (x < bboxMinX) bboxMinX = x;
                    if (x > bboxMaxX) bboxMaxX = x;
                    if (y < bboxMinY) bboxMinY = y;
                    if (y > bboxMaxY) bboxMaxY = y;
                }

        if (bboxMaxX <= bboxMinX || bboxMaxY <= bboxMinY)
        {
            Debug.LogError("[DeriveMaskFromOutline] No opaque pixels found in outline texture. " +
                           "Check Read/Write is enabled on the texture import settings.");
            return null;
        }

        // ── Find a transparent seed pixel near the bounding-box center ──────────
        // WHY: BFS from bbox center. If center is on the outline, spiral outward.
        //      We stop the moment we find a transparent pixel — that's our interior seed.
        int cx = (bboxMinX + bboxMaxX) / 2;
        int cy = (bboxMinY + bboxMaxY) / 2;
        Vector2Int seed = Vector2Int.zero;
        bool seedFound = false;

        // Spiral search: radius 0 → (half the shorter bbox dimension)
        int maxSearchRadius = Mathf.Min((bboxMaxX - bboxMinX), (bboxMaxY - bboxMinY)) / 2;
        for (int radius = 0; radius <= maxSearchRadius && !seedFound; radius++)
        {
            for (int dx = -radius; dx <= radius && !seedFound; dx++)
                for (int dy = -radius; dy <= radius && !seedFound; dy++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius) continue; // shell only
                    int sx = cx + dx, sy = cy + dy;
                    if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;
                    if (src[sy * w + sx].a <= 0.1f)
                    {
                        seed = new Vector2Int(sx, sy);
                        seedFound = true;
                    }
                }
        }

        if (!seedFound)
        {
            Debug.LogError("[DeriveMaskFromOutline] Could not find a transparent seed pixel " +
                           "inside the outline bounding box. The outline may be solid or too thick.");
            return null;
        }

        // ── BFS flood-fill from seed ─────────────────────────────────────────────
        // WHY: BFS (not recursive DFS) avoids stack overflow on large 4K textures.
        //      We allocate a flat bool array instead of a HashSet — O(1) lookup, cache friendly.
        bool[] visited = new bool[w * h];
        bool[] interior = new bool[w * h];
        System.Collections.Generic.Queue<Vector2Int> queue =
            new System.Collections.Generic.Queue<Vector2Int>();

        queue.Enqueue(seed);
        visited[seed.y * w + seed.x] = true;

        int[] dx4 = { 1, -1, 0, 0 };
        int[] dy4 = { 0, 0, 1, -1 };
        bool leaked = false;

        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            interior[p.y * w + p.x] = true;

            for (int d = 0; d < 4; d++)
            {
                int nx = p.x + dx4[d];
                int ny = p.y + dy4[d];

                // WHY: Hitting the canvas edge means the outline wasn't closed.
                //      We flag it but continue — partial fill is better than a crash.
                if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                {
                    leaked = true;
                    continue;
                }

                int ni = ny * w + nx;
                if (visited[ni]) continue;
                if (src[ni].a > 0.1f) continue; // outline pixel = wall

                visited[ni] = true;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        if (leaked)
            Debug.LogWarning("[DeriveMaskFromOutline] Flood-fill reached canvas edges — the T-shirt " +
                             "outline may not be fully closed. Interior mask may be inaccurate.");

        // ── Write output mask texture ────────────────────────────────────────────
        Texture2D mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] maskPixels = new Color[w * h];
        for (int i = 0; i < maskPixels.Length; i++)
            maskPixels[i] = interior[i] ? Color.white : Color.black;

        mask.SetPixels(maskPixels);
        mask.Apply();
        mask.name = "DerivedInteriorMask"; // WHY: Identifies runtime-created mask for OnDestroy cleanup
        return mask;
    }


    // ============================================================
    // UPDATE LOOP
    // ============================================================

    private void Update()
    {
        if (_totalMaskPixels == 0) return;

        HandleColorCycling();
        HandlePaintingInput();
        HandleResetInput();
        // WHY: Velocity updates before zone/cursor so _currentVelocityMultiplier
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
            // WHY: Image.color tints both circle and marker sprite — marker must be
            //      white/greyscale so the tint reads as the pure paint colour.
            _cursorImage.color = _currentColor;
        }
    }

    private void HandlePaintingInput()
    {
        if (Input.GetMouseButtonDown(0) && !_isComplete && !_hasFailed)
        {
            _isPainting = true;
            _distanceTraveledThisStroke = 0f;
            _lastEffectiveCursorPos = _effectiveCursorPos;
            _effectivePosInitialised = true;
            ResetImpulseState();

            // WHY: PlayLoop on MouseDown, not every frame. One call starts the loop;
            //      the AudioSource holds it until we explicitly stop it.
            //      Guard against missing clip so a missing asset doesn't throw.
            if (_audioManager != null && _drawingSound != null)
                _audioManager.PlayLoop(_drawingSound);
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isPainting = false;
            ResetImpulseState();

            // WHY: Stop() is instant. Pause() would resume mid-clip on next MouseDown
            //      which sounds like a stutter. Drawing always starts fresh.
            if (_audioManager != null)
                _audioManager.StopLoop();

            if (_canvasTexture != null)
                _canvasTexture.Apply();
        }
    }


    // WHY: CancelInvoke is critical. Without it, pressing R during the auto-reset
    //      countdown fires ResetCanvas() a second time, clearing a canvas the player
    //      may have already started repainting.
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
    ///      When not painting it decays toward 1.0 so hover speed cannot pre-load
    ///      difficulty before the player even clicks — escalation is earned stroke by stroke.
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
            // WHY: FIX C — decay toward baseline so hover speed is irrelevant.
            //      The moment LMB is released, difficulty starts unwinding.
            _currentVelocityMultiplier = Mathf.Lerp(_currentVelocityMultiplier, 1f,
                                                     Time.deltaTime * _velocitySmoothing);
        }
    }

    // ============================================================
    // EFFECTIVE CURSOR POSITION — SINGLE SOURCE OF TRUTH
    // ============================================================

    /// <summary>
    /// WHY: One method, one update per frame, one output position.
    ///      The visual cursor and the painter both read _effectiveCursorPos.
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
                // WHY: Impulse resets in the safe zone so the delay restarts next time
                //      the player enters the edge — rewards retreating to the middle.
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
                    // WHY: FIX B — full resistance + impulse stack only while LMB held.
                    //      Player sees a calm, accurate cursor while planning their stroke.
                    //      The moment they commit with LMB, the edge fights back.
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
                // WHY: _runtimeBrushRadius used here (was _brushRadius in v5 — the regression).
                //      Step count must use the same scale as the brush stamp itself.
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
    /// WHY: Oscillates PERPENDICULAR to dominant movement axis.
    ///      Driven by _distanceTraveledThisStroke (not Time.time) so wave
    ///      frequency is consistent regardless of mouse speed — purely spatial.
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
                            * _runtimeCurveAmplitude;

        return rawPos + perpendicular * oscillation;
    }

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
                // WHY: Return speed is constant — the player must learn "after the lurch,
                //      I have exactly this much time to correct." Variable return speed
                //      breaks that learned rhythm and makes it feel unfair.
                _currentImpulseDistance -= _returnSpeed * Time.deltaTime;
                if (_currentImpulseDistance <= 0f)
                {
                    _currentImpulseDistance = 0f;
                    // WHY: Cycle immediately — no second delay. After the first impulse,
                    //      difficulty is continuous. The player must stay slow.
                    _impulsePhase = ImpulsePhase.MovingOut;
                    _currentEffectiveMagnitude = _runtimeImpulseMagnitude * _currentVelocityMultiplier;
                    PickImpulseDirection();
                }
                break;
        }
    }

    /// <summary>
    /// WHY: ±30° variation per cycle prevents mechanical compensation.
    ///      Pure outward = player learns the exact angle in 2 reps.
    ///      ±30° keeps it unpredictable while staying fundamentally "away from sketch."
    /// </summary>
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

    /// <summary>
    /// WHY: Centralised reset so no caller forgets to zero the distance.
    ///      _currentImpulseDirection intentionally NOT reset — distance is 0 so
    ///      offset = direction × 0 = (0,0). Direction repicked on next MovingOut.
    /// </summary>
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

    /// <summary>
    /// WHY: Reads _effectiveCursorPos so visual and paint always agree.
    ///
    ///      FIX A — Jitter now fires on _isPainting == true (LMB held).
    ///      In v5 it fired on !_isPainting (hover). This was backward:
    ///      the hover cursor should be calm so the player can plan their stroke.
    ///      The moment they press LMB, the cursor starts fighting back.
    ///      This makes the edge zone feel like a resistance you push into,
    ///      not a random punishment for proximity.
    ///
    ///      Jitter remains VISUAL ONLY — never applied to where paint is stamped.
    /// </summary>
    private void UpdateCursorVisual()
    {
        Vector2 displayTexturePos = _effectiveCursorPos;

        // WHY: FIX A — jitter gates on _isPainting, not !_isPainting.
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

        // WHY: Circle cursor scales with runtimeBrushRadius — always shows true paint
        //      footprint. Marker cursor uses fixed artist-set size — resizing it every
        //      frame would distort the artwork and misrepresent the actual brush tip.
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

    /// <summary>
    /// WHY: Always computed from RAW mouse position, never _effectiveCursorPos.
    ///      If computed from effective pos, an impulse pushing the cursor outside
    ///      the edge zone would immediately kill the impulse — a negative feedback
    ///      loop that collapses the mechanic in one frame.
    ///      Raw mouse = player INTENT. Effective pos = player EXPERIENCE.
    ///
    ///      FIX (v6): Loop limit uses _runtimeEdgeZoneWidth (scaled int cast) instead
    ///      of raw _edgeZoneWidth. On any canvas != 512px these diverged, making
    ///      zone boundaries inconsistent with all other scaled values.
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
        int r = Mathf.CeilToInt(_runtimeBrushRadius);

        int xMin = Mathf.Max(0, cx - r);
        int xMax = Mathf.Min(_canvasWidth - 1, cx + r);
        int yMin = Mathf.Max(0, cy - r);
        int yMax = Mathf.Min(_canvasHeight - 1, cy + r);

        if (xMax < xMin || yMax < yMin) return;

        int regionW = xMax - xMin + 1;
        int regionH = yMax - yMin + 1;
        Color[] existingPixels = _canvasTexture.GetPixels(xMin, yMin, regionW, regionH);

        for (int py = yMin; py <= yMax; py++)
            for (int px = xMin; px <= xMax; px++)
            {
                float dx = px - cx;
                float dy = py - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > _runtimeBrushRadius) continue;

                int flatIdx = py * _canvasWidth + px;

                // WHY: Paper mask check is FIRST and SILENT — no penalty, no event.
                //      The player is not punished for moving near the paper edge; the brush
                //      simply doesn't apply. This models the physical constraint of the paper
                //      without adding a second failure axis the player can't control.
                if (_hasPaperMask && _paperMaskPixels[flatIdx].grayscale <= 0.5f)
                    continue;

                float normalizedDist = dist / _runtimeBrushRadius;
                float alpha = Mathf.Clamp01(
                    1f - Mathf.Pow(normalizedDist, 1f / _brushHardness)
                );

                int localIndex = (py - yMin) * regionW + (px - xMin);
                Color existing = existingPixels[localIndex];
                Color blended = Color.Lerp(existing, color, alpha);
                blended.a = Mathf.Max(existing.a, alpha);
                existingPixels[localIndex] = blended;
            }

        _canvasTexture.SetPixels(xMin, yMin, regionW, regionH, existingPixels);
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

                // WHY: If a paper mask is active, pixels outside it were never paintable
                //      and should not skew either meter. StampBrush already blocked them,
                //      but floating-point alpha from a previous session or edge anti-aliasing
                //      could leave residue — gate the sample to stay consistent.
                if (_hasPaperMask && _paperMaskPixels[idx].grayscale <= 0.5f)
                    continue;

                bool insideTShirt = _maskPixels[idx].grayscale > 0.5f;
                bool hasPaint = canvasPixels[idx].a > 0.05f;

                if (!hasPaint) continue;

                totalPainted++;
                if (insideTShirt) paintedInside++;
                else paintedOutside++;
            }

        if (totalPainted > 0)
        {
            // WHY: stride*stride subsampling underrepresents total mask pixels by factor stride².
            //      We divide _totalMaskPixels by the same factor so the denominator matches
            //      what we can actually observe in the strided loop.
            _paintedInsideFraction = Mathf.Clamp01((float)paintedInside /
                                          (_totalMaskPixels / (float)(stride * stride)));
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
        _audioManager.StopLoop();
        Exit();
        _gameManager.GoToState(GameState.Narrative);

    }

    private void OnFail()
    {
        _isPainting = false;

        if (_audioManager != null)
            _audioManager.StopLoop();

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
        //      fire on the very next frame after reset and immediately evaluate a
        //      freshly cleared canvas — causing a race with the player's first stroke.
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
            $"<b>(max {_runtimeResistanceStrength:F0}px at proximity 1.0)</b>\n" +
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

        // WHY: _maskTexture may have been created by DeriveMaskFromOutline() at runtime
        //      (not loaded from disk), in which case it is unmanaged memory we own.
        //      We tag it with a name in derivation so we can safely identify and destroy it.
        //      If it was assigned from the Inspector it is a managed asset — Destroy() on
        //      a managed asset is a no-op at runtime and harmless in-editor.
        if (_maskTexture != null && _maskTexture.name == "DerivedInteriorMask")
            Destroy(_maskTexture);

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
        if (_cursorGO != null) Destroy(_cursorGO);
        if (_debugTextGO != null) Destroy(_debugTextGO);
        if (_canvasTexture != null) Destroy(_canvasTexture);
        if (_cursorTexture != null) Destroy(_cursorTexture);

        // WHY: _maskTexture may have been created by DeriveMaskFromOutline() at runtime
        //      (not loaded from disk), in which case it is unmanaged memory we own.
        //      We tag it with a name in derivation so we can safely identify and destroy it.
        //      If it was assigned from the Inspector it is a managed asset — Destroy() on
        //      a managed asset is a no-op at runtime and harmless in-editor.
        if (_maskTexture != null && _maskTexture.name == "DerivedInteriorMask")
            Destroy(_maskTexture);

        Cursor.visible = true;
        CancelInvoke(nameof(ResetCanvas));
        // WHY: We do not own the Canvas — the scene hierarchy does.
        //      Destroying it here would nuke the entire Minigames hierarchy.
        //      MinigameState or scene lifecycle handles teardown.
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
