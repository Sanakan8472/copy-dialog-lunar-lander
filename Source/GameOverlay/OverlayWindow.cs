using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.ComponentModel;

namespace CopyDialogLunarLander
{
    /// <summary>
    /// Current frame stats.
    /// </summary>
    public struct OverlayStats
    {
        public double captureTime;
        public double updateTime;
        public double frameTimer;
        public double fps;

        public string GetStatsString()
        {
            return $"Capture: {captureTime.ToString("0.00")} Update: {updateTime.ToString("0.00")} FPS: {fps.ToString("00.00")} Frame: {frameTimer.ToString("00.00")}";
        }
    }


    /// <summary>
    /// Base class for windows attached to a single progress chart in a operation status dialog window.
    /// Any new game needs to derive from this class and implement GameInterface.
    /// A game can also implement "static void FillOptions(System.Windows.Forms.ToolStripMenuItem optionsMenu)" to add options to the tray icon.
    /// </summary>
    public abstract class OverlayWindow : Window, GameInterface
    {
        enum State
        {
            Sleeping,
            LookingForChart,
            Playing
        }

        private AutomationElement _trackedChartView = null;
        private IntPtr _parentOperationStatusWindow = IntPtr.Zero;

        private static OverlayWindow _activeOverlayWindow = null;

        private PropertyCondition _progressChartCondition = new PropertyCondition(AutomationElement.ClassNameProperty, "RateChartOverlayWindow", PropertyConditionFlags.None);
        private AutomationElement _progressChart = null;

        private State _state = State.Sleeping;
        private System.Windows.Rect _rect = new Rect();

        System.Drawing.Color _terrainColor = System.Drawing.Color.Empty;
        DrawingGroup _backingStore = new DrawingGroup();

        private OverlayStats _stats = new OverlayStats();
        private Stopwatch _fpsStopWatch = null;
        private long _frames = 0;

        public OverlayWindow()
        {
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            // Alpha needs to be at least 1 or the window input will click through to the underlying window.
            Background = new SolidColorBrush(new System.Windows.Media.Color() { R = 0, G = 0, B = 0, A = 1 });
            Topmost = true;
            ShowInTaskbar = false;
            //RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

            Width = 200;
            Height = 100;
            Left = 100;
            Top = 100;

            _stats.fps = 30.0f;
        }

        #region GameInterface

        public abstract void Init(System.Windows.Size worldSize);
        public abstract void DeInit();
        public abstract void HeightFieldUpdated(float[] heightField, System.Drawing.Color terrainColor);
        public abstract void Update(DrawingGroup backingStore, OverlayStats stats);

        #endregion GameInterface

        #region Private functions

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                TrackChartView();

            }
            catch (System.Windows.Automation.ElementNotAvailableException)
            {
                Sleep();
            }
            _stats.frameTimer = sw.ElapsedMilliseconds;
        }

        public void Wake(AutomationElement trackedCharView, IntPtr parentOperationStatusWindow)
        {
            if (_state != State.Sleeping)
                return;

            _fpsStopWatch = Stopwatch.StartNew();
            _state = State.LookingForChart;
            this._trackedChartView = trackedCharView;
            this._parentOperationStatusWindow = parentOperationStatusWindow;

            var hwnd = new IntPtr(trackedCharView.Current.NativeWindowHandle);
            uint dpi = NativeInterop.GetDpiForWindow(hwnd);
            // BoundingRectangle is in physical device coordinates (pixels), we need to convert to logical units first.
            _rect = _trackedChartView.Current.BoundingRectangle;
            _rect = new Rect(0, 0, Math.Ceiling(_rect.Width / dpi * 96), Math.Ceiling(_rect.Height / dpi * 96));
            // This should be equivalent to these device independent constants but not sure if that is true for all OS versions.
            // _rect = new Rect(0, 0, 395.0, 85.0);
            Init(new System.Windows.Size(_rect.Width, _rect.Height));
             
            TrackChartView();
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }


        public void Sleep()
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _state = State.Sleeping;
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_activeOverlayWindow == this)
            {
                _activeOverlayWindow = null;
            }
            DeInit();
            Sleep();
            base.OnClosing(e);
        }

        public void TrackChartView()
        {
            if (_trackedChartView == null)
            {
                Sleep();
                return;
            }

            if (_progressChart == null)
            {
                _progressChart = _trackedChartView.FindFirst(TreeScope.Descendants, _progressChartCondition);
            }
            // The progress chart can be minimized by clicking 'fewer details'. The progress chart itself shouldn't be used for
            // anything else as any queries on it glitches out windows automation and pure win32 API calls.
            if (_progressChart != null && NativeInterop.IsWindowVisible(new IntPtr(_progressChart.Current.NativeWindowHandle)))
            {
                _state = State.Playing;

                Show();

                bool showOverlay = false;
                try
                {
                    IntPtr hwndFocus = NativeInterop.GetForegroundWindow();
                    IntPtr hwndCurrent = new IntPtr((int)_trackedChartView.Current.NativeWindowHandle);
                    if (IsActive && _activeOverlayWindow != this)
                    {
                        _activeOverlayWindow = this;
                        System.Diagnostics.Debug.WriteLine($"Active: {hwndCurrent}, parent OW: {_parentOperationStatusWindow}");
                    }
                    else if (!IsActive && _activeOverlayWindow == this)
                    {
                        _activeOverlayWindow = null;
                        System.Diagnostics.Debug.WriteLine($"Not Active: {hwndCurrent}, parent OW: {_parentOperationStatusWindow}");
                    }
                    if (_activeOverlayWindow != null && _activeOverlayWindow._parentOperationStatusWindow == _parentOperationStatusWindow)
                    {
                        showOverlay = true;
                    }

                    while (hwndCurrent != IntPtr.Zero)
                    {
                        if (hwndCurrent == hwndFocus)
                        {
                            showOverlay = true;
                            break;
                        }
                        hwndCurrent = NativeInterop.GetParent(hwndCurrent);
                    }

                    showOverlay = showOverlay || IsActive;
                }
                catch (Exception)
                {
                }

                {
                    // BoundingRectangle is in physical device coordinates (pixel) units. Convert to logical.
                    var rect = _trackedChartView.Current.BoundingRectangle;
                    var pos = this.PointFromScreen(new System.Windows.Point(rect.Left, rect.Top));
                    var pos2 = this.PointFromScreen(new System.Windows.Point(rect.Right, rect.Bottom));
                    // Convert to absolute coordinates by adding the current pos to them.
                    pos.X += Left;
                    pos.Y += Top;
                    pos2.X += Left;
                    pos2.Y += Top;

                    Width = pos2.X - pos.X;
                    Height = pos2.Y - pos.Y;
                    Left = pos.X;
                    // Hacky way of moving the overlay out of the way.
                    Top = showOverlay ? pos.Y : 20000 ;
                }

                {
                    Stopwatch sw = Stopwatch.StartNew();
                    UpdateHeightfield();
                    _stats.captureTime = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    Update(_backingStore, _stats);
                    _stats.updateTime = sw.Elapsed.TotalMilliseconds;

                    _frames++;
                    if (_fpsStopWatch.Elapsed.TotalMilliseconds > 1000)
                    {
                        _stats.fps = (double)_frames / ((double)_fpsStopWatch.ElapsedMilliseconds / 1000.0);
                        _stats.fps = Math.Max(_stats.fps, 30); // Do not fall below 30fps for sim stability, will cause slowdown instead
                        _fpsStopWatch.Restart();
                        _frames = 0;
                    }
                }
            }
            else
            {
                _state = State.LookingForChart;
                Hide();
            }
        }
        protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.DrawDrawing(_backingStore);
        }

        private void UpdateHeightfield()
        {
            if (_rect.Width == 0)
                return;

            _terrainColor = System.Drawing.Color.Empty;
            var value = _trackedChartView.GetCurrentPropertyValue(AutomationElement.NativeWindowHandleProperty);
            IntPtr nativeHandle = new IntPtr((int)value);
            Bitmap bmp = WindowCapture.Capture(nativeHandle);
            if (bmp != null)
            {
                BitmapData pixelData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                float[] heightField = new float[(int)_rect.Width];
                //try
                //{
                //    bmp.Save("D:\\aaa.png");
                //}
                //catch (Exception)
                //{
                //}         

                Dictionary<System.Drawing.Color, int> accu = new Dictionary<System.Drawing.Color, int>();
                accu[System.Drawing.Color.Empty] = 0;
                unsafe
                {
                    byte* pixelPtr = (byte*)pixelData.Scan0;
                    for (int x = 2; x < 6; x++)
                    {
                        for (int y = 0; y < pixelData.Height; ++y)
                        {
                            byte* pixel = pixelPtr + (x * 4 + y * pixelData.Stride);
                            byte b = pixel[0];
                            byte g = pixel[1];
                            byte r = pixel[2];
                            if (r == g && r == b)
                                continue;

                            System.Drawing.Color color = System.Drawing.Color.FromArgb(r, g, b);
                            float sat = color.GetSaturation();
                            if (sat > 0.8)
                            {
                                if (!accu.ContainsKey(color))
                                    accu[color] = 0;
                                accu[color]++;
                            }
                        }
                    }
                    _terrainColor = accu.Aggregate((l, r) => l.Value > r.Value ? l : r).Key;
                    if (_terrainColor != System.Drawing.Color.Empty)
                    {
                        Parallel.For(0, (int)_rect.Width, x =>
                        {
                            heightField[x] = 0;
                            int pixelPosX = (int)Math.Round(((double)x / _rect.Width) * pixelData.Width);
                            for (int y = pixelData.Height - 1; y >= 0; --y)
                            {
                                byte* pixel = pixelPtr + (pixelPosX * 4 + y * pixelData.Stride);
                                byte b = pixel[0];
                                byte g = pixel[1];
                                byte r = pixel[2];

                                System.Drawing.Color color = System.Drawing.Color.FromArgb(r, g, b);
                                if (color == System.Drawing.Color.FromArgb(0, 0, 0))
                                    continue;
                                if (_terrainColor != color)
                                {
                                    heightField[x] = (float)(pixelData.Height - y - 1) / pixelData.Height;
                                    break;
                                }
                            }
                        });
                    }
                }
                bmp.UnlockBits(pixelData);

                if (_terrainColor != System.Drawing.Color.Empty)
                {
                    HeightFieldUpdated(heightField, _terrainColor);
                }

            }
        }
        #endregion Private functions

    }
}
