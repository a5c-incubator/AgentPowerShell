using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentPowerShell.Platform.Windows;

public sealed class WindowsJobObject : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private nint _handle;

    private WindowsJobObject(nint handle)
    {
        _handle = handle;
    }

    public static WindowsJobObject? TryCreate(string? name = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = WindowsNativeMethods.CreateJobObject(0, name);
        if (handle == 0)
        {
            return null;
        }

        var job = new WindowsJobObject(handle);
        if (!job.TryEnableKillOnClose())
        {
            job.Dispose();
            return null;
        }

        return job;
    }

    public void Assign(Process process)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);

        if (!WindowsNativeMethods.AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to assign process to Windows Job Object.");
        }
    }

    private bool TryEnableKillOnClose()
    {
        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);
            return WindowsNativeMethods.SetInformationJobObject(
                _handle,
                JobObjectInfoType.ExtendedLimitInformation,
                buffer,
                (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        _ = WindowsNativeMethods.CloseHandle(_handle);
        _handle = 0;
    }
}
