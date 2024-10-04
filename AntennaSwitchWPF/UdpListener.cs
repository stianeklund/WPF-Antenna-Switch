using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace AntennaSwitchWPF;

/// <summary>
/// Listens for N1MM type udp broadcast
/// </summary>
public class UdpListener
{
    private readonly UdpClient _udpClient = new(Port);
    private const int Port = 12060; // Default N1MM UDP broadcast port
    // private const int Port = 2242; // Default N1MM UDP broadcast port
    public event EventHandler<string?>? RxFrequencyReceived;
    public event EventHandler<string?>? TxFrequencyReceived;
    public event EventHandler<string?>? ModeReceived;
    public event EventHandler<int?>? ActiveRadioReceived;
    public event EventHandler<bool?>? SplitReceived;
    public event EventHandler<bool?>? IsTransmittingReceived;

    public async Task StartListeningAsync()
    {
        while (true)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                var message = Encoding.ASCII.GetString(result.Buffer);
                ParseMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving UDP message: {ex.Message}");
            }
        }
    }

    private void ParseMessage(string message)
    {
        try
        {
            var xml = XElement.Parse(message);
            // Finding the radio info element might not be necessary
            // var radioInfo = xml.Element("RadioInfo");
            var rxFrequency = xml?.Element("Freq")?.Value;
            var txFrequency = xml?.Element("TxFreq")?.Value;
            var mode = xml?.Element("Mode")?.Value;
            var split = xml?.Element("IsSplit")?.Value;
            var activeRadioNum = xml?.Element("ActiveRadioNr")?.Value;
            var isTransmitting = xml?.Element("IsTransmitting")?.Value;

            if (!string.IsNullOrEmpty(rxFrequency))
            {
                RxFrequencyReceived?.Invoke(this, rxFrequency);
            }

            if (!string.IsNullOrEmpty(txFrequency))
            {
                TxFrequencyReceived?.Invoke(this, txFrequency);
            }

            if (!string.IsNullOrEmpty(mode))
            {
                ModeReceived?.Invoke(this, mode);
            }

            if (!string.IsNullOrEmpty(split))
            {
                SplitReceived?.Invoke(this, split.Equals("True"));
            }

            if (!string.IsNullOrEmpty(activeRadioNum))
            {
                if (int.TryParse(activeRadioNum, out var num))
                {
                    ActiveRadioReceived?.Invoke(this, num);
                }
            }

            if (!string.IsNullOrEmpty(isTransmitting))
            {
                if (bool.TryParse(isTransmitting, out var tx))
                {
                    IsTransmittingReceived?.Invoke(this, tx);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing XML message: {ex.Message}");
        }
    }

    public void Stop()
    {
        _udpClient.Close();
    }
}