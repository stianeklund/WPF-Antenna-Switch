using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace AntennaSwitchWPF;

/// <summary>
///     Listens for N1MM type UDP broadcast
/// </summary>
public class UdpListener : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _listeningTask;
    private UdpClient? _udpClient;

    public string? RxFrequency { get; private set; }
    public string? TxFrequency { get; private set; }
    public string? Mode { get; private set; }
    public int? ActiveRadio { get; private set; }
    public bool IsSplit { get; private set; }
    public bool IsTransmitting { get; private set; }

    public void Dispose()
    {
        _cts?.Dispose();
        _udpClient?.Dispose();
        _listeningTask?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void DebugLog(RadioInfo info)
    {
        Console.Write($"[UdpListener] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: " +
                      $"RX: {info.RxFrequency}  " +
                      $"TX: {info.TxFrequency}  " +
                      $"Mode: {info.Mode}  " +
                      $"Split: {info.IsSplit}  " +
                      $"Active Radio: {info.ActiveRadio}  " +
                      $"Transmit: {info.IsTransmitting}  ");
    }

    private static void DebugLog(string message)
    {
        Console.WriteLine($"[UdpListener] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}");
    }

    public event EventHandler<RadioInfo>? RadioInfoReceived;

    public async Task StartListeningAsync(string addressOrHostname, int port)
    {
        _cts?.CancelAsync();
        try
        {
            _cts = new CancellationTokenSource();
            if (!IPAddress.TryParse(addressOrHostname, out var ipAddress))
            {
                // If it's not a valid IP address, assume it's a hostname and resolve it
                var addresses = await Dns.GetHostAddressesAsync(addressOrHostname);
                if (addresses.Length == 0)
                    throw new ArgumentException($"Unable to resolve hostname: {addressOrHostname}");

                ipAddress = addresses[0]; // Use the first resolved IP address
            }

            var endPoint = new IPEndPoint(ipAddress, port);
            try
            {
                _udpClient = new UdpClient(endPoint);
                _listeningTask = ListenAsync(_cts.Token);
                await Task.CompletedTask;
            }
            catch (SocketException e)
            {
                var errorMessage = $"Error binding to {ipAddress}:{port}. ";
                errorMessage += e.SocketErrorCode switch
                {
                    SocketError.AddressAlreadyInUse => "The address is already in use.",
                    SocketError.AddressNotAvailable => "The address is not available on this machine.",
                    _ => $"Socket error: {e.SocketErrorCode}"
                };
                throw new InvalidOperationException(errorMessage, e);
            }
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Failed to start UDP listener: {e.Message}", e);
        }
    }

    /// <summary>
    ///     Listens for UDP messages from N1MM or similar software
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                if (_udpClient == null) return;

                var result = await _udpClient.ReceiveAsync(cancellationToken);
                var message = Encoding.ASCII.GetString(result.Buffer);
                ParseMessage(message);
            }
            catch (OperationCanceledException)
            {
                // Listening was cancelled, exit the loop
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving UDP message: {ex.Message}");
                // Consider adding a small delay here to prevent tight loop in case of persistent errors
                await Task.Delay(1000, cancellationToken);
            }
    }

    private void ParseMessage(string message)
    {
        try
        {
            var radioInfo = new RadioInfo();

            // Use regular expressions to extract the values we need
            radioInfo.RxFrequency = ExtractValue(message, "Freq");
            radioInfo.TxFrequency = ExtractValue(message, "TXFreq");
            radioInfo.Mode = ExtractValue(message, "Mode");
            radioInfo.IsSplit = bool.TryParse(ExtractValue(message, "IsSplit"), out var isSplit) && isSplit;
            radioInfo.ActiveRadio = int.TryParse(ExtractValue(message, "ActiveRadioNr"), out var activeRadio) ? activeRadio : null;
            radioInfo.IsTransmitting = bool.TryParse(ExtractValue(message, "IsTransmitting"), out var isTransmitting) && isTransmitting;

            UpdateFields(radioInfo);
            RadioInfoReceived?.Invoke(this, radioInfo);
        }
        catch (Exception ex)
        {
            DebugLog($"Error parsing XML message: {ex.Message}");
        }
    }

    private static string? ExtractValue(string xml, string elementName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(xml, $@"<{elementName}>([^<]*)</{elementName}>");
        return match.Success ? match.Groups[1].Value : null;
    }

    private void UpdateFields(RadioInfo info)
    {
        RxFrequency = info.RxFrequency;
        TxFrequency = info.IsSplit ? info.TxFrequency : info.RxFrequency;
        Mode = info.Mode;
        ActiveRadio = info.ActiveRadio;
        IsSplit = info.IsSplit;
        IsTransmitting = info.IsTransmitting;
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.CancelAsync();
            _udpClient?.Close();
            _udpClient?.Dispose();

            if (_listeningTask != null)
            {
                await Task.WhenAny(_listeningTask, Task.Delay(5000)); // Wait for up to 5 seconds
                if (!_listeningTask.IsCompleted)
                    Console.WriteLine("Warning: UdpListener task did not complete within 5 seconds.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping UdpListener: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _udpClient = null;
            _listeningTask = null;
        }
    }
}
