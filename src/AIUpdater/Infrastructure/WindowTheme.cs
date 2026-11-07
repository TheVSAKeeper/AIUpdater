using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIUpdater.Infrastructure;

internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetAttribute(handle, DwmwaUseImmersiveDarkMode, 1);
        SetAttribute(handle, DwmwaCaptionColor, 0x0011140B);
        SetAttribute(handle, DwmwaTextColor, 0x00F1F7ED);
    }

    private static void SetAttribute(nint handle, int attribute, int value)
    {
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
