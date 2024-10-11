using System.Collections.Concurrent;
using System.Timers;
using Timer = System.Timers.Timer;

namespace AntennaSwitchWPF;

internal class RelayManager : IDisposable
{
    private const int CooldownPeriodMs = 100; // 500ms cooldown
    private readonly ConcurrentDictionary<int, List<int>> _bandToRelaysCache = new();
    private readonly Timer _cooldownTimer;
    private readonly ConcurrentDictionary<int, int> _lastSelectedRelayForBand = new();
    private readonly ConcurrentDictionary<int, bool> _relayStates = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly UdpMessageSender _sender;
    private DateTime _lastBandChangeTime;

    public RelayManager(UdpMessageSender sender)
    {
        _sender = sender;
        for (var i = 1; i <= 16; i++) _relayStates[i] = false;
        _cooldownTimer = new Timer(CooldownPeriodMs);
        _cooldownTimer.Elapsed += CooldownTimer_Elapsed;
        _lastBandChangeTime = DateTime.MinValue;
    }

    public int CurrentlySelectedRelay { get; private set; }
    public bool IsCoolingDown => _cooldownTimer.Enabled;

    private (int RelayId, int BandNumber)? QueuedRelayChange { get; set; }

    public void Dispose()
    {
        _sender.Dispose();
        _semaphore.Dispose();
        _cooldownTimer.Dispose();
    }

    private async void CooldownTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        _cooldownTimer.Stop();
        if (QueuedRelayChange.HasValue)
        {
            var (relayId, bandNumber) = QueuedRelayChange.Value;
            await ExecuteRelayChangeAsync(relayId, bandNumber, CancellationToken.None);
        }
    }

    public async Task TurnOffAllRelays(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await TurnOffAllRelaysInternal(cancellationToken);
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
            await SetRelayAsyncInternal(relayId, state, cancellationToken);
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
            var command = $"RELAY-KEY-255,{relayId},1";
            await SendCommandWithRetry(command, cancellationToken);
            _relayStates[relayId] = !_relayStates[relayId]; // Update cached state
            CurrentlySelectedRelay = relayId;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool GetRelayState(int relayId)
    {
        return _relayStates.TryGetValue(relayId, out var state) && state;
    }

    public Task<List<int>> GetRelaysForBandAsync(int bandNumber, List<AntennaConfig> antennaConfigs,
        CancellationToken cancellationToken = default)
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

    public async Task SetRelayForAntennaAsync(int relayId, int bandNumber,
        CancellationToken cancellationToken = default)
    {
        if (CurrentlySelectedRelay == relayId) return; // No change needed

        var now = DateTime.UtcNow;
        if (IsCoolingDown || (now - _lastBandChangeTime).TotalMilliseconds < CooldownPeriodMs)
        {
            // If we're in cooldown or trying to change too quickly, queue the change
            _cooldownTimer.Stop(); // Stop any existing timer
            _cooldownTimer.Start(); // Start a new cooldown period
            QueuedRelayChange = (relayId, bandNumber);
            return;
        }

        await ExecuteRelayChangeAsync(relayId, bandNumber, cancellationToken);
    }

    private async Task ExecuteRelayChangeAsync(int relayId, int bandNumber, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("Turning off all relays");
            await TurnOffAllRelaysInternal(cancellationToken);

            Console.WriteLine($"Setting relay {relayId} to true");
            await SetRelayAsyncInternal(relayId, true, cancellationToken);

            if (_relayStates.TryGetValue(relayId, out var state) && state)
            {
                Console.WriteLine($"Relay {relayId} successfully set to true");
                _lastSelectedRelayForBand[bandNumber] = relayId;
                CurrentlySelectedRelay = relayId;
                _lastBandChangeTime = DateTime.UtcNow;
                QueuedRelayChange = null;
            }
            else
            {
                Console.WriteLine($"Failed to set relay {relayId} to true");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ExecuteRelayChangeAsync: {ex.Message}");
        }

    }

    private async Task TurnOffAllRelaysInternal(CancellationToken cancellationToken)
    {
        await SendCommandWithRetry("RELAY-SET_ALL-255,0,0", cancellationToken);
        foreach (var key in _relayStates.Keys) _relayStates[key] = false;
        CurrentlySelectedRelay = 0;
    }

    private async Task SetRelayAsyncInternal(int relayId, bool state, CancellationToken cancellationToken)
    {
        if (_relayStates.TryGetValue(relayId, out var currentState) && currentState == state &&
            CurrentlySelectedRelay == relayId)
        {
            return;
        }

        var command = $"RELAY-SET-255,{relayId},{(state ? 1 : 0)}";
        await SendCommandWithRetry(command, cancellationToken);
        CurrentlySelectedRelay = relayId;
        _relayStates[relayId] = state; // Update cached state
    }

    public int GetLastSelectedRelayForBand(int bandNumber)
    {
        return _lastSelectedRelayForBand.GetValueOrDefault(bandNumber, 0);
    }

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

        for (var attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                await _sender.SendMessageAndReceiveResponseAsync(command, cancellationToken);
                return; // Success, exit the method
            }
            catch (Exception)
            {
                if (attempt == maxRetries - 1) throw; // Rethrow the exception on the last attempt

                await Task.Delay(retryDelay * (attempt + 1), cancellationToken);
            }
    }
}