using System.Diagnostics;
using AgentPowerShell.Protos;
using AgentPowerShell.Shim;

if (Environment.GetEnvironmentVariable("AGENTPOWERSHELL_IN_SESSION") == "1")
{
    var realShell = RealPowerShellResolver.Resolve(Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "pwsh"));
    var direct = new ProcessStartInfo
    {
        FileName = realShell,
        UseShellExecute = false
    };
    foreach (var arg in args)
    {
        direct.ArgumentList.Add(arg);
    }

    using var directProcess = Process.Start(direct) ?? throw new InvalidOperationException("Failed to start real PowerShell.");
    await directProcess.WaitForExitAsync().ConfigureAwait(false);
    return directProcess.ExitCode;
}

var invocation = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "pwsh");
var executablePath = RealPowerShellResolver.Resolve(invocation);
var request = new ShimCommandRequest
{
    SessionId = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_SESSION_ID") ?? string.Empty,
    InvocationName = invocation,
    ExecutablePath = executablePath,
    Arguments = [.. args],
    WorkingDirectory = Environment.CurrentDirectory,
    Interactive = args.Length == 0,
    Environment = Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString() ?? string.Empty)
};

var client = new ShimClient();
ShimCommandResponse response;
try
{
    response = await client.ExecuteAsync(request, CancellationToken.None).ConfigureAwait(false);
}
catch
{
    DaemonLauncher.TryStart();
    await Task.Delay(1000).ConfigureAwait(false);
    response = await client.ExecuteAsync(request, CancellationToken.None).ConfigureAwait(false);
}

if (!string.IsNullOrEmpty(response.Stdout))
{
    Console.Out.Write(response.Stdout);
}

if (!string.IsNullOrEmpty(response.Stderr))
{
    Console.Error.Write(response.Stderr);
}

return response.ExitCode;
