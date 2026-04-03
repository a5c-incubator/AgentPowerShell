using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AgentPowerShell.Core;
using AgentPowerShell.Events;

namespace AgentPowerShell.Daemon;

public sealed class ApprovalManager(
    AgentPowerShellConfig config,
    ApprovalSecretStore secretStore,
    IEventSink eventSink,
    HttpClient httpClient) : IApprovalHandler
{
    public Task<string> GetOrCreateTotpSecretAsync(string sessionId, CancellationToken cancellationToken) =>
        secretStore.GetOrCreateTotpSecretAsync(sessionId, cancellationToken);

    public async Task<string> CreateWebAuthnAssertionAsync(string sessionId, string challenge, CancellationToken cancellationToken)
    {
        var secret = await secretStore.GetOrCreateWebAuthnSecretAsync(sessionId, cancellationToken).ConfigureAwait(false);
        using var hmac = new HMACSHA256(Convert.FromBase64String(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(challenge));
        return Convert.ToBase64String(hash);
    }

    public async Task<ApprovalResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sessionId = request.Metadata?.TryGetValue("sessionId", out var session) == true ? session : "system";
        var timeout = request.Timeout > TimeSpan.Zero
            ? request.Timeout
            : TimeSpan.FromSeconds(config.Approval.TimeoutSeconds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        ApprovalResponse? lastResponse = null;
        foreach (var mode in ResolveModes())
        {
            try
            {
                var response = mode switch
                {
                    "dialog" => await TryDialogAsync(request, timeoutCts.Token).ConfigureAwait(false),
                    "tty" => await TryTtyAsync(request, timeoutCts.Token).ConfigureAwait(false),
                    "totp" => await TryTotpAsync(request, sessionId, timeoutCts.Token).ConfigureAwait(false),
                    "webauthn" => await TryWebAuthnAsync(request, sessionId, timeoutCts.Token).ConfigureAwait(false),
                    "rest" => await TryRestAsync(request, sessionId, timeoutCts.Token).ConfigureAwait(false),
                    _ => null
                };

                if (response is null)
                {
                    continue;
                }

                lastResponse = response;
                await PublishAsync(sessionId, mode, response, startedAt, cancellationToken).ConfigureAwait(false);
                if (response.Approved)
                {
                    return response;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        var denied = lastResponse ?? new ApprovalResponse(false, "timeout", "Approval timed out or no approval backend was available.");
        await PublishAsync(sessionId, "escalation", denied, startedAt, cancellationToken).ConfigureAwait(false);
        return denied;
    }

    private IEnumerable<string> ResolveModes() =>
        config.Approval.Mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(mode => mode.ToLowerInvariant());

    private static Task<ApprovalResponse?> TryTtyAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Metadata?.TryGetValue("approvalInput", out var input) == true)
        {
            var approved = IsApproved(input);
            return Task.FromResult<ApprovalResponse?>(new ApprovalResponse(approved, "tty", approved ? "Approved via terminal input." : "Denied via terminal input."));
        }

        if (Console.IsInputRedirected)
        {
            return Task.FromResult<ApprovalResponse?>(null);
        }

        Console.WriteLine($"APPROVAL REQUIRED: {request.Reason}");
        Console.Write("Approve? [y/N]: ");
        var answer = Console.ReadLine();
        var allowed = IsApproved(answer);
        return Task.FromResult<ApprovalResponse?>(new ApprovalResponse(allowed, "tty", allowed ? "Approved via terminal prompt." : "Denied via terminal prompt."));
    }

    private static Task<ApprovalResponse?> TryDialogAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Metadata?.TryGetValue("dialogApproved", out var simulated) == true)
        {
            var approved = IsApproved(simulated);
            return Task.FromResult<ApprovalResponse?>(new ApprovalResponse(approved, "dialog", approved ? "Approved via simulated dialog." : "Denied via simulated dialog."));
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<ApprovalResponse?>(null);
        }

        var result = MessageBoxW(IntPtr.Zero, request.Reason, "AgentPowerShell Approval", 0x00000004 | 0x00000020);
        return Task.FromResult<ApprovalResponse?>(result switch
        {
            6 => new ApprovalResponse(true, "dialog", "Approved via Windows dialog."),
            7 => new ApprovalResponse(false, "dialog", "Denied via Windows dialog."),
            _ => null
        });
    }

    private async Task<ApprovalResponse?> TryTotpAsync(ApprovalRequest request, string sessionId, CancellationToken cancellationToken)
    {
        if (request.Metadata?.TryGetValue("totpCode", out var code) != true)
        {
            return null;
        }

        var secret = await secretStore.GetOrCreateTotpSecretAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var approved = ApprovalTotp.ValidateCode(code, secret);
        return new ApprovalResponse(approved, "totp", approved ? "Approved via TOTP." : "Invalid TOTP code.");
    }

    private async Task<ApprovalResponse?> TryWebAuthnAsync(ApprovalRequest request, string sessionId, CancellationToken cancellationToken)
    {
        if (request.Metadata?.TryGetValue("webauthnAssertion", out var assertion) != true)
        {
            return null;
        }

        var expected = await CreateWebAuthnAssertionAsync(sessionId, request.Reason, cancellationToken).ConfigureAwait(false);
        var approved = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(assertion ?? string.Empty),
            Encoding.UTF8.GetBytes(expected));

        return new ApprovalResponse(approved, "webauthn", approved ? "Approved via WebAuthn assertion." : "Invalid WebAuthn assertion.");
    }

    private async Task<ApprovalResponse?> TryRestAsync(ApprovalRequest request, string sessionId, CancellationToken cancellationToken)
    {
        var endpoint = request.Metadata?.TryGetValue("restApprovalEndpoint", out var overrideEndpoint) == true
            ? overrideEndpoint
            : config.Approval.RestApiEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        using var response = await httpClient.PostAsJsonAsync(
            endpoint,
            new { sessionId, request.Reason, request.Metadata },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RestApprovalResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return new ApprovalResponse(false, "rest", "Remote approval endpoint returned an empty response.");
        }

        return new ApprovalResponse(payload.Approved, payload.Approver ?? "rest", payload.Message);
    }

    private async Task PublishAsync(
        string sessionId,
        string mode,
        ApprovalResponse response,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await eventSink.PublishAsync(
            new AgentPowerShell.Events.ApprovalEvent(
                startedAt,
                sessionId,
                mode,
                response.Approved,
                response.Message ?? string.Empty),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsApproved(string? value) =>
        string.Equals(value, "y", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "approve", StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private sealed record RestApprovalResponse(bool Approved, string? Approver, string? Message);
}
