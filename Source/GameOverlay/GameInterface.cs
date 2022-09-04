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
    /// Any new game needs to derive from OverlayWindow and implement this GameInterface.
    /// </summary>
    interface GameInterface
    {
        /// <summary>
        /// Called once to define the virtual game space. This is usually 395x85.
        /// </summary>
        /// <param name="worldSize">Logical size of the world. This is independent of DPI scaling and does not neccessarily match pixels.</param>
        void Init(System.Windows.Size worldSize);

        /// <summary>
        /// Called before the window is destroyed. Use this for any game related cleanup.
        /// </summary>
        void DeInit();

        /// <summary>
        /// Each frame a height field is extracted from the progress chart.
        /// </summary>
        /// <param name="heightField">Element count is worldSize.width and values are between 0 (bottom) and 1 (top). Due to DPI scaling the values can be between units of the virtual game space.</param>
        /// <param name="terrainColor">The color of the copy progress bar</param>
        void HeightFieldUpdated(float[] heightField, System.Drawing.Color terrainColor);

        /// <summary>
        /// Called at 60hz. This is the main game loop function.
        /// </summary>
        /// <param name="backingStore">Render any content to this image</param>
        /// <param name="stats">Use this to determine time stepping of the simulation and profiling</param>
        void Update(DrawingGroup backingStore, OverlayStats stats);
    }
}
