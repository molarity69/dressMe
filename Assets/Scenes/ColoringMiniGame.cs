// ============================================================
// ColoringMinigame.cs
//
// RESPONSIBILITY: Self-contained coloring minigame that simulates
// the sensory/motor struggle of staying within the lines.
// Manages the painting canvas, tri-modal cursor behavior,
// progress tracking, color selection, and a visual cursor
// that reflects the current color, displacement, and impulse jumps.
//
// CONSUMERS: Standalone testing (MonoBehaviour).
//            Later: wrap in IState for GameStateMachine integration.
// DEPENDS ON: Unity UI (Canvas, RawImage, Image, Text)
//             Two textures: sketch outline + mask
//
// KEY ARCHITECTURE FIX (v2):
//   A single _effectiveCursorPos (texture space) is the source of truth.
//   It is updated once per frame and used by BOTH the painter and the
//   cursor visual. This eliminates the drift/divergence bug where
//   paint and cursor were computed independently and disagreed.
// ============================================================

using UnityEngine;
using UnityEngine.UI;

public class ColoringMinigame : MonoBehaviour
{
    // ============================================================
    // CONFIGURATION
    // ============================================================

    [Header("Textures (Required)")]
    [Tooltip("The visible sketch outline. Inside area should be transparent, outline should be opaque black.")]
    [SerializeField] private Texture2D _sketchTexture;

    [Tooltip("Mask: White pixels = inside sketch (valid paint area). Black pixels = outside/outline.")]
    [SerializeField] private Texture2D _maskTexture;

    [Header("Colors (Cycle with RMB)")]
    [SerializeField]
    private Color[] _availableColors = new Color[]
    {
        new Color(1f, 0.15f, 0.15f, 1f),
        new Color(0.15f, 0.6f, 0.15f, 1f),
        new Color(0.15f, 0.3f, 1f, 1f),
    };

    [Header("Brush — Marker Style")]
    [Tooltip("Radius in texture pixels. 12 = medium marker on 512px canvas.")]
    [SerializeField] private float _brushRadius = 12f;
    [Tooltip("0 = fully soft/airbrush, 1 = hard marker edge. 0.75 feels like a real marker.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _brushHardness = 0.75f;

    [Header("Zone Detection")]
    [Tooltip("How many pixels from the outline count as 'near edge'. 30px is forgiving.")]
    [SerializeField] private float _edgeZoneWidth = 30f;
    [Tooltip("Number of radial directions to sample for edge detection. 8 = 45° increments.")]
    [SerializeField] private int _edgeSampleDirections = 8;

    [Header("Middle Zone — Curved Drawing (LMB Held)")]
    [Tooltip("Peak perpendicular displacement of the wave. Higher = wilder curves.")]
    [SerializeField] private float _curveAmplitude = 28f;
    [Tooltip("Wave cycles per pixel traveled. 0.015 ≈ 1 full wave per 67px.")]
    [SerializeField] private float _curveFrequency = 0.015f;

    [Header("Edge Zone — Cursor Jitter (LMB Released)")]
    [Tooltip("Peak pixel displacement of the visual-only shake.")]
    [SerializeField] private float _jitterIntensity = 10f;
    [Tooltip("How fast the Perlin noise cycles. Higher = faster shake.")]
    [SerializeField] private float _jitterSpeed = 18f;

    [Header("Edge Zone — Resistance + Impulse (LMB Held)")]
    [Tooltip("Steady outward pull in texture pixels/second.")]
    [SerializeField] private float _resistanceStrength = 35f;
    [Tooltip("Seconds between impulse jumps (randomised in this range).")]
    [SerializeField] private Vector2 _impulseInterval = new Vector2(1.5f, 4f);
    [Tooltip("How far (texture pixels) the impulse lurches the effective cursor outward.")]
    [SerializeField] private float _impulseMagnitude = 25f;
    [Tooltip("How quickly (pixels/second) the effective cursor snaps back to the real mouse after resistance/impulse.")]
    [SerializeField] private float _returnSpeed = 80f;

    [Header("Progress & Win/Fail")]
    [Range(0.1f, 1f)]
    [SerializeField] private float _completionThreshold = 0.85f;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _outOfBoundsFailThreshold = 0.15f;
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

    // Mask data — cached as flat array to avoid GetPixel() overhead
    private Color[] _maskPixels;
    private int _totalMaskPixels;

    // Input state
    private bool _isPainting;
    private int _currentColorIndex;
    private Color _currentColor;
    private float _distanceTraveledThisStroke;

    // WHY: This is the architectural fix. _effectiveCursorPos is the SINGLE SOURCE
    //      OF TRUTH for both painting and the cursor visual. It lives in texture space
    //      and is updated once per frame by UpdateEffectiveCursorPosition().
    //      The previous version computed offsets independently in the painter and the
    //      visual updater, causing them to diverge — dots instead of lines, ghost cursors.
    private Vector2 _effectiveCursorPos;
    private Vector2 _lastEffectiveCursorPos;
    private bool _effectivePosInitialised;

    // Zone state
    public enum CursorZone { Outside, Middle, NearEdge }
    private CursorZone _currentZone;
    private float _edgeProximity;
    private Vector2 _nearestOutsideDirection;

    // WHY: Accumulated outward displacement for edge resistance.
    //      This is NOT recomputed from scratch each frame — it's an ongoing velocity
    //      that builds up and then bleeds off as the player moves away from the edge.
    //      This is what creates "fluid struggle" rather than an instant snap.
    private Vector2 _resistanceAccumulator;

    // Impulse state
    private float _nextImpulseTime;

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
        _textureToScreenScale = new Vector2(
            panelSize.x / _canvasWidth,
            panelSize.y / _canvasHeight
        );

        _totalMaskPixels = 0;
        for (int i = 0; i < _maskPixels.Length; i++)
            if (_maskPixels[i].grayscale > 0.5f)
                _totalMaskPixels++;

        if (_totalMaskPixels == 0)
        {
            Debug.LogError("[ColoringMinigame] Mask has zero white pixels! Check your mask texture.");
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

        _nextImpulseTime = Time.time + Random.Range(_impulseInterval.x, _impulseInterval.y);

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

        // White background
        GameObject bgGO = new GameObject("WhiteBackground");
        bgGO.transform.SetParent(panel.transform, false);
        Image bgImage = bgGO.AddComponent<Image>();
        bgImage.color = Color.white;
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        // Painting layer
        GameObject paintingGO = new GameObject("PaintingLayer");
        paintingGO.transform.SetParent(panel.transform, false);
        _paintingDisplay = paintingGO.AddComponent<RawImage>();
        RectTransform paintRT = paintingGO.GetComponent<RectTransform>();
        paintRT.anchorMin = Vector2.zero; paintRT.anchorMax = Vector2.one;
        paintRT.offsetMin = Vector2.zero; paintRT.offsetMax = Vector2.zero;

        // Sketch overlay
        GameObject sketchGO = new GameObject("SketchOverlay");
        sketchGO.transform.SetParent(panel.transform, false);
        _sketchDisplay = sketchGO.AddComponent<Image>();
        _sketchDisplay.preserveAspect = true;
        RectTransform sketchRT = sketchGO.GetComponent<RectTransform>();
        sketchRT.anchorMin = Vector2.zero; sketchRT.anchorMax = Vector2.one;
        sketchRT.offsetMin = Vector2.zero; sketchRT.offsetMax = Vector2.zero;

        // Cursor visual
        GameObject cursorGO = new GameObject("CursorVisual");
        cursorGO.transform.SetParent(panel.transform, false);
        _cursorImage = cursorGO.AddComponent<Image>();
        _cursorImage.raycastTarget = false;
        _cursorRect = cursorGO.GetComponent<RectTransform>();
        _cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        _cursorRect.pivot = new Vector2(0.5f, 0.5f);
        float cursorScreenSize = _brushRadius * 2f;
        _cursorRect.sizeDelta = new Vector2(cursorScreenSize, cursorScreenSize);

        if (_showProgressOnScreen)
        {
            GameObject textGO = new GameObject("DebugText");
            textGO.transform.SetParent(canvasGO.transform, false);
            _debugText = textGO.AddComponent<Text>();
            _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _debugText.fontSize = 20;
            _debugText.color = Color.black;
            _debugText.alignment = TextAnchor.UpperLeft;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0, 1);
            textRT.anchorMax = new Vector2(0, 1);
            textRT.pivot = new Vector2(0, 1);
            textRT.anchoredPosition = new Vector2(20, -20);
            textRT.sizeDelta = new Vector2(400, 140);
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
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - halfSize + 0.5f;
                float dy = y - halfSize + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = 1f - Mathf.Clamp01((dist - radius) / feather);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
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
        if (_totalMaskPixels == 0)
            return;

        HandleColorCycling();
        HandlePaintingInput();       // Read LMB state
        UpdateCursorZone();          // Determine zone from raw mouse pos
        UpdateEffectiveCursorPos();  // THE FIX: one place updates the effective pos
        UpdateCursorVisual();        // Visual reads _effectiveCursorPos
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
            // WHY: Seed the last effective pos to the current raw texture pos on stroke
            //      start, so the first frame doesn't draw a line from (0,0).
            _lastEffectiveCursorPos = _effectiveCursorPos;
            _effectivePosInitialised = true;
            // WHY: Reset accumulator on each new stroke so resistance from a previous
            //      edge trace doesn't bleed into the start of the next stroke.
            _resistanceAccumulator = Vector2.zero;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isPainting = false;
            _resistanceAccumulator = Vector2.zero;
            _canvasTexture.Apply();
        }
    }

    // ============================================================
    // EFFECTIVE CURSOR POSITION — THE SINGLE SOURCE OF TRUTH
    // ============================================================

    /// <summary>
    /// WHY: This method is the architectural heart of v2. It replaces the old pattern
    ///      where ApplyCursorBehavior() was called independently inside the painter
    ///      AND the visual updater, producing two different results.
    ///
    ///      Now: ONE position is computed here. Everything downstream reads it.
    ///
    ///      Zone   | LMB up   | LMB held
    ///      -------+----------+------------------------------
    ///      Middle | raw pos  | raw pos + wave oscillation
    ///      Edge   | raw pos  | raw pos + resistance drift + impulse
    ///      Outside| raw pos  | raw pos (already escaped)
    ///
    ///      The jitter in Near Edge + LMB up is VISUAL ONLY and applied separately
    ///      in UpdateCursorVisual(). It never affects where paint is stamped.
    /// </summary>
    private void UpdateEffectiveCursorPos()
    {
        Vector2 rawTexturePos = ScreenToTexturePosition(Input.mousePosition);

        // If cursor is outside the canvas, snap effective pos to raw and bail
        if (rawTexturePos.x < 0 || rawTexturePos.x >= _canvasWidth ||
            rawTexturePos.y < 0 || rawTexturePos.y >= _canvasHeight)
        {
            _effectiveCursorPos = rawTexturePos;
            return;
        }

        if (!_effectivePosInitialised)
        {
            // WHY: First frame initialisation — don't interpolate from (0,0).
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
                if (_isPainting && rawDeltaMag > 0.1f)
                {
                    _distanceTraveledThisStroke += rawDeltaMag;
                    _effectiveCursorPos = ComputeCurvedPosition(rawTexturePos);
                }
                else
                {
                    // WHY: Not painting or not moving — follow mouse exactly.
                    //      No oscillation when the mouse is still (avoids a stationary dot wobbling).
                    _effectiveCursorPos = rawTexturePos;
                }
                break;

            case CursorZone.NearEdge:
                if (_isPainting)
                {
                    _effectiveCursorPos = ComputeResistancePosition(rawTexturePos, rawDeltaMag);
                }
                else
                {
                    // WHY: Jitter is visual-only. Effective pos follows the mouse exactly
                    //      so if the player isn't painting, no stray marks appear.
                    _effectiveCursorPos = rawTexturePos;
                    // Bleed off any accumulated resistance when LMB is released
                    _resistanceAccumulator = Vector2.zero;
                }
                break;

            case CursorZone.Outside:
            default:
                _effectiveCursorPos = rawTexturePos;
                _resistanceAccumulator = Vector2.zero;
                break;
        }

        // ── Paint along the path ────────────────────────────────────────────
        // WHY: We paint here, not in HandlePaintingInput, because we need the
        //      effective pos to be fully resolved first. Painting is the consequence
        //      of the position update, not the trigger for it.
        if (_isPainting)
        {
            float dist = Vector2.Distance(_lastEffectiveCursorPos, _effectiveCursorPos);
            if (dist > 0.01f)
            {
                // WHY: Interpolate stamps along the path so fast movement never
                //      leaves gaps. Step size = half brush radius.
                int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (_brushRadius * 0.5f)));
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Vector2 stampPos = Vector2.Lerp(_lastEffectiveCursorPos, _effectiveCursorPos, t);
                    StampBrush(stampPos, _currentColor);
                }
            }
            else
            {
                // WHY: Stationary mouse while holding LMB — stamp a single dot
                //      so the brush leaves a mark immediately on click.
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
    /// WHY: Computes the wave-displaced position for middle-zone curved drawing.
    ///
    ///      The wave oscillates PERPENDICULAR to the dominant axis of mouse movement.
    ///      If you drag the mouse mostly left-right → the brush waves up and down.
    ///      If you drag mostly up-down → the brush waves left and right.
    ///
    ///      The oscillation is driven by _distanceTraveledThisStroke so the wave
    ///      frequency is consistent regardless of mouse speed. Fast mouse = same
    ///      wave frequency, just covered faster. Speed only affects amplitude.
    ///
    ///      Result: the player sees the colored circle visibly rocking perpendicular
    ///      to their movement, dragging paint in sweeping curves.
    /// </summary>
    private Vector2 ComputeCurvedPosition(Vector2 rawPos)
    {
        Vector2 mouseDelta = rawPos - _lastEffectiveCursorPos;
        if (mouseDelta.magnitude < 0.1f)
            return rawPos;

        mouseDelta.Normalize();

        // WHY: Dominant-axis perpendicular keeps the oscillation axis stable.
        //      Without this, diagonal movement flips the perp direction each frame,
        //      turning the smooth wave into random noise.
        Vector2 perpendicular;
        if (Mathf.Abs(mouseDelta.y) > Mathf.Abs(mouseDelta.x))
            perpendicular = new Vector2(1f, 0f);  // Vertical motion → horizontal wave
        else
            perpendicular = new Vector2(0f, 1f);  // Horizontal motion → vertical wave

        float oscillation = Mathf.Sin(_distanceTraveledThisStroke * _curveFrequency * Mathf.PI * 2f)
                            * _curveAmplitude;

        return rawPos + perpendicular * oscillation;
    }

    /// <summary>
    /// WHY: Computes a continuously drifting, impulse-prone position for near-edge tracing.
    ///
    ///      The resistance accumulator is a persistent outward velocity that builds up
    ///      as the player stays near the edge. It bleeds back toward zero over time
    ///      (at _returnSpeed) to simulate the player "fighting back."
    ///
    ///      This creates the fluid tug-of-war feel:
    ///        - The cursor drifts outward with steady pressure
    ///        - Random impulse jumps lurch it further
    ///        - When the player stops pressing, the accumulator bleeds off
    ///
    ///      The effective position is: rawPos + _resistanceAccumulator
    ///      Both the cursor circle AND the paint path use this, so they always agree.
    /// </summary>
    private Vector2 ComputeResistancePosition(Vector2 rawPos, float rawDeltaMag)
    {
        // Steady outward resistance — scales with proximity to edge
        // WHY: deltaTime makes it frame-rate independent. The closer to the edge,
        //      the harder it pulls. 0 at the center of the edge zone, max at the outline.
        Vector2 steadyForce = _nearestOutsideDirection
                              * _resistanceStrength
                              * _edgeProximity
                              * Time.deltaTime;

        _resistanceAccumulator += steadyForce;

        // Impulse — random sudden lurch
        if (Time.time >= _nextImpulseTime && _edgeProximity > 0.3f)
        {
            _nextImpulseTime = Time.time + Random.Range(_impulseInterval.x, _impulseInterval.y);

            // WHY: Add ±30° of variation so the impulse doesn't feel robotic.
            //      A pure outward jump would be too predictable after one attempt.
            Vector2 impulseDir = Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f))
                                 * _nearestOutsideDirection;
            _resistanceAccumulator += impulseDir * _impulseMagnitude;
        }

        // WHY: Bleed the accumulator back toward zero each frame at _returnSpeed.
        //      This is the "fighting back" force — the player's hand reasserts control.
        //      Without this, the accumulator would grow unbounded and shoot the cursor
        //      off the canvas after a few seconds.
        float bleedMagnitude = _returnSpeed * Time.deltaTime;
        if (_resistanceAccumulator.magnitude > bleedMagnitude)
            _resistanceAccumulator -= _resistanceAccumulator.normalized * bleedMagnitude;
        else
            _resistanceAccumulator = Vector2.zero;

        return rawPos + _resistanceAccumulator;
    }

    // ============================================================
    // VISUAL CURSOR
    // ============================================================

    /// <summary>
    /// WHY: The visual cursor simply reads _effectiveCursorPos — the same value
    ///      used for painting. This guarantees they always agree. The only exception
    ///      is Near Edge + no LMB, where we ADD visual jitter on top of the raw pos.
    ///      That jitter is purely cosmetic and never touches paint.
    /// </summary>
    private void UpdateCursorVisual()
    {
        Vector2 displayTexturePos = _effectiveCursorPos;

        // Visual-only jitter when hovering near the edge without painting
        if (_currentZone == CursorZone.NearEdge && !_isPainting)
        {
            float noiseX = Mathf.PerlinNoise(Time.time * _jitterSpeed, 0f) - 0.5f;
            float noiseY = Mathf.PerlinNoise(0f, Time.time * _jitterSpeed) - 0.5f;
            Vector2 jitter = new Vector2(noiseX, noiseY) * _jitterIntensity * 2f;
            displayTexturePos += jitter;
        }

        // Hide when outside canvas
        if (displayTexturePos.x < 0 || displayTexturePos.x >= _canvasWidth ||
            displayTexturePos.y < 0 || displayTexturePos.y >= _canvasHeight)
        {
            _cursorImage.enabled = false;
            return;
        }
        _cursorImage.enabled = true;

        Vector2 panelLocal = TextureToPanelLocal(displayTexturePos);
        _cursorRect.anchoredPosition = panelLocal;

        float cursorSize = _brushRadius * 2f * Mathf.Max(_textureToScreenScale.x, _textureToScreenScale.y);
        _cursorRect.sizeDelta = new Vector2(cursorSize, cursorSize);
    }

    // ============================================================
    // ZONE DETECTION
    // ============================================================

    /// <summary>
    /// WHY: Radial mask sampling from the raw mouse position (not effective pos).
    ///      We always detect zones from where the player INTENDS to be, not where
    ///      the cursor has been pushed to. Otherwise, an impulse that pushes the
    ///      cursor outside would immediately flip the zone to Outside and kill
    ///      the resistance — a feedback loop that breaks the mechanic.
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

        Color[] existingPixels = _canvasTexture.GetPixels(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);

        for (int py = yMin; py <= yMax; py++)
        {
            for (int px = xMin; px <= xMax; px++)
            {
                float dx = px - cx;
                float dy = py - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist <= _brushRadius)
                {
                    float normalizedDist = dist / _brushRadius;
                    float alpha = 1f - Mathf.Pow(normalizedDist, 1f / _brushHardness);
                    alpha = Mathf.Clamp01(alpha);

                    int localIndex = (py - yMin) * (xMax - xMin + 1) + (px - xMin);
                    Color existing = existingPixels[localIndex];
                    Color blended = Color.Lerp(existing, color, alpha);
                    blended.a = Mathf.Max(existing.a, alpha);
                    existingPixels[localIndex] = blended;
                }
            }
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
        {
            for (int x = 0; x < _canvasWidth; x += stride)
            {
                int idx = y * _canvasWidth + x;
                bool isInsideMask = _maskPixels[idx].grayscale > 0.5f;
                bool hasPaint = canvasPixels[idx].a > 0.05f;

                if (hasPaint)
                {
                    totalPainted++;
                    if (isInsideMask) paintedInside++;
                    else paintedOutside++;
                }
            }
        }

        if (totalPainted > 0)
        {
            _paintedInsideFraction = Mathf.Clamp01(
                (float)paintedInside / (_totalMaskPixels / (stride * stride)));
            _paintedOutsideFraction = Mathf.Clamp01(
                (float)paintedOutside / totalPainted);
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
        // WHY: Stub for state machine integration.
        //      In production: _gameManager.ResolveMinigame(MinigameResult.Success)
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
        _resistanceAccumulator = Vector2.zero;
        _effectivePosInitialised = false;
        _nextImpulseTime = Time.time + Random.Range(_impulseInterval.x, _impulseInterval.y);

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
            CursorZone.NearEdge => _isPainting ? "NEAR EDGE (resistance)" : "NEAR EDGE (jitter)",
            CursorZone.Outside => "OUTSIDE",
            _ => "???"
        };

        _debugText.text =
            $"<b>Color:</b> {_currentColor} (RMB cycle)\n" +
            $"<b>Zone:</b> {zoneLabel}\n" +
            $"<b>Proximity:</b> {_edgeProximity:F2}  " +
            $"<b>Resistance:</b> {_resistanceAccumulator.magnitude:F1}px\n" +
            $"<b>Painted Inside:</b> {_paintedInsideFraction:P1} / {_completionThreshold:P0}\n" +
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
        if (_returnSpeed < 10f) _returnSpeed = 10f;
    }
#endif
}
