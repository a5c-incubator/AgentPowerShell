using System.Runtime.InteropServices;

namespace AgentPowerShell.Platform.Linux;

internal static class LinuxNativeMethods
{
    [DllImport("libc", SetLastError = true)]
    internal static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    [DllImport("libc", SetLastError = true)]
    internal static extern int syscall(nint number, nint arg1, nint arg2, nint arg3, nint arg4, nint arg5, nint arg6);

    [DllImport("libc", SetLastError = true)]
    internal static extern int setrlimit(int resource, in RLimit limit);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RLimit
{
    public readonly nuint Current;
    public readonly nuint Maximum;

    public RLimit(nuint current, nuint maximum)
    {
        Current = current;
        Maximum = maximum;
    }
}
