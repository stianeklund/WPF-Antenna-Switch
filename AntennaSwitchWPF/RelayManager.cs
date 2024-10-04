using System.Collections.Concurrent;

namespace AntennaSwitchWPF;

internal class RelayManager : IDisposable
{
    private readonly UdpMessageSender _sender;
    private readonly ConcurrentDictionary<int, bool> _relayStates = new();
    private readonly ConcurrentDictionary<int, List<int>> _bandToRelaysCache = new();
    private readonly ConcurrentDictionary<int, int> _lastSelectedRelayForBand = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RelayManager(UdpMessageSender sender)
    {
        _sender = sender;
        for (int i = 1; i <= 16; i++)
        {
            _relayStates[i] = false;
        }
    }

    public int CurrentlySelectedRelay { get; private set; }

    public async Task TurnOffAllRelays(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await SendCommandWithRetry("RELAY-SET_ALL-255,0,0", cancellationToken);
            foreach (var key in _relayStates.Keys)
            {
                _relayStates[key] = false;
            }

            CurrentlySelectedRelay = 0;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SetRelayAsync(int relayId, bool state, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_relayStates.TryGetValue(relayId, out bool currentState) && currentState == state &&
                CurrentlySelectedRelay == relayId)
            {
                return; // No need to send command if the state is already as desired and the relay is already selected
            }

            string command = $"RELAY-SET-255,{relayId},{(state ? 1 : 0)}";
            await SendCommandWithRetry(command, cancellationToken);
            CurrentlySelectedRelay = relayId;
            _relayStates[relayId] = state; // Update cached state
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ToggleRelayAsync(int relayId, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            string command = $"RELAY-KEY-255,{relayId},1";
            await SendCommandWithRetry(command, cancellationToken);
            _relayStates[relayId] = !_relayStates[relayId]; // Update cached state
            CurrentlySelectedRelay = relayId;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool GetRelayState(int relayId) => _relayStates.TryGetValue(relayId, out bool state) && state;

    public Task<List<int>> GetRelaysForBandAsync(int bandNumber, List<AntennaConfig> antennaConfigs,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (_bandToRelaysCache.TryGetValue(bandNumber, out var cachedRelayIds))
            {
                return cachedRelayIds;
            }

            var relayIds = antennaConfigs
                .Where(config => IsBandSupportedByConfig(config, bandNumber))
                .Select(config => int.Parse(config.Port))
                .ToList();

            _bandToRelaysCache[bandNumber] = relayIds;
            return relayIds;
        }, cancellationToken);
    }

    public async Task SetRelayForAntennaAsync(int relayId, int bandNumber,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (CurrentlySelectedRelay != relayId)
            {
                _semaphore.Release();
                await TurnOffAllRelays(cancellationToken);
                await SetRelayAsync(relayId, true, cancellationToken);
                await _semaphore.WaitAsync(cancellationToken);
                _lastSelectedRelayForBand[bandNumber] = relayId;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public int GetLastSelectedRelayForBand(int bandNumber) =>
        _lastSelectedRelayForBand.GetValueOrDefault(bandNumber, 0);

    private static bool IsBandSupportedByConfig(AntennaConfig config, int bandNumber)
    {
        return bandNumber switch
        {
            1 => config.Is160M,
            2 => config.Is80M,
            3 => config.Is40M,
            4 => config.Is30M,
            5 => config.Is20M,
            6 => config.Is17M,
            7 => config.Is15M,
            8 => config.Is12M,
            9 => config.Is10M,
            10 => config.Is6M,
            _ => false
        };
    }

    private async Task SendCommandWithRetry(string command, CancellationToken cancellationToken)
    {
        const int maxRetries = 2;
        const int retryDelay = 50; // milliseconds

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await _sender.SendMessageAndReceiveResponseAsync(command, cancellationToken);
                return; // Success, exit the method
            }
            catch (Exception)
            {
                if (attempt == maxRetries - 1)
                {
                    throw; // Rethrow the exception on the last attempt
                }

                await Task.Delay(retryDelay * (attempt + 1), cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        _sender.Dispose();
        _semaphore.Dispose();
    }
}