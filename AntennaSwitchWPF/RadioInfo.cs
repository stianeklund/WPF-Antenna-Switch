using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AntennaSwitchWPF;

public class RadioInfo : INotifyPropertyChanged
{
    public string? RxFrequency { get; set; }
    public string? TxFrequency { get; set; }
    public string? Mode { get; set; }
    public int? ActiveRadio { get; set; }
    public bool IsSplit { get; set; }
    public bool IsTransmitting { get; set; }

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
}
