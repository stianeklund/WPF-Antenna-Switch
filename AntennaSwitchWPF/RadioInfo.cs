namespace AntennaSwitchWPF;

public class RadioInfo : INotifyPropertyChanged
{
    public string? RxFrequency { get; set; }
    public string? TxFrequency { get; set; }
    public string? Mode { get; set; }
    public int? ActiveRadio { get; set; }
    public bool IsSplit { get; set; }
    public bool IsTransmitting { get; set; }
}
