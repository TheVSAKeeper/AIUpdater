using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIUpdater;

internal static class WindowTheme
{
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetAttribute(handle, 20, 1); // DWMWA_USE_IMMERSIVE_DARK_MODE
        SetAttribute(handle, 35, 0x0011140B); // DWMWA_CAPTION_COLOR, COLORREF
        SetAttribute(handle, 36, 0x00F1F7ED); // DWMWA_TEXT_COLOR
    }

    private static void SetAttribute(nint handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
