using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace AntennaSwitchWPF;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : IDisposable
{
    private readonly ObservableCollection<AntennaConfig> _antennaConfigs;
    private readonly BandDecoder _bandDecoder;
    private readonly FakeTs590Sg _fakeTs590Sg;
    private readonly RelayManager _relayManager;
    private readonly Settings _settings;
    private readonly UdpListener _udpListener;
    private readonly UdpMessageSender _udpMessageSender;
    private readonly SemaphoreSlim _updateSemaphore = new(1, 1);
    private int _selectedPort;
    private IManagedMqttClient _mqttClient;
    private RadioInfo _radioInfo;

    private static readonly string SystemPath = Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData
    );

    private readonly string _path = Path.Combine(SystemPath, "AntennaSwitchManager", "config.json");

    public List<int> AvailableAntennas = [];

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _settings = new Settings();
            _bandDecoder = new BandDecoder();

            _antennaConfigs = new ObservableCollection<AntennaConfig>();
            for (var i = 1; i <= 8; i++) _antennaConfigs.Add(new AntennaConfig { Port = $"{i}" });
            LoadConfigFromFile();


            _udpListener = new UdpListener();

            PortGrid.ItemsSource = _antennaConfigs;

            _fakeTs590Sg = new FakeTs590Sg(_udpListener);
            _ = _fakeTs590Sg.StartAsync(4532);

            DataContext = _settings;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            _udpMessageSender = !string.IsNullOrEmpty(_settings.AntennaSwitchIpAddress)
                ? new UdpMessageSender(_settings.AntennaSwitchIpAddress, _settings.AntennaSwitchPort)
                : new UdpMessageSender("10.0.0.12", 12090);

            _relayManager = new RelayManager(_udpMessageSender);
            _ = _relayManager.TurnOffAllRelays();
            _udpListener.RadioInfoReceived += OnRadioInfoReceived;

            _radioInfo = new RadioInfo();
            InitializeMqttClient();
            
            // Add this line to subscribe to MQTT messages
            _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessageReceived;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error initializing the application: {ex.Message}\nThe application may not function correctly.",
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Console.WriteLine($"Error initializing the application: {ex}");
        }
    }

    private async void InitializeMqttClient()
    {
        var mqttFactory = new MqttFactory();
        _mqttClient = mqttFactory.CreateManagedMqttClient();

        try
        {
            var mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.MqttBrokerAddress, _settings.MqttBrokerPort);

            if (!string.IsNullOrEmpty(_settings.MqttUsername) && !string.IsNullOrEmpty(_settings.MqttPassword))
            {
                mqttClientOptionsBuilder.WithCredentials(_settings.MqttUsername, _settings.MqttPassword);
            }

            var mqttClientOptions = mqttClientOptionsBuilder.Build();

            var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(mqttClientOptions)
                .Build();

            await _mqttClient.StartAsync(managedMqttClientOptions);

            await _mqttClient.SubscribeAsync([new MqttTopicFilterBuilder()
                .WithTopic("omnirig/sporadic/radio_info")
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .Build()]);

            _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessageReceived;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing MQTT client: {ex.Message}");
        }
    }

    private Task HandleMqttMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
    {
        var payload = arg.ApplicationMessage.PayloadSegment;
        var message = System.Text.Encoding.UTF8.GetString(payload);
        if (string.IsNullOrEmpty(message))
        {
            return Task.CompletedTask;
        }

        if (arg.ApplicationMessage.Topic.EndsWith("/radio_info"))
        {
            RadioInfo? newRadioInfo = JsonSerializer.Deserialize<RadioInfo>(message);
            if (newRadioInfo != null)
            {
                // Console.WriteLine($"MQTT Msg received:{newRadioInfo}");
                UpdateRadioInfo(newRadioInfo);
                
                // Update FakeTs590Sg with the new radio info
                _fakeTs590Sg._lastReceivedInfo = newRadioInfo;
            }
        }
        else if (arg.ApplicationMessage.Topic.EndsWith("/responses"))
        {
            // Handle command responses if needed
        }

        return Task.CompletedTask;
    }

    private void UpdateRadioInfo(RadioInfo newInfo)
    {
        _radioInfo = newInfo;
        Dispatcher.Invoke(() =>
        {
            UpdateRxFrequency(newInfo.Freq.ToString());
            UpdateTxFrequency(newInfo.TxFreq.ToString());
            UpdateMode(newInfo.Mode);
            UpdateSplitStatus(newInfo.IsSplit);
            UpdateActiveRadio(newInfo.ActiveRadioNr);
            UpdateTransmitStatus(newInfo.IsTransmitting);
            _bandDecoder.DecodeBand(newInfo.Freq.ToString());
            UpdateAntennaSelection(_bandDecoder.BandNumber);
        });
    }

    private void AntennaConfigs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    foreach (AntennaConfig item in e.NewItems)
                    {
                        item.PropertyChanged += AntennaConfig_PropertyChanged;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (AntennaConfig item in e.OldItems)
                    {
                        item.PropertyChanged -= AntennaConfig_PropertyChanged;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems != null)
                {
                    foreach (AntennaConfig item in e.OldItems)
                    {
                        item.PropertyChanged -= AntennaConfig_PropertyChanged;
                    }
                }
                if (e.NewItems != null)
                {
                    foreach (AntennaConfig item in e.NewItems)
                    {
                        item.PropertyChanged += AntennaConfig_PropertyChanged;
                    }
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                foreach (var item in _antennaConfigs)
                {
                    item.PropertyChanged += AntennaConfig_PropertyChanged;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }
        UpdateAvailableAntennas();
    }

    private void AntennaConfig_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateAvailableAntennas();
    }

    private async void UpdateAvailableAntennas()
    {
        await UpdateAvailableAntennasAsync();
        await UpdateAntennaSelection(_bandDecoder.BandNumber);
    }

    public void Dispose()
    {
        _udpListener.RadioInfoReceived -= OnRadioInfoReceived;
        _udpListener.Dispose();
        _udpMessageSender.Dispose();
        _fakeTs590Sg?.Dispose();
        _relayManager.Dispose();
        _updateSemaphore.Dispose();
        _mqttClient?.Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
        if (_fakeTs590Sg != null)
            await _fakeTs590Sg.StopAsync();
        SaveConfigToFile();
        Application.Current.Shutdown();
    }

    private async void OnRadioInfoReceived(object? sender, RadioInfo radioInfo)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                _bandDecoder.DecodeBand(radioInfo.Freq.ToString());
                await UpdateAntennaSelection(_bandDecoder.BandNumber);

                UpdateRxFrequency(radioInfo.Freq.ToString());
                UpdateTxFrequency(radioInfo.TxFreq.ToString());
                UpdateMode(radioInfo.Mode);
                UpdateSplitStatus(radioInfo.IsSplit);
                UpdateActiveRadio(radioInfo.ActiveRadioNr);
                UpdateTransmitStatus(radioInfo.IsTransmitting);
                if (_fakeTs590Sg != null) _fakeTs590Sg._lastReceivedInfo = radioInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnRadioInfoReceived: {ex}");
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
            await _relayManager.SetRelayForAntennaAsync(relayId, _bandDecoder.BandNumber);
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

        if (AvailableAntennas != null && AvailableAntennas.Count > 0)
        {
            AntennaSelectionComboBox.SelectedItem =
                currentlySelected is int selectedInt && AvailableAntennas.Contains(selectedInt) ? currentlySelected :
                AvailableAntennas.Contains(_selectedPort) ? _selectedPort :
                AvailableAntennas.First();

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
        CurrentlySelectedRelayLabel.Text = _relayManager.CurrentlySelectedRelay.ToString();
    }

    private int _previousSelectedRelay;

    private async void AntennaSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AntennaSelectionComboBox.SelectedItem is int selectedRelay && selectedRelay != _previousSelectedRelay)
        {
            await SetAntennaAsync(selectedRelay);
            _previousSelectedRelay = selectedRelay;
        }
    }

    private async Task UpdateAntennaSelection(int bandNumber)
    {
        try
        {
            var bandName = GetBandName(bandNumber);
            if (BandLabel != null) BandLabel.Text = bandName;

            if (_relayManager != null)
            {
                AvailableAntennas = await _relayManager.GetRelaysForBandAsync(bandNumber, _antennaConfigs.ToList());

                if (AvailableAntennas != null && AvailableAntennas.Count != 0)
                {
                    var lastSelectedRelay = _relayManager.GetLastSelectedRelayForBand(bandNumber);

                    _selectedPort = lastSelectedRelay != 0 && AvailableAntennas.Contains(lastSelectedRelay)
                        ? lastSelectedRelay
                        : AvailableAntennas.First();

                    await _relayManager.SetRelayForAntennaAsync(_selectedPort, bandNumber);

                    if (PortLabel != null) PortLabel.Text = _selectedPort.ToString();
                    UpdateCurrentlySelectedRelayLabel();
                    UpdateAntennaSelectionUi();

                    // Ensure the ComboBox is updated
                    if (AntennaSelectionComboBox != null)
                    {
                        AntennaSelectionComboBox.ItemsSource = AvailableAntennas;
                        AntennaSelectionComboBox.SelectedItem = _selectedPort;
                        _previousSelectedRelay = _selectedPort;
                    }
                }
                else
                {
                    _selectedPort = 0;
                    if (PortLabel != null) PortLabel.Text = "N/A";
                    if (AntennaSelectionComboBox != null)
                    {
                        AntennaSelectionComboBox.ItemsSource = null;
                        AntennaSelectionComboBox.SelectedItem = null;
                    }

                    if (CurrentlySelectedRelayLabel != null) CurrentlySelectedRelayLabel.Text = "N/A";
                }

                UpdateBandButtons(bandNumber);
            }
            else
            {
                Console.WriteLine("Error: _relayManager is null");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UpdateAntennaSelection: {ex.Message}");
        }
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

    private void UpdateRxFrequency(string frequency)
    {
        RxFrequencyLabel.Text = frequency;
    }

    private void UpdateTxFrequency(string frequency)
    {
        TxFrequencyLabel.Text = frequency;
    }

    private void UpdateMode(string mode)
    {
        ModeLabel.Text = mode;
    }

    private void UpdateSplitStatus(bool isSplit)
    {
        SplitStatusCheckBox.IsChecked = isSplit;
    }

    private void UpdateActiveRadio(int? activeRadio)
    {
        ActiveRadioLabel.Text = activeRadio?.ToString();
    }

    private void UpdateTransmitStatus(bool isTransmitting)
    {
        TransmitStatusLabel.Text = isTransmitting ? "Yes" : "No";
    }

    private void SaveAntennaConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig();
    }

    private void SaveSettingsConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig();
    }

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

    private void SaveConfigToFile()
    {
        var config = new ConfigWrapper
        {
            AntennaConfigs = _antennaConfigs.Select(ac => new AntennaConfig
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
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(config, options);
        
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new InvalidOperationException(nameof(_path)));
        File.WriteAllText(_path, jsonString);
    }

    private void LoadConfigFromFile()
    {
        if (!File.Exists(_path))
        {
            SaveConfigToFile();
            return;
        }

        var jsonString = File.ReadAllText(_path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<ConfigWrapper>(jsonString, options);

        if (config?.AntennaConfigs != null)
        {
            _antennaConfigs.Clear();
            foreach (var antennaConfig in config.AntennaConfigs)
            {
                var newConfig = new AntennaConfig
                {
                    Port = antennaConfig.Port,
                    AntennaName = antennaConfig.AntennaName,
                    Description = antennaConfig.Description,
                    Is160M = antennaConfig.Is160M,
                    Is80M = antennaConfig.Is80M,
                    Is40M = antennaConfig.Is40M,
                    Is30M = antennaConfig.Is30M,
                    Is20M = antennaConfig.Is20M,
                    Is17M = antennaConfig.Is17M,
                    Is15M = antennaConfig.Is15M,
                    Is12M = antennaConfig.Is12M,
                    Is10M = antennaConfig.Is10M,
                    Is6M = antennaConfig.Is6M
                };
                _antennaConfigs.Add(newConfig);
            }
        }

        if (config?.Settings != null)
        {
            _settings.BandDataIpAddress = config.Settings.BandDataIpAddress;
            _settings.BandDataIpPort = config.Settings.BandDataIpPort;
            _settings.AntennaSwitchIpAddress = config.Settings.AntennaSwitchIpAddress;
            _settings.AntennaSwitchPort = config.Settings.AntennaSwitchPort;
            _settings.AntennaPortCount = config.Settings.AntennaPortCount;
            _settings.HasMultipleInputs = config.Settings.HasMultipleInputs;
            _settings.MqttBrokerAddress = config.Settings.MqttBrokerAddress;
            _settings.MqttBrokerPort = config.Settings.MqttBrokerPort;
            _settings.MqttUsername = config.Settings.MqttUsername;
            _settings.MqttPassword = config.Settings.MqttPassword;

        }

        UpdateAvailableAntennas();
    }

    private void CancelAntennaConfig_Click(object sender, RoutedEventArgs e)
    {
        LoadConfigFromFile();
        MessageBox.Show("Changes canceled and previous configuration restored.", "Canceled", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private class ConfigWrapper
    {
        public List<AntennaConfig> AntennaConfigs { get; set; }
        public Settings Settings { get; set; }
    }
}
