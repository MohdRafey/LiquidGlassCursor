using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiquidGlassCursor;

public enum CursorShape
{
  OriginalBlob,
  ClassicPointer,
  SleekDart,
  LiquidDroplet,
  MinimalOrb
}

public enum VisibilityMode
{
  AdaptiveLuminanceRim,
  FloatingContactShadow,
  DoubleBevelOpticalEdge,
  BeerLambertAbsorption,
  AdaptivePlusShadow
}

public partial class LiquidLensOverlay : Window
{
  private const int GWL_EXSTYLE = -20;
  private const int WS_EX_TRANSPARENT = 0x00000020;
  private const int WS_EX_TOOLWINDOW = 0x00000080;
  private const int WS_EX_NOACTIVATE = 0x08000000;
  private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

  private static readonly IntPtr HWND_TOPMOST = new(-1);
  private const int WM_WINDOWPOSCHANGING = 0x0046;
  private const uint SWP_NOSIZE = 0x0001;
  private const uint SWP_NOZORDER = 0x0004;
  private const uint SWP_NOACTIVATE = 0x0010;
  private const uint SWP_NOOWNERZORDER = 0x0200;

  private IntPtr _hwnd;

  [StructLayout(LayoutKind.Sequential)]
  private struct POINT { public int X; public int Y; }

  [StructLayout(LayoutKind.Sequential)]
  private struct WINDOWPOS
  {
    public IntPtr hwnd;
    public IntPtr hwndInsertAfter;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public uint flags;
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

  [DllImport("user32.dll")]
  private static extern bool GetCursorPos(out POINT lpPoint);

  [DllImport("user32.dll")]
  private static extern int GetWindowLong(IntPtr hwnd, int index);

  [DllImport("user32.dll")]
  private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

  [DllImport("user32.dll")]
  private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

  private int _baseSize = 44;
  private int _renderSize = 44;
  private CursorShape _shape = CursorShape.OriginalBlob;
  private VisibilityMode _visibilityMode = VisibilityMode.AdaptivePlusShadow;
  private float _zoom = 1.35f;
  private float _refraction = 0.45f;
  private float _chromaticAberration = 3.2f;
  private float _blurRadius = 1.2f;
  private System.Drawing.Color _tint = System.Drawing.Color.FromArgb(220, 235, 255);

  // Shake-to-Enlarge parameters
  private bool _shakeEnabled = true;
  private float _shakeSensitivity = 1.0f; // 0.5 (hard shake) to 2.0 (gentle shake)
  private float _shakeHoldSeconds = 1.2f;
  private float _shakeScaleMultiplier = 2.0f;

  // Shake tracking state
  private int _lastCursorX;
  private int _lastCursorY;
  private bool _hasLastCursor;
  private int _lastDirX;
  private int _lastDirY;
  private readonly Queue<long> _reversalTimestamps = new();
  private long _enlargedUntilTick;
  private float _currentScale = 1.0f;

  private bool _isConfiguring;
  private WriteableBitmap? _writeableBitmap;
  private Bitmap? _captureBmp;
  private Graphics? _captureGraphics;
  private float[,]? _sdfCache;
  private float[,]? _normalXCache;
  private float[,]? _normalYCache;
  private float _hotspotNormX = 0.12f;
  private float _hotspotNormY = 0.12f;
  private float _centerNormX = 0.44f;
  private float _centerNormY = 0.44f;
  private double _dpiScaleX = 1.0;
  private double _dpiScaleY = 1.0;

  public LiquidLensOverlay()
  {
    InitializeComponent();
    SourceInitialized += OnSourceInitialized;
  }

  private void OnSourceInitialized(object? sender, EventArgs e)
  {
    _hwnd = new WindowInteropHelper(this).Handle;
    int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
    SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    SetWindowDisplayAffinity(_hwnd, WDA_EXCLUDEFROMCAPTURE);

    HwndSource.FromHwnd(_hwnd)?.AddHook(WndProcHook);

    var source = PresentationSource.FromVisual(this);
    if (source?.CompositionTarget != null)
    {
      _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
      _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
    }

    Configure(44, CursorShape.OriginalBlob, VisibilityMode.AdaptivePlusShadow, 1.35f, 0.45f, 3.2f, 1.2f, _tint, true, 1.0f, 1.2f, 2.0f);
    CompositionTarget.Rendering += OnRendering;
  }

  private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
    {
      var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
      pos.hwndInsertAfter = HWND_TOPMOST;
      pos.flags &= ~SWP_NOZORDER;
      Marshal.StructureToPtr(pos, lParam, fDeleteOld: false);
    }
    return IntPtr.Zero;
  }

  public void Configure(
      int size,
      CursorShape shape,
      VisibilityMode visibilityMode,
      float zoom,
      float refraction,
      float chromaticAberration,
      float blurRadius,
      System.Drawing.Color tint,
      bool shakeEnabled,
      float shakeSensitivity,
      float shakeHoldSeconds,
      float shakeScaleMultiplier)
  {
    _visibilityMode = visibilityMode;
    _zoom = zoom;
    _refraction = refraction;
    _chromaticAberration = chromaticAberration;
    _blurRadius = blurRadius;
    _tint = tint;
    _shakeEnabled = shakeEnabled;
    _shakeSensitivity = shakeSensitivity;
    _shakeHoldSeconds = shakeHoldSeconds;
    _shakeScaleMultiplier = shakeScaleMultiplier;

    if (!_shakeEnabled)
    {
      _enlargedUntilTick = 0;
      _currentScale = 1.0f;
    }

    // Allocate canvas large enough to stay razor-sharp at full shake enlargement
    int targetRenderSize = _shakeEnabled
        ? (int)Math.Ceiling(size * Math.Max(1.0f, shakeScaleMultiplier))
        : size;

    if (_baseSize == size && _renderSize == targetRenderSize && _shape == shape && _writeableBitmap != null)
      return;

    _isConfiguring = true;
    try
    {
      var oldGraphics = _captureGraphics;
      var oldBmp = _captureBmp;
      _captureGraphics = null;
      _captureBmp = null;
      oldGraphics?.Dispose();
      oldBmp?.Dispose();

      var newBmp = new Bitmap(targetRenderSize, targetRenderSize, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
      var newGraphics = Graphics.FromImage(newBmp);
      var newWb = new WriteableBitmap(targetRenderSize, targetRenderSize, 96, 96, PixelFormats.Pbgra32, null);

      PrecomputeShapeField(targetRenderSize, shape);

      _baseSize = size;
      _renderSize = targetRenderSize;
      _shape = shape;
      _captureBmp = newBmp;
      _captureGraphics = newGraphics;
      _writeableBitmap = newWb;
      LensImage.Source = _writeableBitmap;

      Width = targetRenderSize / _dpiScaleX;
      Height = targetRenderSize / _dpiScaleY;
    }
    finally
    {
      _isConfiguring = false;
    }
  }

  private void UpdateShakeScale(int curX, int curY)
  {
    long now = Stopwatch.GetTimestamp();
    long freq = Stopwatch.Frequency;

    if (!_shakeEnabled)
    {
      _currentScale = 1.0f;
      return;
    }

    if (!_hasLastCursor)
    {
      _lastCursorX = curX;
      _lastCursorY = curY;
      _hasLastCursor = true;
      return;
    }

    int dx = curX - _lastCursorX;
    int dy = curY - _lastCursorY;
    _lastCursorX = curX;
    _lastCursorY = curY;

    // Higher sensitivity lowers the minimum pixel delta required to count as a shake stroke
    float minDelta = Math.Max(4f, 18f / Math.Max(0.3f, _shakeSensitivity));

    int dirX = Math.Abs(dx) >= minDelta ? Math.Sign(dx) : 0;
    int dirY = Math.Abs(dy) >= minDelta ? Math.Sign(dy) : 0;

    bool reversed = false;
    if (dirX != 0)
    {
      if (_lastDirX != 0 && dirX != _lastDirX)
        reversed = true;
      _lastDirX = dirX;
    }
    if (dirY != 0)
    {
      if (_lastDirY != 0 && dirY != _lastDirY)
        reversed = true;
      _lastDirY = dirY;
    }

    if (reversed)
      _reversalTimestamps.Enqueue(now);

    // Keep only reversals inside the last 450 ms
    long windowTicks = (long)(freq * 0.45);
    while (_reversalTimestamps.Count > 0 && now - _reversalTimestamps.Peek() > windowTicks)
      _reversalTimestamps.Dequeue();

    // Trigger enlargement on 4 rapid direction reversals
    if (_reversalTimestamps.Count >= 4)
    {
      _enlargedUntilTick = now + (long)(freq * _shakeHoldSeconds);
    }

    float targetScale = now < _enlargedUntilTick ? _shakeScaleMultiplier : 1.0f;
    // Smooth exponential interpolation (snappy grow, gentle shrink)
    float lerpSpeed = targetScale > _currentScale ? 0.28f : 0.16f;
    _currentScale += (targetScale - _currentScale) * lerpSpeed;

    if (Math.Abs(_currentScale - 1.0f) < 0.005f)
      _currentScale = 1.0f;
  }

  private void PrecomputeShapeField(int s, CursorShape shape)
  {
    var sdf = new float[s, s];
    var nxCache = new float[s, s];
    var nyCache = new float[s, s];

    if (shape == CursorShape.OriginalBlob)
    {
      _hotspotNormX = 0.12f;
      _hotspotNormY = 0.12f;
      _centerNormX = 0.44f;
      _centerNormY = 0.44f;

      (float x, float y, float r)[] metaballs =
      [
          (0.13f, 0.13f, 0.045f),
                (0.21f, 0.21f, 0.085f),
                (0.31f, 0.31f, 0.135f),
                (0.42f, 0.43f, 0.185f),
                (0.48f, 0.50f, 0.210f),
                (0.36f, 0.54f, 0.155f),
                (0.55f, 0.38f, 0.155f)
      ];

      for (int y = 0; y < s; y++)
      {
        float ny = (y + 0.5f) / s;
        for (int x = 0; x < s; x++)
        {
          float nx = (x + 0.5f) / s;
          float sum = 0f;
          foreach (var (mx, my, mr) in metaballs)
          {
            float dx = nx - mx;
            float dy = ny - my;
            float d2 = dx * dx + dy * dy + 0.0001f;
            sum += (mr * mr) / d2;
          }
          sdf[y, x] = (1.0f - sum) * 0.14f;
        }
      }
    }
    else
    {
      for (int y = 0; y < s; y++)
      {
        float ny = (y + 0.5f) / s;
        for (int x = 0; x < s; x++)
        {
          float nx = (x + 0.5f) / s;
          sdf[y, x] = EvaluateShapeSDF(nx, ny, shape);
        }
      }
    }

    for (int y = 0; y < s; y++)
    {
      for (int x = 0; x < s; x++)
      {
        int xm = Math.Max(0, x - 1);
        int xp = Math.Min(s - 1, x + 1);
        int ym = Math.Max(0, y - 1);
        int yp = Math.Min(s - 1, y + 1);

        float gx = sdf[y, xm] - sdf[y, xp];
        float gy = sdf[ym, x] - sdf[yp, x];
        float len = MathF.Sqrt(gx * gx + gy * gy) + 1e-5f;
        nxCache[y, x] = gx / len;
        nyCache[y, x] = gy / len;
      }
    }

    _sdfCache = sdf;
    _normalXCache = nxCache;
    _normalYCache = nyCache;
  }

  private float EvaluateShapeSDF(float x, float y, CursorShape shape)
  {
    switch (shape)
    {
      case CursorShape.ClassicPointer:
        {
          _hotspotNormX = 0.16f;
          _hotspotNormY = 0.10f;
          _centerNormX = 0.38f;
          _centerNormY = 0.44f;

          float dBody = SdTriangle(x, y, 0.16f, 0.10f, 0.16f, 0.72f, 0.64f, 0.54f) - 0.035f;
          float dTail = SdCapsule(x, y, 0.34f, 0.52f, 0.48f, 0.84f, 0.055f);
          return SmoothMin(dBody, dTail, 0.04f);
        }

      case CursorShape.SleekDart:
        {
          _hotspotNormX = 0.14f;
          _hotspotNormY = 0.14f;
          _centerNormX = 0.42f;
          _centerNormY = 0.42f;

          float dMain = SdTriangle(x, y, 0.14f, 0.14f, 0.34f, 0.82f, 0.82f, 0.34f) - 0.03f;
          float dNotch = SdCircle(x, y, 0.66f, 0.66f, 0.23f);
          return MathF.Max(dMain, -dNotch);
        }

      case CursorShape.MinimalOrb:
        {
          _hotspotNormX = 0.50f;
          _hotspotNormY = 0.50f;
          _centerNormX = 0.50f;
          _centerNormY = 0.50f;

          return SdCircle(x, y, 0.50f, 0.50f, 0.36f);
        }

      default: // LiquidDroplet
        {
          _hotspotNormX = 0.14f;
          _hotspotNormY = 0.14f;
          _centerNormX = 0.46f;
          _centerNormY = 0.46f;

          float dTip = SdCapsule(x, y, 0.15f, 0.15f, 0.45f, 0.45f, 0.04f);
          float dBulb = SdCircle(x, y, 0.48f, 0.48f, 0.27f);
          return SmoothMin(dTip, dBulb, 0.14f);
        }
    }
  }

  private static float SdCircle(float px, float py, float cx, float cy, float r)
  {
    float dx = px - cx;
    float dy = py - cy;
    return MathF.Sqrt(dx * dx + dy * dy) - r;
  }

  private static float SdCapsule(float px, float py, float ax, float ay, float bx, float by, float r)
  {
    float pax = px - ax, pay = py - ay;
    float bax = bx - ax, bay = by - ay;
    float h = Math.Clamp((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-6f), 0f, 1f);
    float dx = pax - bax * h;
    float dy = pay - bay * h;
    return MathF.Sqrt(dx * dx + dy * dy) - r;
  }

  private static float SdTriangle(float px, float py, float x0, float y0, float x1, float y1, float x2, float y2)
  {
    float e0x = x1 - x0, e0y = y1 - y0;
    float e1x = x2 - x1, e1y = y2 - y1;
    float e2x = x0 - x2, e2y = y0 - y2;

    float v0x = px - x0, v0y = py - y0;
    float v1x = px - x1, v1y = py - y1;
    float v2x = px - x2, v2y = py - y2;

    float pq0x = v0x - e0x * Math.Clamp((v0x * e0x + v0y * e0y) / (e0x * e0x + e0y * e0y), 0f, 1f);
    float pq0y = v0y - e0y * Math.Clamp((v0x * e0x + v0y * e0y) / (e0x * e0x + e0y * e0y), 0f, 1f);

    float pq1x = v1x - e1x * Math.Clamp((v1x * e1x + v1y * e1y) / (e1x * e1x + e1y * e1y), 0f, 1f);
    float pq1y = v1y - e1y * Math.Clamp((v1x * e1x + v1y * e1y) / (e1x * e1x + e1y * e1y), 0f, 1f);

    float pq2x = v2x - e2x * Math.Clamp((v2x * e2x + v2y * e2y) / (e2x * e2x + e2y * e2y), 0f, 1f);
    float pq2y = v2y - e2y * Math.Clamp((v2x * e2x + v2y * e2y) / (e2x * e2x + e2y * e2y), 0f, 1f);

    float s = MathF.Sign(e0x * e2y - e0y * e2x);
    float d0 = pq0x * pq0x + pq0y * pq0y;
    float d1 = pq1x * pq1x + pq1y * pq1y;
    float d2 = pq2x * pq2x + pq2y * pq2y;

    float s0 = s * (v0x * e0y - v0y * e0x);
    float s1 = s * (v1x * e1y - v1y * e1x);
    float s2 = s * (v2x * e2y - v2y * e2x);

    float d = MathF.Min(MathF.Min(d0, d1), d2);
    float cross = MathF.Min(MathF.Min(s0, s1), s2);
    return -MathF.Sqrt(d) * MathF.Sign(cross);
  }

  private static float SmoothMin(float a, float b, float k)
  {
    float h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0f, 1f);
    return b * (1f - h) + a * h - k * h * (1f - h);
  }

  private unsafe void OnRendering(object? sender, EventArgs e)
  {
    if (_hwnd == IntPtr.Zero || _isConfiguring || _captureBmp == null || _captureGraphics == null ||
        _writeableBitmap == null || _sdfCache == null || _normalXCache == null || _normalYCache == null)
      return;

    if (!GetCursorPos(out POINT pt)) return;

    UpdateShakeScale(pt.X, pt.Y);

    // Active physical size interpolates smoothly between _baseSize and (_baseSize * _shakeScaleMultiplier)
    int activeSize = Math.Clamp((int)Math.Round(_baseSize * _currentScale), _baseSize, _renderSize);
    int hotX = (int)(activeSize * _hotspotNormX);
    int hotY = (int)(activeSize * _hotspotNormY);
    int capX = pt.X - hotX;
    int capY = pt.Y - hotY;

    SetWindowPos(_hwnd, HWND_TOPMOST, capX, capY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    // Capture the exact activeSize patch from screen and map onto _renderSize SDF
    _captureGraphics.CopyFromScreen(capX, capY, 0, 0, new System.Drawing.Size(activeSize, activeSize), CopyPixelOperation.SourceCopy);

    var rect = new Rectangle(0, 0, activeSize, activeSize);
    BitmapData srcData = _captureBmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
    _writeableBitmap.Lock();

    try
    {
      byte* srcBase = (byte*)srcData.Scan0;
      int srcStride = srcData.Stride;
      byte* dstBase = (byte*)_writeableBitmap.BackBuffer;
      int dstStride = _writeableBitmap.BackBufferStride;

      // Clear any leftover outer band when shrinking back from enlarged state
      if (activeSize < _renderSize)
      {
        for (int y = 0; y < _renderSize; y++)
        {
          uint* row = (uint*)(dstBase + y * dstStride);
          for (int x = (y < activeSize ? activeSize : 0); x < _renderSize; x++)
            row[x] = 0x00000000;
        }
      }

      float cx = activeSize * _centerNormX;
      float cy = activeSize * _centerNormY;
      float invZoom = 1.0f / Math.Max(1.0f, _zoom);
      float aaWidth = 1.5f / activeSize;
      float sdfScale = (float)_renderSize / activeSize;

      bool useShadow = _visibilityMode is VisibilityMode.FloatingContactShadow or VisibilityMode.AdaptivePlusShadow;
      int shadowOffsetX = Math.Max(2, (int)(activeSize * 0.055f));
      int shadowOffsetY = Math.Max(3, (int)(activeSize * 0.075f));
      float shadowSpread = 0.085f;

      for (int y = 0; y < activeSize; y++)
      {
        uint* dstRow = (uint*)(dstBase + y * dstStride);
        int sdfY = Math.Min(_renderSize - 1, (int)(y * sdfScale));

        for (int x = 0; x < activeSize; x++)
        {
          int sdfX = Math.Min(_renderSize - 1, (int)(x * sdfScale));
          float dist = _sdfCache[sdfY, sdfX];

          if (dist > aaWidth)
          {
            if (useShadow)
            {
              int sx = x - shadowOffsetX;
              int sy = y - shadowOffsetY;
              if (sx >= 0 && sy >= 0 && sx < activeSize && sy < activeSize)
              {
                int sSdfX = Math.Min(_renderSize - 1, (int)(sx * sdfScale));
                int sSdfY = Math.Min(_renderSize - 1, (int)(sy * sdfScale));
                float sDist = _sdfCache[sSdfY, sSdfX];
                if (sDist < shadowSpread)
                {
                  float sFactor = Math.Clamp(1.0f - (Math.Max(0f, sDist) / shadowSpread), 0f, 1f);
                  float shadowAlpha = sFactor * sFactor * 0.24f;
                  byte sa = (byte)(shadowAlpha * 255f);
                  byte sr = (byte)(15f * shadowAlpha);
                  byte sg = (byte)(23f * shadowAlpha);
                  byte sb = (byte)(42f * shadowAlpha);
                  dstRow[x] = (uint)((sa << 24) | (sr << 16) | (sg << 8) | sb);
                  continue;
                }
              }
            }

            dstRow[x] = 0x00000000;
            continue;
          }

          float bevelWidth = 0.14f;
          float rimFactor = Math.Clamp(1.0f - (-dist / bevelWidth), 0f, 1f);
          float rimCurve = rimFactor * rimFactor;

          float nx = _normalXCache[sdfY, sdfX];
          float ny = _normalYCache[sdfY, sdfX];

          float baseU = cx + (x - cx) * invZoom;
          float baseV = cy + (y - cy) * invZoom;

          float tirReach = _refraction * activeSize * 0.72f * (rimCurve * rimCurve);
          float refractedU = baseU + nx * tirReach;
          float refractedV = baseV + ny * tirReach;

          float caShift = _chromaticAberration * rimCurve;
          float r = SampleChannelBlurred(srcBase, srcStride, activeSize, refractedU + nx * caShift, refractedV + ny * caShift, 2, _blurRadius);
          float g = SampleChannelBlurred(srcBase, srcStride, activeSize, refractedU, refractedV, 1, _blurRadius);
          float b = SampleChannelBlurred(srcBase, srcStride, activeSize, refractedU - nx * caShift, refractedV - ny * caShift, 0, _blurRadius);

          float tintMix = 0.10f;
          r = r * (1f - tintMix) + _tint.R * tintMix;
          g = g * (1f - tintMix) + _tint.G * tintMix;
          b = b * (1f - tintMix) + _tint.B * tintMix;

          float spec = Math.Clamp(((-nx - ny) * 0.55f) * rimFactor * 1.3f, 0f, 1f);
          float fresnelRim = MathF.Pow(rimFactor, 3.2f) * 140f;
          float boost = spec * 95f + fresnelRim;

          switch (_visibilityMode)
          {
            case VisibilityMode.AdaptiveLuminanceRim:
            case VisibilityMode.AdaptivePlusShadow:
              {
                float luma = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
                float brightMask = Math.Clamp((luma - 0.62f) / 0.38f, 0f, 1f);
                float outerEdge = MathF.Pow(rimFactor, 3.5f);

                float darkEdge = brightMask * outerEdge * 88f;
                float innerTint = brightMask * rimCurve * 16f;
                r = r - darkEdge - innerTint * 1.1f;
                g = g - darkEdge * 0.92f - innerTint * 0.9f;
                b = b - darkEdge * 0.72f - innerTint * 0.5f;

                boost = spec * (95f - brightMask * 55f) + fresnelRim * (1f - brightMask * 0.85f);
                break;
              }

            case VisibilityMode.DoubleBevelOpticalEdge:
              {
                float outerDark = Math.Clamp((rimFactor - 0.80f) / 0.20f, 0f, 1f) * 105f;
                float innerGlow = MathF.Sin(Math.Clamp((rimFactor - 0.45f) / 0.40f, 0f, 1f) * MathF.PI) * 90f;
                r = r - outerDark + innerGlow;
                g = g - outerDark + innerGlow;
                b = b - outerDark * 0.85f + innerGlow;
                boost = spec * 75f;
                break;
              }

            case VisibilityMode.BeerLambertAbsorption:
              {
                float thickness = 0.08f + rimCurve * 0.20f;
                float transmittance = MathF.Exp(-thickness * 1.15f);
                r *= transmittance;
                g *= transmittance * 1.01f;
                b *= Math.Min(1.0f, transmittance * 1.04f);
                boost = spec * 80f + fresnelRim * 0.45f;
                break;
              }

            case VisibilityMode.FloatingContactShadow:
            default:
              break;
          }

          float alpha = Math.Clamp((aaWidth - dist) / (aaWidth * 2f), 0f, 1f);

          byte outA = (byte)(alpha * 255f);
          byte outR = (byte)(Math.Clamp(r + boost, 0f, 255f) * alpha);
          byte outG = (byte)(Math.Clamp(g + boost, 0f, 255f) * alpha);
          byte outB = (byte)(Math.Clamp(b + boost, 0f, 255f) * alpha);

          dstRow[x] = (uint)((outA << 24) | (outR << 16) | (outG << 8) | outB);
        }
      }

      _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, _renderSize, _renderSize));
    }
    finally
    {
      _writeableBitmap.Unlock();
      _captureBmp.UnlockBits(srcData);
    }
  }

  private static unsafe float SampleChannelBlurred(byte* srcBase, int stride, int size, float u, float v, int channelOffset, float blur)
  {
    if (blur <= 0.1f)
      return SampleBilinear(srcBase, stride, size, u, v, channelOffset);

    float c0 = SampleBilinear(srcBase, stride, size, u, v, channelOffset) * 0.40f;
    float c1 = SampleBilinear(srcBase, stride, size, u - blur, v, channelOffset) * 0.15f;
    float c2 = SampleBilinear(srcBase, stride, size, u + blur, v, channelOffset) * 0.15f;
    float c3 = SampleBilinear(srcBase, stride, size, u, v - blur, channelOffset) * 0.15f;
    float c4 = SampleBilinear(srcBase, stride, size, u, v + blur, channelOffset) * 0.15f;
    return c0 + c1 + c2 + c3 + c4;
  }

  private static unsafe float SampleBilinear(byte* srcBase, int stride, int size, float u, float v, int channelOffset)
  {
    u = Math.Clamp(u, 0, size - 1.001f);
    v = Math.Clamp(v, 0, size - 1.001f);

    int x0 = (int)u;
    int y0 = (int)v;
    int x1 = x0 + 1;
    int y1 = y0 + 1;

    float fx = u - x0;
    float fy = v - y0;

    byte* row0 = srcBase + y0 * stride;
    byte* row1 = srcBase + y1 * stride;

    float p00 = row0[x0 * 4 + channelOffset];
    float p10 = row0[x1 * 4 + channelOffset];
    float p01 = row1[x0 * 4 + channelOffset];
    float p11 = row1[x1 * 4 + channelOffset];

    float top = p00 + (p10 - p00) * fx;
    float bot = p01 + (p11 - p01) * fx;
    return top + (bot - top) * fy;
  }

  protected override void OnClosed(EventArgs e)
  {
    CompositionTarget.Rendering -= OnRendering;
    _captureGraphics?.Dispose();
    _captureBmp?.Dispose();
    base.OnClosed(e);
  }
}