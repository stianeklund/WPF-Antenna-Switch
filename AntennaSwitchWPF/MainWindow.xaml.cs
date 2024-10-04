using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AntennaSwitchWPF;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : IDisposable
{
    private readonly ObservableCollection<AntennaConfig> _antennaConfigs;
    private readonly BandDecoder _bandDecoder;
    private readonly Settings _settings;
    private readonly UdpListener _udpListener;
    private readonly UdpMessageSender _udpMessageSender;
    private readonly RelayManager _relayManager;
    private int _selectedPort;
    private readonly FakeTS590SG? _fakeTs590Sg;
    private readonly SemaphoreSlim _updateSemaphore = new(1, 1);

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _bandDecoder = new BandDecoder();
            _udpListener = new UdpListener();

            _antennaConfigs = new ObservableCollection<AntennaConfig>();
            for (var i = 1; i <= 8; i++) _antennaConfigs.Add(new AntennaConfig { Port = $"{i}" });

            PortGrid.ItemsSource = _antennaConfigs;

            _fakeTs590Sg = new FakeTS590SG(_udpListener);
            _ = _fakeTs590Sg.StartAsync(4532);

            _settings = new Settings();
            DataContext = _settings;

            try
            {
                Loaded += MainWindow_Loaded;
                Closing += MainWindow_Closing;
                LoadConfigFromFile();
                _udpMessageSender = !string.IsNullOrEmpty(_settings.AntennaSwitchIpAddress)
                    ? new UdpMessageSender(_settings.AntennaSwitchIpAddress, _settings.AntennaSwitchPort)
                    : new UdpMessageSender("10.0.0.12", 12090);

                _relayManager = new RelayManager(_udpMessageSender);
                _ = _relayManager.TurnOffAllRelays();
                _udpListener.RadioInfoReceived += OnRadioInfoReceived;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration: {ex.Message}\nUsing default settings.",
                    "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Console.WriteLine($"Error loading configuration: {ex}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error initializing the application: {ex.Message}\nThe application may not function correctly.",
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Console.WriteLine($"Error initializing the application: {ex}");
        }
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
        await _updateSemaphore.WaitAsync();
        try
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    _bandDecoder.DecodeBand(radioInfo.RxFrequency);

                    await UpdateAntennaSelection(_bandDecoder.BandNumber);

                    UpdateRxFrequency(radioInfo.RxFrequency ?? "N/A");
                    UpdateTxFrequency(radioInfo.TxFrequency ?? "N/A");
                    UpdateMode(radioInfo.Mode ?? "N/A");
                    UpdateSplitStatus(radioInfo.IsSplit);
                    UpdateActiveRadio(radioInfo.ActiveRadio);
                    UpdateTransmitStatus(radioInfo.IsTransmitting);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in OnRadioInfoReceived: {ex}");
                }
            });
        }
        finally
        {
            _updateSemaphore.Release();
        }
    }

    private List<int> _availableAntennas = [];

    private async Task UpdateAvailableAntennasAsync()
    {
        try
        {
            _availableAntennas = await _relayManager.GetRelaysForBandAsync(_bandDecoder.BandNumber, _antennaConfigs.ToList());
            UpdateAntennaSelectionUi();
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
            UpdateCurrentlySelectedRelay();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting antenna: {ex.Message}");
        }
    }

    private void UpdateAntennaSelectionUi()
    {
        AntennaSelectionComboBox.ItemsSource = _availableAntennas;
        AntennaSelectionComboBox.SelectedItem = null;
        AntennaSelectionComboBox.IsEnabled = _availableAntennas.Count > 1;
    }

    private void UpdateCurrentlySelectedRelay()
    {
        CurrentlySelectedRelayLabel.Text = _relayManager.CurrentlySelectedRelay.ToString();
        AntennaSelectionComboBox.SelectedItem = _relayManager.CurrentlySelectedRelay;
    }

    private async void AntennaSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AntennaSelectionComboBox.SelectedItem is int selectedRelay)
        {
            await SetAntennaAsync(selectedRelay);
        }
    }

    private async Task UpdateAntennaSelection(int bandNumber)
    {
        Console.WriteLine($"UpdateAntennaSelection started for band {bandNumber}");
        var bandName = GetBandName(bandNumber);
        BandLabel.Text = bandName;
        Console.WriteLine($"Band name set to {bandName}");

        var availableAntennas = await _relayManager.GetRelaysForBandAsync(bandNumber, _antennaConfigs.ToList());
        Console.WriteLine($"Available antennas: {string.Join(", ", availableAntennas)}");

        if (availableAntennas.Any())
        {
            int lastSelectedRelay = _relayManager.GetLastSelectedRelayForBand(bandNumber);
            Console.WriteLine($"Last selected relay: {lastSelectedRelay}");

            _selectedPort = lastSelectedRelay != 0 && availableAntennas.Contains(lastSelectedRelay)
                ? lastSelectedRelay
                : availableAntennas.First();
            Console.WriteLine($"Selected port: {_selectedPort}");
            
            await _relayManager.SetRelayForAntennaAsync(_selectedPort, bandNumber);
            Console.WriteLine("SetRelayForAntennaAsync completed");

            PortLabel.Text = _selectedPort.ToString();
            UpdateCurrentlySelectedRelay();
        }
        else
        {
            _selectedPort = 0;
            PortLabel.Text = "N/A";
            Console.WriteLine("No available antennas");
        }

        UpdateBandButtons(bandNumber);
        await UpdateAvailableAntennasAsync();
        Console.WriteLine("UpdateAntennaSelection completed");
    }

    private void UpdateBandButtons(int selectedBandNumber)
    {
        var buttons = new[] { Band160M, Band80M, Band40M, Band30M, Band20M, Band17M, Band15M, Band12M, Band10M, Band6M };
        
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            var bandNumber = i + 1;
            var isSelected = bandNumber == selectedBandNumber;
            var isTransmitting = _udpListener.IsTransmitting;
            var isConfigured = _antennaConfigs.Any(a => IsBandSelectedByConfig(a, bandNumber));

            button.Background = isSelected
                ? isTransmitting ? Brushes.Red : Brushes.Green
                : isConfigured ? Brushes.DodgerBlue : Brushes.DimGray;
            button.Foreground = Brushes.White;
            button.IsEnabled = isConfigured;
        }
    }

    private static string GetBandName(int bandNumber) => bandNumber switch
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

    private static bool IsBandSelectedByConfig(AntennaConfig config, int bandNumber) => bandNumber switch
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

    private void UpdateRxFrequency(string frequency) => RxFrequencyLabel.Text = frequency;
    private void UpdateTxFrequency(string frequency) => TxFrequencyLabel.Text = frequency;
    private void UpdateMode(string mode) => ModeLabel.Text = mode;
    private void UpdateSplitStatus(bool isSplit) => SplitStatusCheckBox.IsChecked = isSplit;
    private void UpdateActiveRadio(int? activeRadio) => ActiveRadioLabel.Text = activeRadio?.ToString() ?? "No radio selected";
    private void UpdateTransmitStatus(bool isTransmitting) => TransmitStatusLabel.Text = isTransmitting ? "Yes" : "No";

    private void SaveAntennaConfig_Click(object sender, RoutedEventArgs e) => SaveConfig();
    private void SaveSettingsConfig_Click(object sender, RoutedEventArgs e) => SaveConfig();

    private void SaveConfig()
    {
        try
        {
            SaveConfigToFile();
            MessageBox.Show("Configuration saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelSettingsConfig_Click(object sender, RoutedEventArgs e)
    {
        LoadConfigFromFile();
        MessageBox.Show("Changes canceled and previous configuration restored.", "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveConfigToFile()
    {
        var config = new ConfigWrapper
        {
            AntennaConfigs = [.._antennaConfigs],
            Settings = _settings
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(config, options);
        File.WriteAllText("config.json", jsonString);
    }

    private void LoadConfigFromFile()
    {
        if (!File.Exists("config.json"))
        {
            SaveConfigToFile();
            return;
        }

        var jsonString = File.ReadAllText("config.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<ConfigWrapper>(jsonString, options);

        if (config?.AntennaConfigs != null)
        {
            _antennaConfigs.Clear();
            foreach (var antennaConfig in config.AntennaConfigs)
            {
                _antennaConfigs.Add(new AntennaConfig
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
                });
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
        }
    }

    private void CancelAntennaConfig_Click(object sender, RoutedEventArgs e)
    {
        LoadConfigFromFile();
        MessageBox.Show("Changes canceled and previous configuration restored.", "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private class ConfigWrapper
    {
        public List<AntennaConfig> AntennaConfigs { get; set; }
        public Settings Settings { get; set; }
    }

    public void Dispose()
    {
        _udpListener.Dispose();
        _udpMessageSender.Dispose();
        _fakeTs590Sg?.Dispose();
        _relayManager.Dispose();
        _updateSemaphore.Dispose();
    }
}
