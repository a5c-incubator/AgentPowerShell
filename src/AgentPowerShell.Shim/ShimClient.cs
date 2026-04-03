using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Shim;

public sealed class ShimClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShimCommandResponse> ExecuteAsync(ShimCommandRequest request, CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await using var pipe = new NamedPipeClientStream(".", ShimIpcEndpoint.ResolvePipeName(), PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
            return await SendAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        }

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(ShimIpcEndpoint.ResolveSocketPath()), cancellationToken).ConfigureAwait(false);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        return await SendAsync(stream, request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ShimCommandResponse> SendAsync(Stream stream, ShimCommandRequest request, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await writer.WriteAsync(JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        stream.WriteByte(0x04);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            buffer.Write(bytes, 0, read);
        }

        var json = Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\0', '\u0004');
        return JsonSerializer.Deserialize<ShimCommandResponse>(json, JsonOptions) ?? new ShimCommandResponse();
    }
}
