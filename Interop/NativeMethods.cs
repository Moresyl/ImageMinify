using System.Runtime.InteropServices;

namespace ImageMinify.Interop;

/// <summary>
/// The Win32 surface the custom window frame needs. Nothing here is called from anywhere except
/// <see cref="WindowFrame"/> and the main window's chrome handling, which is why it is all internal.
/// </summary>
internal static class NativeMethods
{
    // Desktop Window Manager attributes. The numbers are from dwmapi.h; they are not in any
    // .NET enum, so they are spelled out with the header's own names.
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>Round the window's corners — DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND.</summary>
    internal const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Asked before a window is maximized, so the window can say how large "maximized" is.
    /// </summary>
    /// <remarks>
    /// A <c>WindowChrome</c> window answers WM_NCCALCSIZE by claiming the whole window rectangle as
    /// client area. Windows still sizes a maximized window to the monitor rectangle inflated by the
    /// frame it thinks is there, so the content ends up hanging a few pixels past every edge of the
    /// screen and under the taskbar. Answering this message with the monitor's work area is what
    /// makes maximize land exactly where the user can see it.
    /// </remarks>
    internal const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>MONITOR_DEFAULTTONEAREST — never fails, even for an off-screen window.</summary>
    internal const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        internal POINT Reserved;
        internal POINT MaxSize;
        internal POINT MaxPosition;
        internal POINT MinTrackSize;
        internal POINT MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        internal int Size;
        internal RECT Monitor;
        internal RECT WorkArea;
        internal uint Flags;
    }

    [DllImport("user32.dll", SetLastError = false)]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
