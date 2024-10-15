namespace AntennaSwitchWPF.RelayController;

public class RelayManager(IUdpMessageSender sender) : IDisposable
{
    private const int CooldownPeriodMs = 100;
    private readonly Dictionary<int, List<int>> _bandToRelaysCache = new();
    private readonly Dictionary<int, int> _lastSelectedRelayForBand = new();
    private Dictionary<int, bool> _relayStates = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private DateTime _lastBandChangeTime;

    public int CurrentlySelectedRelay { get; private set; }
    public string FormattedDateTime => DateTime.Now.ToString("HH:mm:ss:fff");

    public void Dispose()
    {
        sender.Dispose();
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task TurnOffAllRelays(CancellationToken cancellationToken = default)
    {
        await ExecuteWithSemaphore(() => TurnOffAllRelaysInternal(cancellationToken));
    }

    public async Task SetRelayAsync(int relayId, bool state, CancellationToken cancellationToken = default)
    {
        await ExecuteWithSemaphore(() => SetRelayAsyncInternal(relayId, state, cancellationToken));
    }

    public bool GetRelayState(int relayId) => _relayStates.TryGetValue(relayId, out var state) && state;

    public Dictionary<int, bool> GetAllRelayStates() => new(_relayStates);

    public Task<List<int>> GetRelaysForBandAsync(int bandNumber, List<AntennaConfig> antennaConfigs, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (_bandToRelaysCache.TryGetValue(bandNumber, out var cachedRelayIds)) return cachedRelayIds;

            var relayIds = antennaConfigs
                .Where(config => IsBandSupportedByConfig(config, bandNumber))
                .Select(config => int.Parse(config.Port))
                .ToList();

            _bandToRelaysCache[bandNumber] = relayIds;
            return relayIds;
        }, cancellationToken);
    }

    public async Task SetRelayForAntennaAsync(int relayId, int bandNumber, CancellationToken cancellationToken = default)
    {
        if (CurrentlySelectedRelay == relayId && _relayStates.TryGetValue(relayId, out var state) && state)
        {
            Console.WriteLine($"{FormattedDateTime} Relay {relayId} {state} No change needed.");
            return;
        }

        if (ShouldDelay())
        {
            await Task.Delay(CooldownPeriodMs, cancellationToken);
        }

        await ExecuteRelayChangeAsync(relayId, bandNumber, cancellationToken);
    }

    public async Task TurnOffAllRelaysExceptAsync(int relayToKeepOn, CancellationToken cancellationToken = default)
    {
        await ExecuteWithSemaphore(async () =>
        {
            var currentStates = await GetCurrentRelayStatesAsync(cancellationToken);

            foreach (var kvp in currentStates.Where(kvp => kvp.Key != relayToKeepOn && kvp.Value))
            {
                await SetRelayAsyncInternal(kvp.Key, false, cancellationToken);
            }

            if (!currentStates[relayToKeepOn])
            {
                await SetRelayAsyncInternal(relayToKeepOn, true, cancellationToken);
            }

            CurrentlySelectedRelay = relayToKeepOn;
        });
    }

    public int GetLastSelectedRelayForBand(int bandNumber) =>
        _lastSelectedRelayForBand.GetValueOrDefault(bandNumber, 0);

    public bool IsCorrectRelaySet(int bandNumber)
    {
        var lastSelectedRelay = GetLastSelectedRelayForBand(bandNumber);
        return CurrentlySelectedRelay == lastSelectedRelay && lastSelectedRelay != 0;
    }

    private async Task ExecuteWithSemaphore(Func<Task> action)
    {
        await _semaphore.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task TurnOffAllRelaysInternal(CancellationToken cancellationToken)
    {
        var success = await SendCommandWithRetry("RELAY-AOF-255,1,1", "RELAY-AOF-255,1,1,OK", cancellationToken);
        if (success)
        {
            foreach (var key in _relayStates.Keys) _relayStates[key] = false;
            CurrentlySelectedRelay = 0;
        }
        else
        {
            throw new InvalidOperationException("Failed to turn off all relays");
        }
    }

    private async Task SetRelayAsyncInternal(int relayId, bool state, CancellationToken cancellationToken)
    {
        if (_relayStates.TryGetValue(relayId, out var currentState) && currentState == state &&
            CurrentlySelectedRelay == relayId)
            return;

        var command = $"RELAY-SET-255,{relayId},{(state ? 1 : 0)}";
        var expectedResponse = $"RELAY-SET-255,{relayId},{(state ? 1 : 0)},OK";
        var success = await SendCommandWithRetry(command, expectedResponse, cancellationToken);

        if (success)
        {
            CurrentlySelectedRelay = relayId;
            _relayStates[relayId] = state;
        }
        else
        {
            throw new InvalidOperationException($"Failed to set relay {relayId} to {state}");
        }
    }

    private bool ShouldDelay()
    {
        var now = DateTime.Now;
        return (now - _lastBandChangeTime).TotalMilliseconds < CooldownPeriodMs;
    }

    private async Task ExecuteRelayChangeAsync(int relayId, int bandNumber, CancellationToken cancellationToken)
    {
        try
        {
            await TurnOffAllRelaysExceptAsync(relayId, cancellationToken);

            if (_relayStates.TryGetValue(relayId, out var state) && state)
            {
                Console.WriteLine($"{FormattedDateTime} Relay {relayId} successfully set {state}");
                _lastSelectedRelayForBand[bandNumber] = relayId;
                _lastBandChangeTime = DateTime.UtcNow;
            }
            else
            {
                Console.WriteLine($"{FormattedDateTime} Failed to set relay {relayId} to true, or relay already on");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ExecuteRelayChangeAsync: {ex.Message}");
        }
    }

    private static bool IsBandSupportedByConfig(AntennaConfig config, int bandNumber) =>
        bandNumber switch
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

    private async Task<bool> SendCommandWithRetry(string command, string expectedResponsePattern, CancellationToken cancellationToken)
    {
        const int maxRetries = 2;
        const int retryDelay = 50; // milliseconds

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var success = await sender.SendCommandAndValidateResponseAsync(command, expectedResponsePattern, cancellationToken);
                if (success) return true;
            }
            catch (Exception)
            {
                if (attempt == maxRetries - 1) throw;
            }

            await Task.Delay(retryDelay * (attempt + 1), cancellationToken);
        }

        return false;
    }

    private async Task<Dictionary<int, bool>> GetCurrentRelayStatesAsync(CancellationToken cancellationToken)
    {
        const string command = "RELAY-STATE-255";
        var response = await sender.SendMessageAndReceiveResponseAsync(command, cancellationToken);
        var parts = response.Split(',');
        if (parts is not ["RELAY-STATE-255", _, _, "OK"])
        {
            throw new InvalidOperationException($"Unexpected response format: {response}");
        }

        if (!int.TryParse(parts[1], out var stateHigh) || !int.TryParse(parts[2], out var stateLow))
        {
            throw new InvalidOperationException($"Invalid state values in response: {response}");
        }

        var combinedState = (stateHigh << 8) | stateLow;

        var states = new Dictionary<int, bool>();
        for (var i = 1; i <= 16; i++)
            states[i] = (combinedState & (1 << (i - 1))) != 0;

        _relayStates = states;
        return states;
    }
}
