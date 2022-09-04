using System;
using System.Drawing;

namespace CopyDialogLunarLander
{
    class WindowCapture
    {
        /// <summary>
        /// Captures the given native window as a bitmap.
        /// </summary>
        /// <param name="sourceWindow"></param>
        /// <returns></returns>
        public static Bitmap Capture(IntPtr sourceWindow)
        {
            Bitmap bitmap = null;
            try
            {
                NativeInterop.RECT area;
                if (!NativeInterop.GetWindowRect(sourceWindow, out area))
                {
                    return bitmap;
                }

                IntPtr screenDC = NativeInterop.GetDCEx((sourceWindow), IntPtr.Zero, NativeInterop.DeviceContextValues.Validate);
                IntPtr memDC = NativeInterop.CreateCompatibleDC(screenDC);
                IntPtr hBitmap = NativeInterop.CreateCompatibleBitmap(screenDC, (int)area.Width, (int)area.Height);
                NativeInterop.SelectObject(memDC, hBitmap); // Select bitmap from compatible bitmap to memDC

                NativeInterop.BitBlt(memDC, 0, 0, (int)area.Width, (int)area.Height, screenDC, 0, 0, NativeInterop.TernaryRasterOperations.SRCCOPY);
                bitmap = Image.FromHbitmap(hBitmap);

                NativeInterop.DeleteObject(hBitmap);
                NativeInterop.DeleteDC(memDC);
                NativeInterop.ReleaseDC(IntPtr.Zero, screenDC);
            }
            catch (Exception)
            {

            }
            return bitmap;
        }

    }
}
