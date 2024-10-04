using System.ComponentModel;

namespace AntennaSwitchWPF;

public sealed class AntennaConfig : INotifyPropertyChanged
{
    private string _port;
    private string _antennaName = "";
    private string _description = "";

    public string Port
    {
        get => _port;
        set
        {
            if (_port == value) return;
            _port = value;
            OnPropertyChanged(nameof(Port));
        }
    }

    /// <summary>
    /// The name of the antenna, only used for GUI
    /// </summary>
    public string AntennaName
    {
        get => _antennaName;
        set
        {
            if (_antennaName == value) return;

            _antennaName = value;
            OnPropertyChanged(nameof(AntennaName));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public bool Is160M { get; set; }
    public bool Is80M { get; set; }
    public bool Is40M { get; set; }
    public bool Is30M { get; set; }
    public bool Is20M { get; set; }
    public bool Is17M { get; set; }
    public bool Is15M { get; set; }
    public bool Is12M { get; set; }
    public bool Is10M { get; set; }
    public bool Is6M { get; set; }
    public int PortCount { get; set; }
    public int InputCount { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}