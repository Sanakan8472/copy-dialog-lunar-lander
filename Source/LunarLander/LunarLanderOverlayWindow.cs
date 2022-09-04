using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CopyDialogLunarLander
{
    public class LunarLanderOverlayWindow : OverlayWindow
    {
        private LunarSim _sim = new LunarSim();
        private bool _left = false;
        private bool _right = false;
        private bool _down = false;
        private bool _debug = false;

        private static System.Windows.Forms.ToolStripMenuItem _menuEasy;
        private static System.Windows.Forms.ToolStripMenuItem _menuHard;

        public LunarLanderOverlayWindow()
        {
        }

        #region FillOptions
        static void FillOptions(System.Windows.Forms.ToolStripMenuItem optionsMenu)
        {
            if (_menuEasy == null)
            {
                _menuEasy = (System.Windows.Forms.ToolStripMenuItem)optionsMenu.DropDownItems.Add("Easy");
                _menuEasy.Click += EasySelected;
            }
            else 
            {
                optionsMenu.DropDownItems.Add(_menuEasy);
            }
            if (_menuHard == null)
            {
                _menuHard = (System.Windows.Forms.ToolStripMenuItem)optionsMenu.DropDownItems.Add("Hard");
                _menuHard.Click += HardSelected;
                _menuHard.Checked = true;
            }
            else
            {
                optionsMenu.DropDownItems.Add(_menuHard);
            }
        }


        private static void EasySelected(object sender, EventArgs e)
        {
            LunarSim.k_maxSpeed = 10;
            _menuEasy.Checked = true;
            _menuHard.Checked = false;
        }
        private static void HardSelected(object sender, EventArgs e)
        {
            LunarSim.k_maxSpeed = 5;
            _menuEasy.Checked = false;
            _menuHard.Checked = true;
        }

        #endregion FillOptions

        #region GameInterface

        public override void Init(System.Windows.Size worldSize)
        {
            _sim.Init(worldSize);
        }

        public override void DeInit()
        {
            _sim.Dispose();
        }

        public override void HeightFieldUpdated(float[] heightField, System.Drawing.Color terrainColor)
        {
            _sim.SetHeightField(heightField, terrainColor);
        }

        public override void Update(DrawingGroup _backingStore, OverlayStats stats)
        {
            _sim.SetActive(this.IsActive);
            _sim.Input(_left, _right, _down);

            var drawingContext = _backingStore.Open();

            // The drawingContext coordinate system has 0,0 in the upper left corner.
            // However, for box2d it's the lower left corner so we flip the coordinate space here.
            drawingContext.PushTransform(new ScaleTransform(1 / 1, -1 /1));
            drawingContext.PushTransform(new TranslateTransform(0, -Height * 1));
            // This will execute the debug render hook of box2d which we use to render our scene.
            _sim.Step(1.0f / (float)stats.fps, drawingContext);
            drawingContext.Pop();
            drawingContext.Pop();
            // We don't want text to be upside-down so the normal render method is outside the transform block above.
            _sim.Render(drawingContext);

            if (_debug)
            {
                LunarSceneDraw.DrawText(drawingContext, stats.GetStatsString(), new System.Windows.Point(4, Height - 12), 12D, System.Drawing.Color.Black);
            }
            drawingContext.Close();
        }

        #endregion GameInterface

        #region Input

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Down)
                _down = true;
            if (e.Key == Key.Left)
                _left = true;
            if (e.Key == Key.Right)
                _right = true;
            if (e.Key == Key.Space)
                _sim.Reset();
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.Down)
                _down = false;
            if (e.Key == Key.Left)
                _left = false;
            if (e.Key == Key.Right)
                _right = false;
            if (e.Key == Key.Tab)
            {
                _debug = !_debug;
                _sim.SetDebug(_debug);
            }
            base.OnKeyUp(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            // Debug code, adds a box under the cursor
            /*
            PresentationSource MainWindowPresentationSource = PresentationSource.FromVisual(this);
            Matrix m = MainWindowPresentationSource.CompositionTarget.TransformToDevice;

            var scenePos = e.GetPosition(this);
            scenePos.Y = Height - scenePos.Y;
            _sim.AddBox(scenePos);
            */
        }

        #endregion Input

    }
}
