namespace AntennaSwitchWPF;

public interface IUdpMessageSender : IDisposable
{
    Task<string> SendMessageAndReceiveResponseAsync(string message, CancellationToken cancellationToken = default);

    Task<bool> SendCommandAndValidateResponseAsync(string command, string expectedResponse, CancellationToken
        cancellationToken = default);
}