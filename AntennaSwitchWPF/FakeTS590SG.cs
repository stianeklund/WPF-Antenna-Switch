using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AntennaSwitchWPF;

public class FakeTs590Sg : IDisposable
{
    private readonly UdpListener _udpListener;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _listeningTask;

    public FakeTs590Sg(UdpListener udpListener)
    {
        _udpListener = udpListener;
        _udpListener.RadioInfoReceived += OnRadioInfoReceived;
    }

    private RadioInfo _lastReceivedInfo = new();

    private void OnRadioInfoReceived(object? sender, RadioInfo e)
    {
        // Update internal state when new radio info is received
        _lastReceivedInfo = e;
        // Console.WriteLine($"FakeTS590SG received new radio info: RX={e.RxFrequency}, TX={e.TxFrequency}, Mode={e.Mode}, Split={e.IsSplit}, Transmitting={e.IsTransmitting}");
    }

    public async Task StartAsync(int port)
    {
        await Task.Run(() =>
        {
            _cts = new CancellationTokenSource();
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _listeningTask = ListenForConnectionsAsync(_cts.Token);

            Console.WriteLine($"FakeTS590SG started on port {port}");
        });
    }

    private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Console.WriteLine("Client connected");
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII))
        await using (var writer = new StreamWriter(stream, Encoding.ASCII))
        {
            writer.AutoFlush = true;
 
            var buffer = new char[1024];
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var bytesRead = await reader.ReadAsync(buffer, cancellationToken);

                    string command = new string(buffer, 0, bytesRead).TrimEnd();
                    if (string.IsNullOrEmpty(command)) return;
                    // Console.WriteLine($"Received command: {command}");

                    var response = ProcessCommand(command);
                    await writer.WriteAsync(response);
                    await writer.FlushAsync(cancellationToken);
                    // Console.WriteLine($"Sent response: {response}");
                }
                catch (IOException)
                {
                    // Client disconnected
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling client: {ex.Message}");
                    break;
                }
            }
        }
        Console.WriteLine("Client disconnected");
    }

    internal string ProcessCommand(string command)
    {
        return command switch
        {
            "AI;" => "AI0;",
            ";" => "?;",
            "ID;" => "ID023;",
            "FV;" => "FV1.04;",
            "TY;" => "TYK 00;",
            "FA;" => $"FA{_lastReceivedInfo.RxFrequency?.PadLeft(11, '0') ?? "00000000000"};",
            "FB;" => $"FB{_lastReceivedInfo.TxFrequency?.PadLeft(11, '0') ?? "00000000000"};",
            "MD;" => $"MD{ConvertModeToTs590Sg(_lastReceivedInfo.Mode)};",
            "TX;" => $"TX{(_lastReceivedInfo.IsTransmitting ? "1" : "0")};",
            "SP;" => $"SP{(_lastReceivedInfo.IsSplit ? "1" : "0")};",
            "PS;" => "PS1;",
            "DA;" => "DA1;",
            "IF;" => GenerateIfResponse(),
            "KS;" => "KS030;",
            "SA;" => "SA000;",
            _ => "?;"
        };
    }

    internal string GenerateIfResponse()
    {
        var vfoAFreq = _lastReceivedInfo.RxFrequency?.PadLeft(11, '0') ?? "00000000000";
        var spaces = "     "; // 5 spaces
        var ritXitFrequency = "00000"; // Assuming no RIT/XIT for now
        var ritState = "0";
        var xitState = "0";
        var memoryChannel = "000";
        var rxTxState = _lastReceivedInfo.IsTransmitting ? "1" : "0";
        var operatingMode = ConvertModeToTs590Sg(_lastReceivedInfo.Mode);
        var func = "0"; // Assuming no special function active
        var scanStatus = "0"; // Assuming not scanning
        var simplex = _lastReceivedInfo.IsSplit ? "1" : "0";
        var ctcssTone = "0"; // Assuming no CTCSS
        var ctcssFrequency = "00"; // Assuming no CTCSS frequency
        var alwaysZero = "0";

        return $"IF{vfoAFreq}{spaces}{ritXitFrequency}{ritState}{xitState}{memoryChannel}{rxTxState}{operatingMode}{func}{scanStatus}{simplex}{ctcssTone}{ctcssFrequency}{alwaysZero};";
    }

    private string ConvertModeToTs590Sg(string? mode)
    {
        return mode?.ToUpper() switch
        {
            "CW" => "3",
            "USB" => "2",
            "LSB" => "1",
            "FM" => "4",
            "AM" => "5",
            _ => "0"
        };
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _tcpListener?.Stop();

        if (_listeningTask != null)
            await Task.WhenAny(_listeningTask, Task.Delay(5000));

        _cts?.Dispose();
        _tcpListener = null;
        _listeningTask = null;
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _tcpListener?.Stop();
        GC.SuppressFinalize(this);
    }
}
