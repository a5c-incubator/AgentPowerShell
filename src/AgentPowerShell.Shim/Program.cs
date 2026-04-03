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
catch (Exception firstFailure)
{
    _ = DaemonLauncher.TryStart(request.WorkingDirectory);
    response = await RetryUntilAvailableAsync(client, request, firstFailure, CancellationToken.None).ConfigureAwait(false);
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

static async Task<ShimCommandResponse> RetryUntilAvailableAsync(
    ShimClient client,
    ShimCommandRequest request,
    Exception firstFailure,
    CancellationToken cancellationToken)
{
    var lastFailure = firstFailure;
    for (var attempt = 0; attempt < 10; attempt++)
    {
        try
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            return await client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception retryFailure)
        {
            lastFailure = retryFailure;
        }
    }

    Console.Error.WriteLine($"AgentPowerShell daemon is unavailable for shim execution. {lastFailure.Message}");
    return new ShimCommandResponse
    {
        SessionId = request.SessionId,
        ExitCode = 70,
        PolicyDecision = "error",
        DenialReason = "Daemon unavailable.",
        Stderr = $"AgentPowerShell daemon is unavailable for shim execution. {lastFailure.Message}"
    };
}
