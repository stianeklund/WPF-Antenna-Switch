using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AntennaSwitchWPF;

public class RadioInfo : INotifyPropertyChanged
{
    private int _freq;
    private int _txFreq;
    private string _mode;
    private bool _isTransmitting;
    private bool _isSplit;
    private bool _isConnected;
    private int _activeRadioNr;
    private string _radioName;

    public int Freq
    {
        get => _freq;
        set => SetField(ref _freq, value);
    }

    public int TxFreq
    {
        get => _txFreq;
        set => SetField(ref _txFreq, value);
    }

    public string Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    public bool IsTransmitting
    {
        get => _isTransmitting;
        set => SetField(ref _isTransmitting, value);
    }

    public bool IsSplit
    {
        get => _isSplit;
        set => SetField(ref _isSplit, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    public int ActiveRadioNr
    {
        get => _activeRadioNr;
        set => SetField(ref _activeRadioNr, value);
    }

    public string RadioName
    {
        get => _radioName;
        set => SetField(ref _radioName, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public override string ToString()
    {
        return $"RadioInfo:\n" +
               $"  Frequency: {Freq} Hz\n" +
               $"  TX Frequency: {TxFreq} Hz\n" +
               $"  Mode: {Mode}\n" +
               $"  Is Transmitting: {IsTransmitting}\n" +
               $"  Is Split: {IsSplit}\n" +
               $"  Is Connected: {IsConnected}\n" +
               $"  Active Radio Number: {ActiveRadioNr}\n" +
               $"  Radio Name: {RadioName}";
    }
}
