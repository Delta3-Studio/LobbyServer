using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace LobbyServer;

public partial class UdpListenerService(
    LobbyRepository repository,
    TimeProvider time,
    IOptions<AppSettings> settings,
    ILogger<UdpListenerService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = settings.Value.UdpPort;
        ReadOnlyMemory<byte> ackMsg = "ack"u8.ToArray();
        logger.LogInformation("UDP BINDING HOST: {Host}", settings.Value.UdpHost);

        var hostAddresses =
            string.IsNullOrWhiteSpace(settings.Value.UdpHost)
                ? []
                : await Dns.GetHostAddressesAsync(
                    settings.Value.UdpHost, AddressFamily.InterNetwork, stoppingToken);

        var bindAddress = hostAddresses.FirstOrDefault(IPAddress.Any);
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Blocking = false;

        IPEndPoint bindEndpoint = new(bindAddress, port);
        logger.LogInformation("UDP: Starting socket at {Endpoint}", bindEndpoint);
        socket.Bind(bindEndpoint);
        IPEndPoint anyEndPoint = new(IPAddress.Any, 0);

        var buffer = GC.AllocateArray<byte>(Unsafe.SizeOf<Guid>(), pinned: true);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var received = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint, stoppingToken)
                    .ConfigureAwait(false);

                if (received is not { ReceivedBytes: var receivedSize, RemoteEndPoint: IPEndPoint remoteEndPoint }
                    || receivedSize is 0)
                    continue;

                LogReceived(logger, receivedSize, remoteEndPoint);

                Guid peerToken = new(buffer.AsSpan(0, receivedSize), true);
                if (peerToken == Guid.Empty) continue;

                if (repository.FindEntry(peerToken) is not { } entry) continue;
                LogPlayerFound(logger, entry.Peer.Username, entry.Peer.Endpoint);

                await socket.SendToAsync(ackMsg, SocketFlags.None, remoteEndPoint, stoppingToken);

                if (entry.Peer.Endpoint is not null && !entry.Peer.Endpoint.Equals(remoteEndPoint))
                    LogPlayerAddressChanged(logger, entry.Peer.Username, entry.Peer.Endpoint, remoteEndPoint);

                entry.Peer.Endpoint = remoteEndPoint;
                entry.LastRead = time.GetUtcNow();
            }
            catch (OperationCanceledException)
            {
#pragma warning disable S6667
                logger.LogInformation("UDP: Operation cancelled");
#pragma warning restore S6667
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UDP: socket error");
            }
        }

        logger.LogInformation("UDP: stopping");
    }

    [LoggerMessage(LogLevel.Debug, "UDP: Found player '{name}' for address {endpoint}")]
    static partial void LogPlayerFound(ILogger<UdpListenerService> logger, string name, IPEndPoint? endpoint);

    [LoggerMessage(LogLevel.Debug, "UDP: Received {size} bytes from {endpoint}")]
    static partial void LogReceived(ILogger<UdpListenerService> logger, int size, IPEndPoint endpoint);

    [LoggerMessage(LogLevel.Information, "UDP: Player '{name}' changed address from {oldEndpoint} to {newEndpoint}")]
    static partial void LogPlayerAddressChanged(ILogger<UdpListenerService> logger,
        string name, IPEndPoint oldEndpoint, IPEndPoint newEndpoint);
}
