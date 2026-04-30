using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MaplibreNative;

namespace Vistumbler.UI.Controls;

public class MaplibreMapHost : HwndHost
{
    private static readonly string LogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map_log.txt");
    private static void Log(string s)
    {
        try { System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {s}\n"); } catch { }
    }
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

    public void CenterOn(double latitude, double longitude, double zoom = 14.0)
    {
        if (_map == null) return;
        var cam = new CameraOptions();
        cam.Center = new LatLng(latitude, longitude);
        cam.Zoom   = zoom;
        _map.JumpTo(cam);
        _renderNeedsUpdate = true;
    }

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
    [DllImport("opengl32.dll")] private static extern void   glEnable(uint cap);

    private delegate void glBindFramebufferDelegate(uint target, uint framebuffer);
    private glBindFramebufferDelegate? glBindFramebuffer;

    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
    private const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    private const uint GL_FRAMEBUFFER = 0x8D40;
    private const uint GL_BLEND = 0x0BE2;

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
    private const int  WGL_CONTEXT_ES2_PROFILE_BIT_EXT       = 0x00000004; 
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
        ushort atom = RegisterClassExA(ref wc);
        _classRegistered = true;
    }

    [DllImport("gdi32.dll")] private static extern int  ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);

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

    private const uint PFD_DRAW_TO_WINDOW   = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL   = 0x00000020;
    private const uint PFD_DOUBLEBUFFER     = 0x00000001;
    private const uint WS_CHILD             = 0x40000000;
    private const uint WS_VISIBLE          = 0x10000000;
    private const uint WS_CLIPCHILDREN      = 0x02000000;
    private const uint WS_CLIPSIBLINGS      = 0x04000000;

    private IntPtr _childHwnd  = IntPtr.Zero;
    private IntPtr _hDC        = IntPtr.Zero;
    private IntPtr _hGLRC      = IntPtr.Zero;

    private ExternalRenderingContextFrontend? _frontend;
    private Map?                              _map;
    private RunLoop?                          _runLoop;
    private DispatcherTimer?                  _renderTimer;
    private bool                              _glReady;
    private bool                              _initialized;
    private bool                              _renderNeedsUpdate = true;
    private float                             _dpi = 1.0f;
    private int                               _renderTickCount;

    private float GetDpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        var m = src?.CompositionTarget?.TransformToDevice.M11;
        return (float)(m ?? 1.0);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureWindowClassRegistered();
        // Try to create the child HWND at the layout size if available; we'll resize again in OnRenderSizeChanged.
        int initW = Math.Max(1, (int)ActualWidth);
        int initH = Math.Max(1, (int)ActualHeight);
        _childHwnd = CreateWindowEx(
            0, MapLibreWindowClass, "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, initW, initH,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        IsVisibleChanged += OnIsVisibleChanged;
        // Defer init until layout has given us a real size.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)TryInitialize);

        return new HandleRef(this, _childHwnd);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)TryInitialize);
        else
            _renderTimer?.Stop();
    }

    private void TryInitialize()
    {
        if (_initialized)
        {
            _renderTimer?.Start();
            return;
        }
        if (!IsVisible) { Log($"TryInitialize: not visible, defer"); return; }
        if (ActualWidth < 2 || ActualHeight < 2) { Log($"TryInitialize: size too small {ActualWidth}x{ActualHeight}"); return; }
        if (_childHwnd == IntPtr.Zero) { Log("TryInitialize: child HWND is null"); return; }

        _dpi = GetDpiScale();
        int physW = Math.Max(1, (int)(ActualWidth  * _dpi));
        int physH = Math.Max(1, (int)(ActualHeight * _dpi));
        Log($"TryInitialize: ActualSize={ActualWidth}x{ActualHeight} dpi={_dpi} physical={physW}x{physH}");
        SetWindowPos(_childHwnd, IntPtr.Zero, 0, 0, physW, physH, 0x0040);

        _initialized = true;
        try { InitOpenGl(); Log("InitOpenGl OK"); }
        catch (Exception ex) { Log($"InitOpenGl EX: {ex}"); throw; }
        try { InitMaplibre(); Log("InitMaplibre OK"); }
        catch (Exception ex) { Log($"InitMaplibre EX: {ex}"); throw; }
        _renderTimer?.Start();
        Log("Render timer started");
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        IsVisibleChanged -= OnIsVisibleChanged;
        _renderTimer?.Stop();
        _renderTimer = null;

        _map?.Dispose();
        _map = null;
        _frontend?.Dispose();
        _frontend = null;
        _runLoop?.Dispose();
        _runLoop = null;

        if (_hGLRC != IntPtr.Zero) { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(_hGLRC); _hGLRC = IntPtr.Zero; }
        if (_hDC   != IntPtr.Zero) { ReleaseDC(_childHwnd, _hDC); _hDC = IntPtr.Zero; }
        if (_childHwnd != IntPtr.Zero) { DestroyWindow(_childHwnd); _childHwnd = IntPtr.Zero; }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        float dpi = GetDpiScale();
        int wL = Math.Max(1, (int)info.NewSize.Width);
        int hL = Math.Max(1, (int)info.NewSize.Height);
        int wP = Math.Max(1, (int)(info.NewSize.Width  * dpi));
        int hP = Math.Max(1, (int)(info.NewSize.Height * dpi));

        if (_childHwnd != IntPtr.Zero)
            SetWindowPos(_childHwnd, IntPtr.Zero, 0, 0, wP, hP, 0x0040);

        if (_frontend != null)
        {
            _map?.SetSize(new MaplibreNative.Size((uint)wL, (uint)hL));
            _frontend.Backend.Size = new MaplibreNative.Size((uint)wP, (uint)hP);
        }
        _renderNeedsUpdate = true;

        // If init was waiting for a real size, kick it off now.
        if (!_initialized && IsVisible)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)TryInitialize);
    }

    private void InitOpenGl()
    {
        if (_glReady) return;
        _hDC = GetDC(_childHwnd);
        Log($"InitOpenGl: hDC={_hDC}");

        var pfd = new PIXELFORMATDESCRIPTOR();
        pfd.nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>();
        pfd.nVersion = 1;
        pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        pfd.iPixelType = 0;
        pfd.cColorBits = 24;
        pfd.cDepthBits = 24;
        pfd.cStencilBits = 8;
        pfd.iLayerType = 0;

        int format = ChoosePixelFormat(_hDC, ref pfd);
        bool spf = SetPixelFormat(_hDC, format, ref pfd);
        Log($"InitOpenGl: format={format} SetPixelFormat={spf}");

        IntPtr tempContext = wglCreateContext(_hDC);
        wglMakeCurrent(_hDC, tempContext);

        IntPtr pCreateContextAttribsARB = wglGetProcAddress("wglCreateContextAttribsARB");
        if (pCreateContextAttribsARB != IntPtr.Zero)
        {
            var createAttribs = Marshal.GetDelegateForFunctionPointer<WglCreateContextAttribsARBDelegate>(pCreateContextAttribsARB);
            var attribs = new[] { 
                WGL_CONTEXT_MAJOR_VERSION_ARB, 3, 
                WGL_CONTEXT_MINOR_VERSION_ARB, 3, 
                WGL_CONTEXT_PROFILE_MASK_ARB, 0x00000002, // WGL_CONTEXT_COMPATIBILITY_PROFILE_BIT
                0 
            };
            _hGLRC = createAttribs(_hDC, IntPtr.Zero, attribs);
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(tempContext);
            wglMakeCurrent(_hDC, _hGLRC);
        }
        else
        {
            _hGLRC = tempContext;
        }

        IntPtr pBindFb = wglGetProcAddress("glBindFramebuffer");
        if (pBindFb != IntPtr.Zero) {
            glBindFramebuffer = Marshal.GetDelegateForFunctionPointer<glBindFramebufferDelegate>(pBindFb);
        }
        _glReady = true;
        Log($"InitOpenGl done: hGLRC={_hGLRC} glBindFramebuffer={(pBindFb != IntPtr.Zero)}");
    }

    private void InitMaplibre()
    {
        float dpi = _dpi;
        int wL = Math.Max(1, (int)ActualWidth);
        int hL = Math.Max(1, (int)ActualHeight);
        int wP = Math.Max(1, (int)(ActualWidth  * dpi));
        int hP = Math.Max(1, (int)(ActualHeight * dpi));

        var sizeLogical  = new MaplibreNative.Size((uint)wL, (uint)hL);
        var sizePhysical = new MaplibreNative.Size((uint)wP, (uint)hP);

        _runLoop = new RunLoop(RunLoop.Type.New);

        _frontend = new ExternalRenderingContextFrontend(_hDC, _hGLRC, sizePhysical, dpi);
        _frontend.Updated += (parameters) => { _renderNeedsUpdate = true; };
        Log($"InitMaplibre: frontend created, sizeLogical={wL}x{hL} sizePhysical={wP}x{hP} dpi={dpi}");

        var mapOptions = new MapOptions().WithMapMode(MapMode.Continuous).WithSize(sizeLogical).WithPixelRatio(dpi);
        var resOptions = new ResourceOptions()
            .WithCachePath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maplibre_cache.db"))
            .WithAssetPath(AppDomain.CurrentDomain.BaseDirectory);
        
        var obs = new DelegateMapObserver((type, msg) => {
            System.IO.File.AppendAllText("map_log.txt", $"[MapObserver:{type}] {msg}\n");
        });
        
        _map = new Map(_frontend, obs, mapOptions, resOptions);
        Log("Map created");
        _map.Style.LoadURL(StyleUrl);
        Log($"Style.LoadURL: {StyleUrl}");
        
        var initCam = new CameraOptions();
        initCam.Center = new LatLng(40.7128, -74.0060); // NYC
        initCam.Zoom = 12.0; // Zoom in more
        _map.JumpTo(initCam);

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (_, _) =>
        {
            if (_frontend != null && _map != null && ActualWidth > 1 && ActualHeight > 1) {
                float curDpi = GetDpiScale();
                if (curDpi <= 0) curDpi = _dpi;
                var sizeLog  = new MaplibreNative.Size((uint)ActualWidth, (uint)ActualHeight);
                var sizePhys = new MaplibreNative.Size((uint)(ActualWidth * curDpi), (uint)(ActualHeight * curDpi));
                var backendSize = _frontend.Backend.Size;
                if (backendSize.Width != sizePhys.Width || backendSize.Height != sizePhys.Height) {
                    Log($"Tick: resize backend {backendSize.Width}x{backendSize.Height} -> {sizePhys.Width}x{sizePhys.Height}");
                    _map.SetSize(sizeLog);
                    _frontend.Backend.Size = sizePhys;
                    if (_childHwnd != IntPtr.Zero) SetWindowPos(_childHwnd, IntPtr.Zero, 0, 0, (int)sizePhys.Width, (int)sizePhys.Height, 0x0040);
                    _renderNeedsUpdate = true;
                }
            }

            _runLoop?.RunOnce();

            if (_renderNeedsUpdate && _glReady && _frontend != null)
            {
                _renderNeedsUpdate = false;
                bool mc = wglMakeCurrent(_hDC, _hGLRC);

                var sz = _frontend.Backend.Size;
                int vw = (int)Math.Max(1, sz.Width);
                int vh = (int)Math.Max(1, sz.Height);

                glBindFramebuffer?.Invoke(GL_FRAMEBUFFER, 0);
                glViewport(0, 0, vw, vh);
                uint err1 = glGetError();

                glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT);
                uint err2 = glGetError();

                bool renderOk = false;
                try {
                    _frontend.Render();
                    renderOk = true;
                } catch (Exception ex) {
                    Log($"Render EX: {ex}");
                }
                uint err3 = glGetError();
                bool sb = SwapBuffers(_hDC);
                uint err4 = glGetError();

                _renderTickCount++;
                if (_renderTickCount <= 5 || _renderTickCount % 60 == 0)
                    Log($"Render #{_renderTickCount}: makeCurrent={mc} viewport={vw}x{vh} render={renderOk} swap={sb} errs={err1:X}/{err2:X}/{err3:X}/{err4:X}");
            }
        };
    }
}

public class DelegateMapObserver : MapObserver
{
    private readonly Action<string, string> _log;
    public DelegateMapObserver(Action<string, string> log) { _log = log; }
    protected override void onDidFailLoadingMap(MapLoadError type, string description) { _log("Fail", $"[{type}] {description}"); }
    protected override void onDidFinishLoadingMap() { _log("Finish", "Map loaded"); }
    protected override void onWillStartLoadingMap() { _log("Will", "WillStartLoading"); }
    protected override void onDidFinishLoadingStyle() { _log("Style", "DidFinishLoadingStyle"); }
    protected override void onDidFinishRenderingFrame(RenderFrameStatus status) { _log("FrameDone", $"mode={status.mode} needsRepaint={status.needsRepaint} placementChanged={status.placementChanged}"); }
}
