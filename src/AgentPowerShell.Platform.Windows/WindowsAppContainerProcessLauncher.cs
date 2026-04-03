using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgentPowerShell.Platform.Windows;

public sealed class WindowsAppContainerProcessLauncher
{
    public async Task<WindowsAppContainerProcessResult> ExecuteAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AppContainer launch is only available on Windows.");
        }

        var appContainerName = $"aps-{Guid.NewGuid():N}";
        var appContainerSid = nint.Zero;
        var attributeList = nint.Zero;
        var securityCapabilitiesBuffer = nint.Zero;
        var commandLineBuffer = nint.Zero;
        var environmentBuffer = nint.Zero;
        var stdoutRead = nint.Zero;
        var stdoutWrite = nint.Zero;
        var stderrRead = nint.Zero;
        var stderrWrite = nint.Zero;
        var stdinHandle = nint.Zero;
        var processInfo = default(ProcessInformation);

        try
        {
            var createProfileResult = WindowsNativeMethods.CreateAppContainerProfile(
                appContainerName,
                appContainerName,
                "AgentPowerShell isolated native process",
                0,
                0,
                out appContainerSid);
            if (createProfileResult != 0 || appContainerSid == 0)
            {
                throw new Win32Exception(createProfileResult, "Failed to create AppContainer profile.");
            }

            var pipeAttributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                InheritHandle = 1
            };

            CreateNonInheritableParentPipe(ref pipeAttributes, out stdoutRead, out stdoutWrite);
            CreateNonInheritableParentPipe(ref pipeAttributes, out stderrRead, out stderrWrite);

            stdinHandle = WindowsNativeMethods.CreateFileW(
                "NUL",
                WindowsNativeMethods.GenericRead,
                WindowsNativeMethods.FileShareRead | WindowsNativeMethods.FileShareWrite,
                ref pipeAttributes,
                WindowsNativeMethods.OpenExisting,
                0,
                0);
            if (stdinHandle == 0 || stdinHandle == -1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open NUL for AppContainer stdin.");
            }

            attributeList = AllocateAttributeList();

            var securityCapabilities = new SecurityCapabilities
            {
                AppContainerSid = appContainerSid
            };
            securityCapabilitiesBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
            Marshal.StructureToPtr(securityCapabilities, securityCapabilitiesBuffer, fDeleteOld: false);
            if (!WindowsNativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    WindowsNativeMethods.ProcThreadAttributeSecurityCapabilities,
                    securityCapabilitiesBuffer,
                    (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                    0,
                    0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set AppContainer security capabilities.");
            }

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Cb = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = WindowsNativeMethods.StartfUseStdHandles,
                    StdInput = stdinHandle,
                    StdOutput = stdoutWrite,
                    StdError = stderrWrite
                },
                AttributeList = attributeList
            };

            var commandLine = BuildCommandLine(executablePath, arguments);
            commandLineBuffer = Marshal.StringToHGlobalUni(commandLine);

            var environmentBlock = BuildEnvironmentBlock(environment);
            environmentBuffer = Marshal.StringToHGlobalUni(environmentBlock);

            var launchDirectory = ResolveLaunchDirectory(executablePath, workingDirectory);

            if (!WindowsNativeMethods.CreateProcessW(
                    executablePath,
                    commandLineBuffer,
                    0,
                    0,
                    inheritHandles: true,
                    WindowsNativeMethods.ExtendedStartupInfoPresent | WindowsNativeMethods.CreateUnicodeEnvironment,
                    environmentBuffer,
                    launchDirectory,
                    ref startupInfo,
                    out processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to start AppContainer process.");
            }

            using var job = WindowsJobObject.TryCreate($"agentpowershell-{sessionId}");
            job?.Assign(processInfo.Process);

            WindowsNativeMethods.CloseHandle(stdoutWrite);
            stdoutWrite = 0;
            WindowsNativeMethods.CloseHandle(stderrWrite);
            stderrWrite = 0;

            using var stdoutStream = CreateReader(stdoutRead);
            stdoutRead = 0;
            using var stderrStream = CreateReader(stderrRead);
            stderrRead = 0;

            var stdoutTask = stdoutStream.ReadToEndAsync(cancellationToken);
            var stderrTask = stderrStream.ReadToEndAsync(cancellationToken);

            await WaitForExitAsync(processInfo.Process, cancellationToken).ConfigureAwait(false);
            if (!WindowsNativeMethods.GetExitCodeProcess(processInfo.Process, out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to query AppContainer process exit code.");
            }

            return new WindowsAppContainerProcessResult(
                unchecked((int)exitCode),
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));
        }
        finally
        {
            if (processInfo.Thread != 0)
            {
                WindowsNativeMethods.CloseHandle(processInfo.Thread);
            }

            if (processInfo.Process != 0)
            {
                WindowsNativeMethods.CloseHandle(processInfo.Process);
            }

            CloseIfOpen(stdinHandle);
            CloseIfOpen(stdoutRead);
            CloseIfOpen(stdoutWrite);
            CloseIfOpen(stderrRead);
            CloseIfOpen(stderrWrite);

            if (attributeList != 0)
            {
                WindowsNativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (securityCapabilitiesBuffer != 0)
            {
                Marshal.FreeHGlobal(securityCapabilitiesBuffer);
            }

            if (commandLineBuffer != 0)
            {
                Marshal.FreeHGlobal(commandLineBuffer);
            }

            if (environmentBuffer != 0)
            {
                Marshal.FreeHGlobal(environmentBuffer);
            }

            if (appContainerSid != 0)
            {
                WindowsNativeMethods.FreeSid(appContainerSid);
            }

            _ = WindowsNativeMethods.DeleteAppContainerProfile(appContainerName);
        }
    }

    private static void CreateNonInheritableParentPipe(ref SecurityAttributes attributes, out nint readPipe, out nint writePipe)
    {
        if (!WindowsNativeMethods.CreatePipe(out readPipe, out writePipe, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create AppContainer stdio pipe.");
        }

        if (!WindowsNativeMethods.SetHandleInformation(readPipe, WindowsNativeMethods.HandleFlagInherit, 0))
        {
            WindowsNativeMethods.CloseHandle(readPipe);
            WindowsNativeMethods.CloseHandle(writePipe);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to clear handle inheritance on AppContainer pipe.");
        }
    }

    private static nint AllocateAttributeList()
    {
        nuint size = 0;
        _ = WindowsNativeMethods.InitializeProcThreadAttributeList(0, 1, 0, ref size);
        var attributeList = Marshal.AllocHGlobal((int)size);
        if (!WindowsNativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        {
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize process attribute list.");
        }

        return attributeList;
    }

    private static StreamReader CreateReader(nint handle)
    {
        var safeHandle = new SafeFileHandle(handle, ownsHandle: true);
        return new StreamReader(new FileStream(safeHandle, FileAccess.Read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    private static Task WaitForExitAsync(nint processHandle, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var waitResult = WindowsNativeMethods.WaitForSingleObject(processHandle, WindowsNativeMethods.Infinite);
            if (waitResult != WindowsNativeMethods.WaitObject0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for AppContainer process failed.");
            }
        }, cancellationToken);

    private static string ResolveLaunchDirectory(string executablePath, string workingDirectory)
    {
        var candidate = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
        {
            return candidate;
        }

        return string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
    }

    private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var pair in environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }

    private static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        var parts = new List<string>(arguments.Count + 1)
        {
            Quote(executablePath)
        };
        parts.AddRange(arguments.Select(Quote));
        return string.Join(' ', parts);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\\'))
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var slashCount = 0;

        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                slashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', slashCount * 2 + 1);
                builder.Append('"');
                slashCount = 0;
                continue;
            }

            if (slashCount > 0)
            {
                builder.Append('\\', slashCount);
                slashCount = 0;
            }

            builder.Append(ch);
        }

        if (slashCount > 0)
        {
            builder.Append('\\', slashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static void CloseIfOpen(nint handle)
    {
        if (handle != 0)
        {
            WindowsNativeMethods.CloseHandle(handle);
        }
    }
}

public sealed record WindowsAppContainerProcessResult(int ExitCode, string Stdout, string Stderr);
