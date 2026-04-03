using System.Runtime.InteropServices;

namespace AgentPowerShell.Platform.MacOS;

internal static class MacOsNativeMethods
{
    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    internal static extern int setrlimit(int resource, in MacOsRLimit limit);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MacOsRLimit
{
    public readonly nuint Current;
    public readonly nuint Maximum;

    public MacOsRLimit(nuint current, nuint maximum)
    {
        Current = current;
        Maximum = maximum;
    }
}
