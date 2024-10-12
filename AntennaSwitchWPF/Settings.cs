namespace AntennaSwitchWPF;

public sealed class Settings
{
    public int AntennaPortCount { get; set; } = 1;

    public bool HasMultipleInputs { get; set; }

   public string BandDataIpAddress { get; set; } = string.Empty;

    public int BandDataIpPort { get; set; }

    public string AntennaSwitchIpAddress { get; set; } = string.Empty;

    public int AntennaSwitchPort { get; set; }
}
