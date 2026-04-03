namespace AgentPowerShell.Core;

public interface IShellInterceptor
{
    Task<InterceptedShellResult> ExecuteAsync(InterceptedShellRequest request, CancellationToken cancellationToken);
}

public interface IPlatformEnforcer
{
    string PlatformId { get; }
    Task ApplyPolicyAsync(ExecutionPolicy policy, CancellationToken cancellationToken);
}

public interface IApprovalHandler
{
    Task<ApprovalResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken);
}

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken);
    Task<TokenPair> IssueTokenPairAsync(string subject, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);
    Task<TokenPair> RefreshTokenPairAsync(string refreshToken, CancellationToken cancellationToken);
}

public interface INetworkMonitor
{
    Task<NetworkInspectionResult> InspectConnectionAsync(NetworkConnectionObservation observation, CancellationToken cancellationToken);
    Task<DnsInspectionResult> InspectDnsQueryAsync(DnsQueryObservation observation, CancellationToken cancellationToken);
}

public interface IFilesystemMonitor
{
    Task<FileInspectionResult> RecordAccessAsync(FileAccessObservation observation, CancellationToken cancellationToken);
    Task<FileInspectionResult> RequestDeleteAsync(FileDeleteRequest request, CancellationToken cancellationToken);
    Task<QuarantinedFile> RestoreAsync(string sessionId, string quarantineId, CancellationToken cancellationToken);
}

public sealed record InterceptedShellRequest(string CommandLine, string WorkingDirectory, IReadOnlyDictionary<string, string> Environment);

public sealed record InterceptedShellResult(int ExitCode, string Stdout, string Stderr);

public sealed record ApprovalRequest(string Reason, TimeSpan Timeout, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ApprovalResponse(bool Approved, string? Approver = null, string? Message = null);

public sealed record AuthenticationRequest(string? ApiKey = null, string? BearerToken = null);

public sealed record AuthenticationResult(bool Authenticated, string Mode, string? Subject, string? Role, string? Message = null);

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record NetworkConnectionObservation(string SessionId, string Destination, int Port, string Protocol);

public sealed record DnsQueryObservation(string SessionId, string Query, IReadOnlyList<string>? Answers = null);

public sealed record NetworkInspectionResult(EvaluationResult Evaluation, string Destination, int Port, string Protocol);

public sealed record DnsInspectionResult(EvaluationResult Evaluation, string Query, IReadOnlyList<string> Answers);

public sealed record FileAccessObservation(string SessionId, string Path, string Operation);

public sealed record FileDeleteRequest(string SessionId, string Path);

public sealed record FileInspectionResult(EvaluationResult Evaluation, string Path, string Operation, QuarantinedFile? Quarantine = null);

public sealed record QuarantinedFile(
    string Id,
    string SessionId,
    string OriginalPath,
    string QuarantinePath,
    bool IsDirectory,
    DateTimeOffset QuarantinedAt);
