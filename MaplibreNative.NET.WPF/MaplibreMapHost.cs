using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MaplibreNative;

namespace MaplibreNative.WPF;

/// <summary>
/// WPF HwndHost that embeds a MapLibre Native OpenGL map.
/// Handles its own OpenGL context, RunLoop, pan/zoom input, and an
/// optional navigation control overlay (zoom-in, zoom-out, reset-north).
/// </summary>
public class MaplibreMapHost : HwndHost
{
    private static readonly string LogPath =
        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map_log.txt");
    private static void Log(string s)
    {
        try { System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {s}\n"); } catch { }
    }

    // ── Public properties ─────────────────────────────────────────────────────

    public string StyleUrl
    {
        get => (string)GetValue(StyleUrlProperty);
        set => SetValue(StyleUrlProperty, value);
    }
    public static readonly DependencyProperty StyleUrlProperty =
        DependencyProperty.Register(nameof(StyleUrl), typeof(string), typeof(MaplibreMapHost),
            new PropertyMetadata("https://demotiles.maplibre.org/style.json", OnStyleUrlChanged));

    private static void OnStyleUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MaplibreMapHost h && h._map != null && e.NewValue is string url)
            h._map.Style.LoadURL(url);
    }

    /// <summary>
    /// Show or hide the built-in navigation control (zoom in/out + north reset).
    /// Default is true.
    /// </summary>
    public bool ShowNavigationControls
    {
        get => (bool)GetValue(ShowNavigationControlsProperty);
        set => SetValue(ShowNavigationControlsProperty, value);
    }
    public static readonly DependencyProperty ShowNavigationControlsProperty =
        DependencyProperty.Register(nameof(ShowNavigationControls), typeof(bool), typeof(MaplibreMapHost),
            new PropertyMetadata(true, OnShowNavigationControlsChanged));

    private static void OnShowNavigationControlsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var h = (MaplibreMapHost)d;
        if (h._navPopup != null)
            h._navPopup.IsOpen = (bool)e.NewValue && h.IsVisible;
    }

    // ── Public camera helpers ─────────────────────────────────────────────────

    public void CenterOn(double latitude, double longitude, double zoom = 14.0)
    {
        if (_map == null) return;
        var cam = new CameraOptions();
        cam.Center = new LatLng(latitude, longitude);
        cam.Zoom   = zoom;
        _map.JumpTo(cam);
        _renderNeedsUpdate = true;
    }

    public void ZoomIn()
    {
        if (_map == null) return;
        _map.ScaleBy(2.0, new System.Nullable<PointDouble>());
        _renderNeedsUpdate = true;
    }

    public void ZoomOut()
    {
        if (_map == null) return;
        _map.ScaleBy(0.5, new System.Nullable<PointDouble>());
        _renderNeedsUpdate = true;
    }

    public void ResetNorth()
    {
        if (_map == null) return;
        var cam = new CameraOptions();
        cam.Bearing = 0.0;
        _map.JumpTo(cam);
        _renderNeedsUpdate = true;
    }

    // ── GeoJSON layer management ──────────────────────────────────────────────
    // These methods call APIs added to Style in the C++/CLI wrapper. They use
    // reflection so the WPF library keeps compiling against older DLL builds
    // that may not have those methods yet (pre-release DLL).

    private static System.Reflection.MethodInfo? _miAddGeoJsonSource;
    private static System.Reflection.MethodInfo? _miSetGeoJsonSourceUrl;
    private static System.Reflection.MethodInfo? _miSetGeoJsonSourceData;
    private static System.Reflection.MethodInfo? _miHasSource;
    private static System.Reflection.MethodInfo? _miRemoveSource;
    private static System.Reflection.MethodInfo? _miAddCircleLayer;
    private static System.Reflection.MethodInfo? _miHasLayer;
    private static System.Reflection.MethodInfo? _miRemoveLayer;
    private static bool _styleApiResolved;

    private static void EnsureStyleApiResolved(object styleObj)
    {
        if (_styleApiResolved) return;
        _styleApiResolved = true;
        var t = styleObj.GetType();
        _miAddGeoJsonSource    = t.GetMethod("AddGeoJsonSource",    [typeof(string), typeof(string)]);
        _miSetGeoJsonSourceUrl = t.GetMethod("SetGeoJsonSourceUrl", [typeof(string), typeof(string)]);
        _miSetGeoJsonSourceData= t.GetMethod("SetGeoJsonSourceData",[typeof(string), typeof(string)]);
        _miHasSource           = t.GetMethod("HasSource",           [typeof(string)]);
        _miRemoveSource        = t.GetMethod("RemoveSource",        [typeof(string)]);
        _miAddCircleLayer      = t.GetMethod("AddCircleLayer",      [typeof(string), typeof(string), typeof(string), typeof(float), typeof(float), typeof(string)]);
        _miHasLayer            = t.GetMethod("HasLayer",            [typeof(string)]);
        _miRemoveLayer         = t.GetMethod("RemoveLayer",         [typeof(string)]);
    }

    private static bool StyleApiAvailable => _miAddGeoJsonSource != null;

    /// <summary>
    /// Add or update a GeoJSON source that fetches from a URL, with circle
    /// layers for open (green), WEP (orange) and secure (red) access points.
    /// Requires a DLL build that includes the GeoJSON source/layer APIs.
    /// </summary>
    public void SetWifiGeoJsonLayer(string sourceId, string geoJsonUrl)
    {
        if (_map == null) return;
        var style = _map.Style;
        EnsureStyleApiResolved(style);
        if (!StyleApiAvailable) { Log("SetWifiGeoJsonLayer: Style API not available in this DLL build"); return; }

        bool hasSource = (bool)_miHasSource!.Invoke(style, [sourceId])!;
        if (!hasSource)
        {
            _miAddGeoJsonSource!.Invoke(style, [sourceId, geoJsonUrl]);
            _AddWifiCircleLayers(style, sourceId);
        }
        else
        {
            _miSetGeoJsonSourceUrl!.Invoke(style, [sourceId, geoJsonUrl]);
        }
        _renderNeedsUpdate = true;
    }

    /// <summary>
    /// Update an existing GeoJSON source with inline GeoJSON string data.
    /// Use this for live/frequently-updated data to avoid HTTP round-trips.
    /// If the source does not exist yet, it is created (with circle layers).
    /// Requires a DLL build that includes the GeoJSON source/layer APIs.
    /// </summary>
    public void SetWifiGeoJsonLayerData(string sourceId, string geoJson)
    {
        if (_map == null) return;
        var style = _map.Style;
        EnsureStyleApiResolved(style);
        if (!StyleApiAvailable) { Log("SetWifiGeoJsonLayerData: Style API not available in this DLL build"); return; }

        bool hasSource = (bool)_miHasSource!.Invoke(style, [sourceId])!;
        if (!hasSource)
        {
            _miAddGeoJsonSource!.Invoke(style, [sourceId, ""]);
            _AddWifiCircleLayers(style, sourceId);
        }
        _miSetGeoJsonSourceData!.Invoke(style, [sourceId, geoJson]);
        _renderNeedsUpdate = true;
    }

    /// <summary>Remove a GeoJSON wifi layer set (3 security-type circle layers + source).</summary>
    public void RemoveWifiGeoJsonLayer(string sourceId)
    {
        if (_map == null) return;
        var style = _map.Style;
        EnsureStyleApiResolved(style);
        if (!StyleApiAvailable) return;

        _miRemoveLayer!.Invoke(style, [sourceId + "_open"]);
        _miRemoveLayer!.Invoke(style, [sourceId + "_wep"]);
        _miRemoveLayer!.Invoke(style, [sourceId + "_secure"]);
        _miRemoveSource!.Invoke(style, [sourceId]);
        _renderNeedsUpdate = true;
    }

    private static void _AddWifiCircleLayers(object style, string sourceId)
    {
        void AddIfMissing(string lid, string color)
        {
            bool has = (bool)_miHasLayer!.Invoke(style, [lid])!;
            if (!has) _miAddCircleLayer!.Invoke(style, [lid, sourceId, color, 5f, 0.85f, null]);
        }
        AddIfMissing(sourceId + "_open",   "#00802b");  // green
        AddIfMissing(sourceId + "_wep",    "#cc7a00");  // orange
        AddIfMissing(sourceId + "_secure", "#b30000");  // red
    }

    // ── Win32 / WGL P/Invoke ──────────────────────────────────────────────────

    [DllImport("user32.dll")] private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int   ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hDC);
    [DllImport("opengl32.dll")] private static extern bool   wglDeleteContext(IntPtr hGLRC);
    [DllImport("opengl32.dll")] private static extern bool   wglMakeCurrent(IntPtr hDC, IntPtr hGLRC);
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string procName);
    [DllImport("opengl32.dll")] private static extern void   glViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll")] private static extern uint   glGetError();
    [DllImport("opengl32.dll")] private static extern void   glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] private static extern void   glClear(uint mask);

    private delegate void glBindFramebufferDelegate(uint target, uint framebuffer);
    private glBindFramebufferDelegate? glBindFramebuffer;

    private const uint GL_COLOR_BUFFER_BIT   = 0x00004000;
    private const uint GL_DEPTH_BUFFER_BIT   = 0x00000100;
    private const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    private const uint GL_FRAMEBUFFER        = 0x8D40;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WglCreateContextAttribsARBDelegate(IntPtr hDC, IntPtr hShareContext, int[] attribList);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct WNDCLASSEXA
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPStr)] public string  lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)] private static extern ushort RegisterClassExA(ref WNDCLASSEXA wc);
    [DllImport("user32.dll")]                          private static extern IntPtr  DefWindowProcA(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")]                        private static extern IntPtr  GetModuleHandleW(IntPtr lpModuleName);

    private const int  WGL_CONTEXT_MAJOR_VERSION_ARB         = 0x2091;
    private const int  WGL_CONTEXT_MINOR_VERSION_ARB         = 0x2092;
    private const int  WGL_CONTEXT_PROFILE_MASK_ARB          = 0x9126;
    private const uint CS_OWNDC                              = 0x0020;
    private const string MapLibreWindowClass                 = "MapLibreGLChild";

    private static WndProcDelegate? _wndProcKeepAlive;
    private static bool             _classRegistered;

    private static void EnsureWindowClassRegistered()
    {
        if (_classRegistered) return;
        _wndProcKeepAlive = (hWnd, msg, w, l) => DefWindowProcA(hWnd, msg, w, l);
        var wc = new WNDCLASSEXA
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEXA>(),
            style         = CS_OWNDC,
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
            hInstance     = GetModuleHandleW(IntPtr.Zero),
            lpszClassName = MapLibreWindowClass,
        };
        RegisterClassExA(ref wc);
        _classRegistered = true;
    }

    [DllImport("gdi32.dll")] private static extern int  ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool   ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool   ReleaseCapture();

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize, nVersion;
        public uint   dwFlags;
        public byte   iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte   cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte   cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint   dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;
    private const uint PFD_DOUBLEBUFFER   = 0x00000001;
    private const uint WS_CHILD           = 0x40000000;
    private const uint WS_VISIBLE         = 0x10000000;
    private const uint WS_CLIPCHILDREN    = 0x02000000;
    private const uint WS_CLIPSIBLINGS    = 0x04000000;

    // ── State ─────────────────────────────────────────────────────────────────

    private IntPtr _childHwnd = IntPtr.Zero;
    private IntPtr _hDC       = IntPtr.Zero;
    private IntPtr _hGLRC     = IntPtr.Zero;

    private ExternalRenderingContextFrontend? _frontend;
    private Map?                              _map;
    private RunLoop?                          _runLoop;
    private DispatcherTimer?                  _renderTimer;
    private bool                              _glReady;
    private bool                              _initialized;
    private bool                              _renderNeedsUpdate = true;
    private float                             _dpi = 1.0f;
    private int                               _renderTickCount;

    // ── Input state ───────────────────────────────────────────────────────────

    private bool  _isDragging;
    private short _lastMouseX;
    private short _lastMouseY;

    // ── Navigation popup ──────────────────────────────────────────────────────

    private Popup?          _navPopup;
    private RotateTransform? _compassRotate;

    // ── HwndHost overrides ────────────────────────────────────────────────────

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureWindowClassRegistered();
        int initW = Math.Max(1, (int)ActualWidth);
        int initH = Math.Max(1, (int)ActualHeight);
        _childHwnd = CreateWindowEx(
            0, MapLibreWindowClass, "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, initW, initH,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        IsVisibleChanged += OnIsVisibleChanged;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)TryInitialize);

        return new HandleRef(this, _childHwnd);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool visible = (bool)e.NewValue;
        if (visible)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)TryInitialize);
        else
            _renderTimer?.Stop();

        if (_navPopup != null)
            _navPopup.IsOpen = visible && ShowNavigationControls;
    }

    private void TryInitialize()
    {
        if (_initialized) { _renderTimer?.Start(); return; }
        if (!IsVisible || ActualWidth < 2 || ActualHeight < 2 || _childHwnd == IntPtr.Zero) return;

        _dpi = GetDpiScale();
        int physW = Math.Max(1, (int)(ActualWidth  * _dpi));
        int physH = Math.Max(1, (int)(ActualHeight * _dpi));
        SetWindowPos(_childHwnd, IntPtr.Zero, 0, 0, physW, physH, 0x0040);

        _initialized = true;
        try { InitOpenGl();    Log("InitOpenGl OK"); } catch (Exception ex) { Log($"InitOpenGl EX: {ex}"); throw; }
        try { InitMaplibre();  Log("InitMaplibre OK"); } catch (Exception ex) { Log($"InitMaplibre EX: {ex}"); throw; }
        try { InitNavPopup();  } catch (Exception ex) { Log($"InitNavPopup EX: {ex}"); }

        // Hide nav popup when the host window loses focus so it doesn't float
        // over unrelated windows (WPF Popup creates its own top-level HWND).
        var parentWin = System.Windows.Window.GetWindow(this);
        if (parentWin != null)
        {
            parentWin.Deactivated += (_, _) => { if (_navPopup != null) _navPopup.IsOpen = false; };
            parentWin.Activated   += (_, _) => { if (_navPopup != null) _navPopup.IsOpen = ShowNavigationControls && IsVisible; };
        }

        _renderTimer?.Start();
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        IsVisibleChanged -= OnIsVisibleChanged;
        _renderTimer?.Stop();
        _renderTimer = null;

        if (_navPopup != null) { _navPopup.IsOpen = false; _navPopup = null; }

        _map?.Dispose();      _map      = null;
        _frontend?.Dispose(); _frontend = null;
        _runLoop?.Dispose();  _runLoop  = null;

        if (_hGLRC != IntPtr.Zero) { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(_hGLRC); _hGLRC = IntPtr.Zero; }
        if (_hDC   != IntPtr.Zero) { ReleaseDC(_childHwnd, _hDC); _hDC = IntPtr.Zero; }
        if (_childHwnd != IntPtr.Zero) { DestroyWindow(_childHwnd); _childHwnd = IntPtr.Zero; }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        float dpi = GetDpiScale();
        int wP = Math.Max(1, (int)(info.NewSize.Width  * dpi));
        int hP = Math.Max(1, (int)(info.NewSize.Height * dpi));

        if (_childHwnd != IntPtr.Zero)
            SetWindowPos(_childHwnd, IntPtr.Zero, 0, 0, wP, hP, 0x0040);

        if (_frontend != null)
        {
            _map?.SetSize(new MaplibreNative.Size((uint)info.NewSize.Width, (uint)info.NewSize.Height));
            _frontend.Backend.Size = new MaplibreNative.Size((uint)wP, (uint)hP);
        }
        _renderNeedsUpdate = true;

        PositionNavPopup();

        if (!_initialized && IsVisible)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)TryInitialize);
    }

    // ── Win32 mouse input → MapLibre camera ───────────────────────────────────

    private const int WM_LBUTTONDOWN    = 0x0201;
    private const int WM_LBUTTONUP      = 0x0202;
    private const int WM_LBUTTONDBLCLK  = 0x0203;
    private const int WM_MOUSEMOVE      = 0x0200;
    private const int WM_MOUSEWHEEL     = 0x020A;

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (hwnd == _childHwnd && _map != null)
        {
            long lp   = lParam.ToInt64();
            short cx  = (short)(lp & 0xFFFF);
            short cy  = (short)((lp >> 16) & 0xFFFF);

            switch (msg)
            {
                case WM_LBUTTONDOWN:
                    _isDragging = true;
                    _lastMouseX = cx;
                    _lastMouseY = cy;
                    SetCapture(hwnd);
                    handled = true;
                    break;

                case WM_LBUTTONDBLCLK:
                {
                    // Cancel any drag initiated by the preceding WM_LBUTTONDOWN,
                    // then zoom in 1 level centred on the cursor.
                    _isDragging = false;
                    ReleaseCapture();
                    double anchorX = cx / _dpi;
                    double anchorY = cy / _dpi;
                    _map.ScaleBy(2.0, new System.Nullable<PointDouble>(new PointDouble(anchorX, anchorY)));
                    _renderNeedsUpdate = true;
                    handled = true;
                    break;
                }

                case WM_MOUSEMOVE:
                    if (_isDragging)
                    {
                        double dx = (cx - _lastMouseX) / _dpi;
                        double dy = (cy - _lastMouseY) / _dpi;
                        _map.MoveBy(new PointDouble(dx, dy));
                        _lastMouseX = cx;
                        _lastMouseY = cy;
                        _renderNeedsUpdate = true;
                        handled = true;
                    }
                    break;

                case WM_LBUTTONUP:
                    _isDragging = false;
                    ReleaseCapture();
                    handled = true;
                    break;

                case WM_MOUSEWHEEL:
                {
                    int wheelDelta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    var pt = new POINT { X = (short)(lp & 0xFFFF), Y = (short)((lp >> 16) & 0xFFFF) };
                    ScreenToClient(hwnd, ref pt);
                    double anchorX = pt.X / _dpi;
                    double anchorY = pt.Y / _dpi;
                    double scale   = Math.Pow(1.15, wheelDelta / 120.0);
                    _map.ScaleBy(scale, new System.Nullable<PointDouble>(new PointDouble(anchorX, anchorY)));
                    _renderNeedsUpdate = true;
                    handled = true;
                    break;
                }
            }
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    // ── Navigation popup ──────────────────────────────────────────────────────

    private void InitNavPopup()
    {
        // Outer border groups all three buttons with a shared drop-shadow.
        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Effect = new DropShadowEffect
            {
                BlurRadius  = 6,
                ShadowDepth = 2,
                Opacity     = 0.25,
                Color       = Colors.Black,
                Direction   = 270,
            },
        };

        var panel = new StackPanel { Width = 29 };
        outerBorder.Child = panel;

        // Zoom-in (+)
        var zoomInBtn = MakeNavButton("+", ZoomIn);
        SetButtonCorners(zoomInBtn, topLeft: 4, topRight: 4, bottomRight: 0, bottomLeft: 0);
        panel.Children.Add(zoomInBtn);

        // Divider
        panel.Children.Add(new Border
        {
            Height     = 1,
            Background = new SolidColorBrush(Color.FromRgb(218, 218, 218)),
        });

        // Zoom-out (−)
        var zoomOutBtn = MakeNavButton("\u2212", ZoomOut);
        SetButtonCorners(zoomOutBtn, topLeft: 0, topRight: 0, bottomRight: 0, bottomLeft: 0);
        panel.Children.Add(zoomOutBtn);

        // Divider
        panel.Children.Add(new Border
        {
            Height     = 1,
            Background = new SolidColorBrush(Color.FromRgb(218, 218, 218)),
        });

        // Compass / reset-north (↑, rotates with map bearing)
        _compassRotate = new RotateTransform { CenterX = 0.5, CenterY = 0.5 };
        var compassIcon = new TextBlock
        {
            Text                = "\u2191",  // ↑
            FontSize            = 16,
            FontWeight          = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            IsHitTestVisible    = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform     = _compassRotate,
        };
        var compassBtn = MakeNavButton(null, ResetNorth);
        SetButtonCorners(compassBtn, topLeft: 0, topRight: 0, bottomRight: 4, bottomLeft: 4);
        compassBtn.Child = compassIcon;
        panel.Children.Add(compassBtn);

        _navPopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen          = true,
            IsHitTestVisible   = true,
            PlacementTarget    = this,
            Placement          = PlacementMode.Relative,
            Child              = outerBorder,
        };

        PositionNavPopup();
        _navPopup.IsOpen = ShowNavigationControls && IsVisible;
    }

    /// <summary>Creates a 29×29 white nav button with hover effect.</summary>
    private static Border MakeNavButton(string? text, Action onClick)
    {
        var btn = new Border
        {
            Width      = 29,
            Height     = 29,
            Background = Brushes.White,
            Cursor     = Cursors.Hand,
        };

        if (text != null)
        {
            btn.Child = new TextBlock
            {
                Text                = text,
                FontSize            = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                IsHitTestVisible    = false,
            };
        }

        btn.MouseLeftButtonDown += (_, e) => { onClick(); e.Handled = true; };
        btn.MouseEnter          += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        btn.MouseLeave          += (_, _) => btn.Background = Brushes.White;
        return btn;
    }

    private static void SetButtonCorners(Border btn, double topLeft, double topRight, double bottomRight, double bottomLeft)
        => btn.CornerRadius = new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);

    private void PositionNavPopup()
    {
        if (_navPopup == null) return;
        const double margin      = 10;
        const double panelWidth  = 29;
        // Place in the top-right corner (mirrors maplibre-gl-js default).
        _navPopup.HorizontalOffset = ActualWidth  - panelWidth - margin;
        _navPopup.VerticalOffset   = margin;
    }

    private void UpdateCompassBearing()
    {
        if (_compassRotate == null || _map == null) return;
        double bearing = _map.GetCameraOptions()?.Bearing ?? 0.0;
        // Rotate the arrow counter-clockwise to indicate bearing offset from north.
        _compassRotate.Angle = -bearing;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float GetDpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        var m   = src?.CompositionTarget?.TransformToDevice.M11;
        return (float)(m ?? 1.0);
    }

    // ── OpenGL init ───────────────────────────────────────────────────────────

    private void InitOpenGl()
    {
        if (_glReady) return;
        _hDC = GetDC(_childHwnd);
        Log($"InitOpenGl: hDC={_hDC}");

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize        = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion     = 1,
            dwFlags      = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType   = 0,
            cColorBits   = 24,
            cDepthBits   = 24,
            cStencilBits = 8,
        };

        int fmt = ChoosePixelFormat(_hDC, ref pfd);
        SetPixelFormat(_hDC, fmt, ref pfd);
        Log($"InitOpenGl: format={fmt}");

        IntPtr tempCtx = wglCreateContext(_hDC);
        wglMakeCurrent(_hDC, tempCtx);

        IntPtr pAttribs = wglGetProcAddress("wglCreateContextAttribsARB");
        if (pAttribs != IntPtr.Zero)
        {
            var createAttribs = Marshal.GetDelegateForFunctionPointer<WglCreateContextAttribsARBDelegate>(pAttribs);
            int[] attribs =
            [
                WGL_CONTEXT_MAJOR_VERSION_ARB, 3,
                WGL_CONTEXT_MINOR_VERSION_ARB, 3,
                WGL_CONTEXT_PROFILE_MASK_ARB,  0x00000002, // Compatibility Profile
                0,
            ];
            _hGLRC = createAttribs(_hDC, IntPtr.Zero, attribs);
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(tempCtx);
            wglMakeCurrent(_hDC, _hGLRC);
        }
        else
        {
            _hGLRC = tempCtx;
        }

        IntPtr pBindFb = wglGetProcAddress("glBindFramebuffer");
        if (pBindFb != IntPtr.Zero)
            glBindFramebuffer = Marshal.GetDelegateForFunctionPointer<glBindFramebufferDelegate>(pBindFb);

        _glReady = true;
        Log($"InitOpenGl done: hGLRC={_hGLRC}");
    }

    // ── MapLibre init ─────────────────────────────────────────────────────────

    private void InitMaplibre()
    {
        float dpi = _dpi;
        int wL = Math.Max(1, (int)ActualWidth);
        int hL = Math.Max(1, (int)ActualHeight);
        int wP = Math.Max(1, (int)(ActualWidth  * dpi));
        int hP = Math.Max(1, (int)(ActualHeight * dpi));

        var sizeLogical  = new MaplibreNative.Size((uint)wL, (uint)hL);
        var sizePhysical = new MaplibreNative.Size((uint)wP, (uint)hP);

        // MapLibre uses libuv for async I/O. A RunLoop on this thread is required
        // before creating the Map; RunOnce() is called every timer tick to pump it.
        _runLoop = new RunLoop(RunLoop.Type.New);

        _frontend = new ExternalRenderingContextFrontend(_hDC, _hGLRC, sizePhysical, dpi);
        _frontend.Updated += _ => { _renderNeedsUpdate = true; };
        Log($"InitMaplibre: logical={wL}x{hL} physical={wP}x{hP} dpi={dpi}");

        var mapOptions = new MapOptions()
            .WithMapMode(MapMode.Continuous)
            .WithSize(sizeLogical)
            .WithPixelRatio(dpi);

        var resOptions = new ResourceOptions()
            .WithCachePath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maplibre_cache.db"))
            .WithAssetPath(AppDomain.CurrentDomain.BaseDirectory);

        var obs = new MaplibreNative.WPF.DelegateMapObserver((type, msg) =>
            Log($"[MapObserver:{type}] {msg}"));

        _map = new Map(_frontend, obs, mapOptions, resOptions);
        Log("Map created");

        _map.Style.LoadURL(StyleUrl);
        Log($"Style.LoadURL: {StyleUrl}");

        var initCam = new CameraOptions();
        initCam.Center = new LatLng(20.0, 0.0);
        initCam.Zoom   = 2.0;
        _map.JumpTo(initCam);

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        // Sync child window size if it has drifted (e.g. after DPI change).
        if (_frontend != null && _map != null && ActualWidth > 1 && ActualHeight > 1)
        {
            float curDpi = GetDpiScale();
            var   sizePhys = new MaplibreNative.Size(
                (uint)(ActualWidth * curDpi), (uint)(ActualHeight * curDpi));
            var backendSize = _frontend.Backend.Size;
            if (backendSize.Width != sizePhys.Width || backendSize.Height != sizePhys.Height)
            {
                _map.SetSize(new MaplibreNative.Size((uint)ActualWidth, (uint)ActualHeight));
                _frontend.Backend.Size = sizePhys;
                if (_childHwnd != IntPtr.Zero)
                    SetWindowPos(_childHwnd, IntPtr.Zero, 0, 0,
                        (int)sizePhys.Width, (int)sizePhys.Height, 0x0040);
                _renderNeedsUpdate = true;
            }
        }

        // Pump libuv so HTTP responses for style/tiles are delivered.
        _runLoop?.RunOnce();

        if (_renderNeedsUpdate && _glReady && _frontend != null)
        {
            _renderNeedsUpdate = false;
            wglMakeCurrent(_hDC, _hGLRC);

            var sz = _frontend.Backend.Size;
            glBindFramebuffer?.Invoke(GL_FRAMEBUFFER, 0);
            glViewport(0, 0, (int)Math.Max(1, sz.Width), (int)Math.Max(1, sz.Height));
            glClearColor(0f, 0f, 0f, 1f);
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);

            try { _frontend.Render(); } catch (Exception ex) { Log($"Render EX: {ex}"); }

            SwapBuffers(_hDC);

            _renderTickCount++;
            if (_renderTickCount <= 5 || _renderTickCount % 120 == 0)
                Log($"Render #{_renderTickCount}");

            // Update compass arrow to reflect current bearing.
            UpdateCompassBearing();
        }
    }
}
