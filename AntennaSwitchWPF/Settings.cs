namespace AntennaSwitchWPF;

public sealed class Settings 
{
    public int AntennaPortCount { get; set; } = 1;

    public bool HasMultipleInputs { get; set; }

   public string BandDataIpAddress { get; set; } = string.Empty;

    public int BandDataIpPort { get; set; }

    public string AntennaSwitchIpAddress { get; set; } = string.Empty;

    public int AntennaSwitchPort { get; set; }
    public string? MqttBrokerAddress { get; set; }
    public int? MqttBrokerPort { get; set; }
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }
    public string? MqttTopic { get; set; }
}
