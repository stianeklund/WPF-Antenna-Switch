using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

namespace AntennaSwitchWPF;

public sealed class Settings : INotifyPropertyChanged
{
    private int _antennaPortCount = 1;
    private bool _hasMultipleInputs;
    private string _bandDataIpAddress = string.Empty;
    private int _bandDataIpPort;
    private string _antennaSwitchIpAddress = string.Empty;
    private int _antennaSwitchPort;

    public int AntennaPortCount
    {
        get => _antennaPortCount;
        set => SetProperty(ref _antennaPortCount, value > 0 ? value : 1);
    }

    public bool HasMultipleInputs
    {
        get => _hasMultipleInputs;
        set => SetProperty(ref _hasMultipleInputs, value);
    }

    public string BandDataIpAddress
    {
        get => _bandDataIpAddress;
        set => SetProperty(ref _bandDataIpAddress, ValidateIpAddress(value));
    }

    public int BandDataIpPort
    {
        get => _bandDataIpPort;
        set => SetProperty(ref _bandDataIpPort, ValidatePort(value));
    }

    public string AntennaSwitchIpAddress
    {
        get => _antennaSwitchIpAddress;
        set => SetProperty(ref _antennaSwitchIpAddress, ValidateIpAddress(value));
    }

    public int AntennaSwitchPort
    {
        get => _antennaSwitchPort;
        set => SetProperty(ref _antennaSwitchPort, ValidatePort(value));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private static string ValidateIpAddress(string ipAddress)
    {
        return IPAddress.TryParse(ipAddress, out _) ? ipAddress : string.Empty;
    }

    private static int ValidatePort(int port)
    {
        return port is >= 0 and <= 65535 ? port : 0;
    }
}
