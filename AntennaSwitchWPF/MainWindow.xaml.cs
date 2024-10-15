using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntennaSwitchWPF.RelayController;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace AntennaSwitchWPF;

public partial class MainWindow : IDisposable
{
    private readonly ObservableCollection<AntennaConfig> _antennaConfigs;
    private readonly BandDecoder _bandDecoder;
    private readonly FakeTs590Sg _fakeTs590Sg;
    private RelayManager? _relayManager;
    private readonly Settings _settings;
    private readonly UdpListener _udpListener;
    private UdpMessageSender _udpMessageSender;
    private readonly SemaphoreSlim _updateSemaphore = new(1, 1);
    private int? _selectedPort;
    private IManagedMqttClient? _mqttClient;
    private RadioInfo _radioInfo;
    private readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true };

    private static readonly string SystemPath =
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private readonly string _configPath = Path.Combine(SystemPath, "AntennaSwitchManager", "config.json");

    public List<int> AvailableAntennas { get; private set; } = [];
    public List<string> MqttTopic { get; } = ["Frequent", "Sporadic"];

    public string FormattedDateTime => DateTime.Now.ToString("HH:mm:ss:fff");

    public MainWindow()
    {
        InitializeComponent();
        _settings = new Settings();
        _bandDecoder = new BandDecoder();
        _antennaConfigs = [];
        _udpListener = new UdpListener();
        _fakeTs590Sg = new FakeTs590Sg(_udpListener);
        _radioInfo = new RadioInfo();

        InitializeComponents();
        SetupEventHandlers();
        LoadConfigFromFile();
        InitializeMqttClientAsync().ConfigureAwait(false);
    }

    private void InitializeComponents()
    {
        _udpMessageSender = CreateUdpMessageSender();
        _relayManager = new RelayManager(_udpMessageSender);

        var portCount = _settings.AntennaPortCount > 0 ? _settings.AntennaPortCount : 6;
        for (var i = 1; i <= portCount; i++) _antennaConfigs.Add(new AntennaConfig { Port = $"{i}" });

        PortGrid.ItemsSource = _antennaConfigs;
        DataContext = _settings;
        _fakeTs590Sg.StartAsync(4532).ConfigureAwait(false);

        MqttTopicComboBox.ItemsSource = MqttTopic;
        MqttTopicComboBox.SelectedItem = MqttTopic.FirstOrDefault();
    }

    private void SetupEventHandlers()
    {
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SizeChanged += OnSizeChanged;
        _udpListener.RadioInfoReceived += OnRadioInfoReceived;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    private UdpMessageSender CreateUdpMessageSender()
    {
        return !string.IsNullOrEmpty(_settings.AntennaSwitchIpAddress)
            ? new UdpMessageSender(_settings.AntennaSwitchIpAddress, _settings.AntennaSwitchPort)
            : new UdpMessageSender("10.0.0.12", 12090);
    }

    private async Task InitializeMqttClientAsync()
    {
        try
        {
            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateManagedMqttClient();

            var mqttClientOptions = CreateMqttClientOptions();
            if (mqttClientOptions == null) return;

            var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(mqttClientOptions)
                .Build();

            await _mqttClient.StartAsync(managedMqttClientOptions);
            await SubscribeToMqttTopicAsync();
            _mqttClient.SubscriptionsChangedAsync += MqttClientOnSubscriptionsChangedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessageReceivedAsync;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing MQTT client: {ex.Message} {ex.StackTrace}");
        }
    }

    private Task MqttClientOnSubscriptionsChangedAsync(SubscriptionsChangedEventArgs arg)
    {
        Console.WriteLine(
            $"{FormattedDateTime} MQTT subscription changed, topic: {arg.SubscribeResult.FirstOrDefault()?.Items.FirstOrDefault()?.TopicFilter.Topic}");
        return Task.CompletedTask;
    }

    private MqttClientOptions? CreateMqttClientOptions()
    {
        if (string.IsNullOrEmpty(_settings.MqttBrokerAddress) || _settings.MqttBrokerPort is 0 or null) return null;

        var mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.MqttBrokerAddress, _settings.MqttBrokerPort);

        if (!string.IsNullOrEmpty(_settings.MqttUsername) && !string.IsNullOrEmpty(_settings.MqttPassword))
            mqttClientOptionsBuilder.WithCredentials(_settings.MqttUsername, _settings.MqttPassword);

        return mqttClientOptionsBuilder.Build();
    }

    private async Task SubscribeToMqttTopicAsync()
    {
        var topic = GetMqttTopic().ToLower();

        var topicFilter = new MqttTopicFilterBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
            .Build();

        await _mqttClient!.SubscribeAsync([topicFilter]);
    }

    private string GetMqttTopic()
    {
        return _settings.CurrentMqttTopic != null
            ? $"omnirig/{_settings.CurrentMqttTopic.ToLower()}/radio_info"
            : $"omnirig/{MqttTopic.FirstOrDefault()?.ToLower()}/radio_info";
    }

    private Task HandleMqttMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        var message = Encoding.UTF8.GetString(arg.ApplicationMessage.PayloadSegment);
        if (string.IsNullOrEmpty(message)) return Task.CompletedTask;

        if (arg.ApplicationMessage.Topic.EndsWith("/radio_info"))
        {
            HandleRadioInfoMessage(message);
        }
        else if (arg.ApplicationMessage.Topic.EndsWith("/responses"))
        {
            // Handle command responses if needed
        }

        return Task.CompletedTask;
    }

    private void HandleRadioInfoMessage(string message)
    {
        var newRadioInfo = JsonSerializer.Deserialize<RadioInfo>(message);
        if (newRadioInfo != null)
        {
            UpdateRadioInfo(newRadioInfo);
            _fakeTs590Sg._lastReceivedInfo = newRadioInfo;
        }
    }

    private void UpdateRadioInfo(RadioInfo newInfo)
    {
        _radioInfo = newInfo;
        Dispatcher.Invoke(() => UpdateUiWithRadioInfo(newInfo));
    }

    private void UpdateUiWithRadioInfo(RadioInfo info)
    {
        UpdateRxFrequency(info.Freq.ToString());
        UpdateTxFrequency(info.TxFreq.ToString());
        UpdateMode(info.Mode);
        UpdateSplitStatus(info.IsSplit);
        UpdateActiveRadio(info.ActiveRadioNr);
        UpdateTransmitStatus(info.IsTransmitting);
        _bandDecoder.DecodeBand(info.Freq.ToString());
        UpdateAntennaSelectionAsync(_bandDecoder.BandNumber).ConfigureAwait(false);
    }

    private async void UpdateAvailableAntennas()
    {
        await UpdateAvailableAntennasAsync();
        await UpdateAntennaSelectionAsync(_bandDecoder.BandNumber);
    }

    public void Dispose()
    {
        _udpListener.RadioInfoReceived -= OnRadioInfoReceived;
        _udpListener.Dispose();
        _udpMessageSender.Dispose();
        _fakeTs590Sg.Dispose();
        _relayManager?.Dispose();
        _updateSemaphore.Dispose();
        _mqttClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await StartUdpListenerAsync();
    }

    private async void MainWindow_OnSizeChanged(object sender, RoutedEventArgs e)
    {
        await StartUdpListenerAsync();
    }

    private async Task StartUdpListenerAsync()
    {
        if (!string.IsNullOrEmpty(_settings.BandDataIpAddress) && _settings.BandDataIpPort > 0)
            await _udpListener.StartListeningAsync(_settings.BandDataIpAddress, _settings.BandDataIpPort);
    }

    private async Task StopUdpListenerAsync()
    {
        await _udpListener.StopAsync();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        await StopUdpListenerAsync();
        await _fakeTs590Sg.StopAsync();
        SaveConfigToFile();
        Application.Current.Shutdown();
    }

    private async void OnRadioInfoReceived(object? sender, RadioInfo radioInfo)
    {
        Console.WriteLine($"{FormattedDateTime} OnRadioInfoReceived called");
        await Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                _bandDecoder.DecodeBand(radioInfo.Freq.ToString());

                if (_relayManager != null && !_relayManager.IsCorrectRelaySet(_bandDecoder.BandNumber))
                    await UpdateAntennaSelectionAsync(_bandDecoder.BandNumber);

                UpdateUiWithRadioInfo(radioInfo);
                _fakeTs590Sg._lastReceivedInfo = radioInfo;

                Console.WriteLine($"{FormattedDateTime} OnRadioInfoReceived completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{FormattedDateTime} Error in OnRadioInfoReceived: {ex}");
            }
        });
    }

    private async Task UpdateAvailableAntennasAsync()
    {
        try
        {
            if (_relayManager != null)
            {
                AvailableAntennas =
                    await _relayManager.GetRelaysForBandAsync(_bandDecoder.BandNumber, _antennaConfigs.ToList());
                UpdateAntennaSelectionUi();
                UpdateBandButtons(_bandDecoder.BandNumber);
                UpdateCurrentlySelectedRelayLabel();
            }
            else
            {
                Console.WriteLine("Error: _relayManager is null");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting available antennas: {ex.Message}");
        }
    }

    private async Task SetAntennaAsync(int relayId)
    {
        try
        {
            if (_relayManager != null) await _relayManager.SetRelayForAntennaAsync(relayId, _bandDecoder.BandNumber);

            UpdateCurrentlySelectedRelayLabel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting antenna: {ex.Message}");
        }
    }

    private void UpdateAntennaSelectionUi()
    {
        if (AntennaSelectionComboBox == null)
        {
            Console.WriteLine("Error: AntennaSelectionComboBox is null");
            return;
        }

        var currentlySelected = AntennaSelectionComboBox.SelectedItem;
        AntennaSelectionComboBox.ItemsSource = AvailableAntennas;

        if (AvailableAntennas.Count > 0)
        {
            AntennaSelectionComboBox.SelectedItem =
                currentlySelected is int selectedInt && AvailableAntennas.Contains(selectedInt) ? currentlySelected :
                AvailableAntennas.Contains((int)_selectedPort!) ? _selectedPort :
                AvailableAntennas.FirstOrDefault();

            AntennaSelectionComboBox.IsEnabled = AvailableAntennas.Count > 1;
            AntennaSelectionComboBox.Visibility = AvailableAntennas.Count > 1 ? Visibility.Visible : Visibility.Hidden;
        }
        else
        {
            AntennaSelectionComboBox.SelectedItem = null;
            AntennaSelectionComboBox.IsEnabled = false;
            AntennaSelectionComboBox.Visibility = Visibility.Hidden;
        }
    }

    private void UpdateCurrentlySelectedRelayLabel()
    {
        CurrentlySelectedRelayLabel.Text = _relayManager?.CurrentlySelectedRelay.ToString() ?? "N/A";
    }

    private async void AntennaSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AntennaSelectionComboBox.SelectedItem is int selectedRelay &&
            selectedRelay != _relayManager?.GetLastSelectedRelayForBand(_bandDecoder.BandNumber))
            await SetAntennaAsync(selectedRelay);
    }

    private async Task UpdateAntennaSelectionAsync(int bandNumber)
    {
        try
        {
            if (BandLabel != null) BandLabel.Text = GetBandName(bandNumber);

            if (_relayManager == null)
            {
                ResetAntennaSelection();
                UpdateBandButtons(bandNumber);
                return;
            }

            AvailableAntennas = await _relayManager.GetRelaysForBandAsync(bandNumber, _antennaConfigs.ToList());
            var lastSelectedRelay = _relayManager.GetLastSelectedRelayForBand(bandNumber);

            

            _selectedPort = AvailableAntennas.Contains(lastSelectedRelay) ? lastSelectedRelay : AvailableAntennas.FirstOrDefault();

            if (_selectedPort != 0)
            {
                if (PortLabel != null) PortLabel.Text = _selectedPort.ToString();
                UpdateCurrentlySelectedRelayLabel();
                UpdateAntennaSelectionUi();

                // Only update if it's the first run or if the relay has changed
                if (_relayManager.CurrentlySelectedRelay == 0 || lastSelectedRelay != _relayManager.CurrentlySelectedRelay)
                {
                    Console.WriteLine($"{FormattedDateTime} Calling SetAntennaAsync for port {_selectedPort}");
                    if (_selectedPort != null) await SetAntennaAsync(_selectedPort.Value);
                    Console.WriteLine($"{FormattedDateTime} Available antennas: {string.Join(", ", AvailableAntennas)}");
                    Console.WriteLine($"{FormattedDateTime} Last selected relay: {lastSelectedRelay}");
                }
            }
            else
            {
                ResetAntennaSelection();
            }

            UpdateBandButtons(bandNumber);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{FormattedDateTime} Error in UpdateAntennaSelection: {ex.Message}");
        }
    }

    private void ResetAntennaSelection()
    {
        Console.WriteLine($"{FormattedDateTime} No available antennas for band {_bandDecoder.BandNumber}");
        _selectedPort = 0;
        if (PortLabel != null) PortLabel.Text = "N/A";
        if (AntennaSelectionComboBox != null)
        {
            AntennaSelectionComboBox.ItemsSource = null;
            AntennaSelectionComboBox.SelectedItem = null;
        }

        UpdateCurrentlySelectedRelayLabel();
    }

    private void UpdateBandButtons(int selectedBandNumber)
    {
        var buttons = new[]
            { Band160M, Band80M, Band40M, Band30M, Band20M, Band17M, Band15M, Band12M, Band10M, Band6M };

        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            var bandNumber = i + 1;
            var isSelected = bandNumber == selectedBandNumber;
            var isTransmitting = _udpListener.IsTransmitting;
            var isConfigured = _antennaConfigs.Any(a => IsBandSelectedByConfig(a, bandNumber));

            button.Background = isSelected
                ? isTransmitting ? Brushes.Red : Brushes.Green
                : isConfigured
                    ? Brushes.DodgerBlue
                    : Brushes.DimGray;
            button.Foreground = Brushes.White;
            button.IsEnabled = isConfigured;
        }
    }

    private static string GetBandName(int bandNumber)
    {
        return bandNumber switch
        {
            0 => "None",
            1 => "160m",
            2 => "80m",
            3 => "40m",
            4 => "30m",
            5 => "20m",
            6 => "17m",
            7 => "15m",
            8 => "12m",
            9 => "10m",
            10 => "6m",
            _ => throw new ArgumentOutOfRangeException(nameof(bandNumber), bandNumber, null)
        };
    }

    private static bool IsBandSelectedByConfig(AntennaConfig config, int bandNumber)
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

    private void UpdateRxFrequency(string frequency) => RxFrequencyLabel.Text = frequency;

    private void UpdateTxFrequency(string frequency) => TxFrequencyLabel.Text = frequency;

    private void UpdateMode(string mode) => ModeLabel.Text = mode;

    private void UpdateSplitStatus(bool isSplit) => SplitStatusCheckBox.IsChecked = isSplit;

    private void UpdateActiveRadio(int? activeRadio) => ActiveRadioLabel.Text = activeRadio?.ToString() ?? "N/A";

    private void UpdateTransmitStatus(bool isTransmitting) => TransmitStatusLabel.Text = isTransmitting ? "Yes" : "No";

    private void SaveAntennaConfig_Click(object sender, RoutedEventArgs e) => SaveConfig();

    private void SaveSettingsConfig_Click(object sender, RoutedEventArgs e) => SaveConfig();

    private void SaveConfig()
    {
        try
        {
            SaveConfigToFile();
            MessageBox.Show("Configuration saved successfully.", "Success", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CancelSettingsConfig_Click(object sender, RoutedEventArgs e)
    {
        LoadConfigFromFile();
        MessageBox.Show("Changes canceled and previous configuration restored.", "Canceled", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task ChangeUpdateFrequency(string? topic)
    {
        if (string.IsNullOrEmpty(topic)) return;

        MqttTopicComboBox.SelectedItem = topic;
        _settings.CurrentMqttTopic = topic;

        if (_mqttClient is { IsConnected: true, IsStarted: true })
        {
            await _mqttClient.UnsubscribeAsync("omnirig/+/radio_info");
            await SubscribeToMqttTopicAsync();
        }

        if (!string.IsNullOrEmpty(_settings.AntennaSwitchIpAddress)) SaveConfigToFile();
    }

    private void SaveConfigToFile()
    {
        var config = new ConfigWrapper
        {
            AntennaConfigs = _antennaConfigs.Take(_settings.AntennaPortCount > 0 ? _settings.AntennaPortCount : 6)
                .Select(ac => new AntennaConfig
                {
                    Port = ac.Port,
                    AntennaName = ac.AntennaName,
                    Description = ac.Description,
                    Is160M = ac.Is160M,
                    Is80M = ac.Is80M,
                    Is40M = ac.Is40M,
                    Is30M = ac.Is30M,
                    Is20M = ac.Is20M,
                    Is17M = ac.Is17M,
                    Is15M = ac.Is15M,
                    Is12M = ac.Is12M,
                    Is10M = ac.Is10M,
                    Is6M = ac.Is6M
                }).ToList(),
            Settings = new Settings
            {
                BandDataIpAddress = _settings.BandDataIpAddress,
                BandDataIpPort = _settings.BandDataIpPort,
                AntennaSwitchIpAddress = _settings.AntennaSwitchIpAddress,
                AntennaSwitchPort = _settings.AntennaSwitchPort,
                AntennaPortCount = _settings.AntennaPortCount,
                HasMultipleInputs = _settings.HasMultipleInputs,
                MqttBrokerAddress = _settings.MqttBrokerAddress,
                MqttBrokerPort = _settings.MqttBrokerPort,
                MqttUsername = _settings.MqttUsername,
                MqttPassword = _settings.MqttPassword,
                CurrentMqttTopic = _settings.CurrentMqttTopic
            }
        };

        //var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(config, _options);

        Console.WriteLine($"Saving config to file: {_configPath}");

        Directory.CreateDirectory(Path.GetDirectoryName(_configPath) ??
                                  throw new InvalidOperationException(nameof(_configPath)));
        File.WriteAllText(_configPath, jsonString);

        Console.WriteLine("Config saved successfully");
    }

    private void LoadConfigFromFile()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Console.WriteLine($"Config file not found at {_configPath}. Creating a new one.");
                SaveConfigToFile();
                return;
            }

            var jsonString = File.ReadAllText(_configPath);

            var config = JsonSerializer.Deserialize<ConfigWrapper>(jsonString, _options) ??
                         throw new JsonException("Deserialized config is null");

            UpdateAntennaConfigs(config.AntennaConfigs);
            UpdateSettings(config.Settings);

            UpdateAvailableAntennas();
            Console.WriteLine("Config loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            MessageBox.Show(
                $"Error loading configuration: {ex.Message}\n\nThe application will continue with default settings.",
                "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateAntennaConfigs(List<AntennaConfig>? loadedConfigs)
    {
        if (loadedConfigs == null)
        {
            Console.WriteLine("Warning: AntennaConfigs is null in the loaded config");
            return;
        }

        _antennaConfigs.Clear();

        var portCount = _settings.AntennaPortCount > 0 ? _settings.AntennaPortCount : 6;

        for (var i = 1; i <= portCount; i++)
        {
            var existingConfig = loadedConfigs.FirstOrDefault(ac => ac.Port == $"{i}");
            _antennaConfigs.Add(existingConfig ?? new AntennaConfig { Port = $"{i}" });
        }

        Console.WriteLine($"Loaded {_antennaConfigs.Count} antenna configs");
    }

    private void UpdateSettings(Settings? loadedSettings)
    {
        if (loadedSettings == null)
        {
            Console.WriteLine("Warning: Settings is null in the loaded config");
            return;
        }

        _settings.UpdateFrom(loadedSettings);

        DataContext = null;
        DataContext = _settings;

        RecreateUdpMessageSender();
        RecreateRelayManager();
        UpdateMqttClientSettings();
    }

    private void RecreateUdpMessageSender()
    {
        _udpMessageSender.Dispose();
        _udpMessageSender = new UdpMessageSender(_settings.AntennaSwitchIpAddress, _settings.AntennaSwitchPort);
    }

    private void RecreateRelayManager()
    {
        _relayManager?.Dispose();
        _relayManager = new RelayManager(_udpMessageSender);
    }

    private void UpdateMqttClientSettings()
    {
        if (_mqttClient == null) return;

        _mqttClient.Dispose();
        InitializeMqttClientAsync().ConfigureAwait(false);
    }

    private void CancelAntennaConfig_Click(object sender, RoutedEventArgs e)
    {
        LoadConfigFromFile();
        MessageBox.Show("Changes canceled and previous configuration restored.", "Canceled", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void MqttTopicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string selectedTopic }) await ChangeUpdateFrequency(selectedTopic);
    }

    private class ConfigWrapper
    {
        public List<AntennaConfig> AntennaConfigs { get; set; } = [];
        public Settings Settings { get; init; } = new();
    }
}
