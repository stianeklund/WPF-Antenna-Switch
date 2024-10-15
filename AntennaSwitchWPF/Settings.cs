namespace AntennaSwitchWPF;

public sealed class Settings 
{
    public int AntennaPortCount { get; set; }

    public bool HasMultipleInputs { get; set; }

   public string BandDataIpAddress { get; set; } = string.Empty;

    public int BandDataIpPort { get; set; }

    public string AntennaSwitchIpAddress { get; set; } = string.Empty;

    public int AntennaSwitchPort { get; set; }
    public string? MqttBrokerAddress { get; set; }
    public int? MqttBrokerPort { get; set; }
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }
    public string? CurrentMqttTopic { get; set; }

    public void UpdateFrom(Settings other)
    {
        AntennaPortCount = other.AntennaPortCount;
        HasMultipleInputs = other.HasMultipleInputs;
        BandDataIpAddress = other.BandDataIpAddress;
        BandDataIpPort = other.BandDataIpPort;
        AntennaSwitchIpAddress = other.AntennaSwitchIpAddress;
        AntennaSwitchPort = other.AntennaSwitchPort;
        MqttBrokerAddress = other.MqttBrokerAddress;
        MqttBrokerPort = other.MqttBrokerPort;
        MqttUsername = other.MqttUsername;
        MqttPassword = other.MqttPassword;
        CurrentMqttTopic = other.CurrentMqttTopic;
    }
}
