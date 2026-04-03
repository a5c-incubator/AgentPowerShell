using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AgentPowerShell.Core;
using AgentPowerShell.Protos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPowerShell.Daemon;

public sealed class ShimIpcHostedService(
    ShimCommandProcessor processor,
    ILogger<ShimIpcHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await RunNamedPipeAsync(stoppingToken).ConfigureAwait(false);
            return;
        }

        await RunUnixSocketAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task RunNamedPipeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                ShimIpcEndpoint.ResolvePipeName(),
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            await HandleConnectionAsync(server, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunUnixSocketAsync(CancellationToken cancellationToken)
    {
        var socketPath = ShimIpcEndpoint.ResolveSocketPath();
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            using var stream = new NetworkStream(client, ownsSocket: false);
            await HandleConnectionAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            var payload = await ReadPayloadAsync(stream, cancellationToken).ConfigureAwait(false);
            var request = JsonSerializer.Deserialize<ShimCommandRequest>(payload, JsonOptions) ?? new ShimCommandRequest();
            var response = await processor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Shim IPC request failed.");
        }
    }

    private static async Task<string> ReadPayloadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var terminatorIndex = Array.IndexOf(bytes, (byte)0x04, 0, read);
            if (terminatorIndex >= 0)
            {
                buffer.Write(bytes, 0, terminatorIndex);
                break;
            }

            buffer.Write(bytes, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
