using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ImageMinify.Interop;

/// <summary>
/// Applies the Windows 11 frame — rounded corners and a title bar that follows the application's
/// theme — to a window whose caption is drawn in XAML.
/// </summary>
/// <remarks>
/// <para>
/// All of this is best-effort. Every call is a DWM attribute set that returns a failure HRESULT on a
/// build that does not know the attribute, and a Windows 10 machine simply ends up with a square
/// window instead of a rounded one. Nothing here is allowed to throw: a missing rounded corner is a
/// cosmetic difference, not a reason for the application to fail to start.
/// </para>
/// <para>
/// There is deliberately no Mica backdrop here. Showing one means giving up the window's own opaque
/// ground so the material can be seen through it, and DWM gives no reliable way to find out whether
/// it is going to draw that material: it answers S_OK to <c>DWMWA_SYSTEMBACKDROP_TYPE</c> whenever
/// it merely recognises the attribute, then silently declines under conditions the application
/// cannot enumerate. When it declines, the window is transparent over nothing at all — the desktop
/// does not show through either, because the material was what was supposed to be there — so the
/// whole window composites against black and every translucent surface in the palette turns grey.
/// That shipped once and reads as "the light theme is broken". A window that is always painted is
/// worth more than a material that is sometimes there.
/// </para>
/// </remarks>
internal static class WindowFrame
{
    /// <summary>
    /// Applies the frame to <paramref name="window"/>. Safe to call before the window is shown —
    /// the work is deferred to <see cref="Window.SourceInitialized"/> if the handle does not exist
    /// yet, because a DWM attribute cannot be set on a window that has not been created.
    /// </summary>
    internal static void Apply(Window window, bool isDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        ApplyCore(handle, isDark);

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyCore(new WindowInteropHelper(window).Handle, isDark);
        }
    }

    /// <summary>
    /// Answers WM_GETMINMAXINFO with the work area of the monitor the window is on, so maximizing
    /// fills the screen exactly instead of spilling past its edges and under the taskbar.
    /// </summary>
    /// <remarks>
    /// Hook this from the window's <c>HwndSourceHook</c>. The monitor is resolved per message rather
    /// than cached: the answer changes when the window is dragged to another display, when a
    /// display's resolution changes, and when the taskbar is moved or auto-hidden.
    /// </remarks>
    internal static IntPtr HandleMaximizeExtents(IntPtr hwnd, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_GETMINMAXINFO)
        {
            return IntPtr.Zero;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var extents = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);

        // Both are in the monitor's own coordinates, which is why the position is a difference
        // rather than the work area's absolute origin: on a secondary display to the left of the
        // primary one, that origin is negative and the window would be placed off-screen.
        extents.MaxPosition.X = info.WorkArea.Left - info.Monitor.Left;
        extents.MaxPosition.Y = info.WorkArea.Top - info.Monitor.Top;
        extents.MaxSize.X = info.WorkArea.Right - info.WorkArea.Left;
        extents.MaxSize.Y = info.WorkArea.Bottom - info.WorkArea.Top;

        Marshal.StructureToPtr(extents, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }

    private static void ApplyCore(IntPtr handle, bool isDark)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // The caption buttons and the resize border are drawn by DWM rather than by the application,
        // so this is the only way the palette reaches them.
        SetAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, isDark ? 1 : 0);
        SetAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, NativeMethods.DWMWCP_ROUND);
    }

    private static void SetAttribute(IntPtr handle, int attribute, int value)
    {
        try
        {
            NativeMethods.DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll is absent. There is no compositor to ask, so there is nothing to do.
        }
        catch (EntryPointNotFoundException)
        {
            // Ancient dwmapi.dll without the attribute setter. Same conclusion.
        }
    }
}
