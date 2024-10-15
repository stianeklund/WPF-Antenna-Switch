using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AntennaSwitchWPF;

public class UdpMessageSender : IUdpMessageSender, IDisposable
{
    private readonly string _ipAddress;
    private readonly int _port;
    private readonly UdpClient _client;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private const int MaxRetries = 3;
    private const int InitialBackoffMs = 100;
    private const int TimeoutMs = 1000;

    public UdpMessageSender(string ipAddress, int port)
    {
        _ipAddress = ipAddress;
        _port = port;
        _client = new UdpClient();
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
    }

    /*public UdpMessageSender() {
    }*/

    public async Task<string> SendMessageAndReceiveResponseAsync(string message, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    var endPoint = new IPEndPoint(IPAddress.Parse(_ipAddress), _port);

                    byte[] datagram = Encoding.ASCII.GetBytes(message);
                    await _client.SendAsync(datagram, datagram.Length, endPoint);

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeoutMs);

                    var receiveResult = await _client.ReceiveAsync(cts.Token);
                    var response = Encoding.ASCII.GetString(receiveResult.Buffer);
                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Response timed out");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt == MaxRetries - 1) throw;
                    await Task.Delay(InitialBackoffMs * (int)Math.Pow(2, attempt), cancellationToken);
                }
            }

            throw new InvalidOperationException("Max retries reached");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> SendCommandAndValidateResponseAsync(string command, string expectedResponsePattern, CancellationToken cancellationToken = default)
    {
        var response = await SendMessageAndReceiveResponseAsync(command, cancellationToken);
        return Regex.IsMatch(response, expectedResponsePattern);
    }

    public void Dispose()
    {
        _client.Dispose();
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}