using System;
using System.Windows;

namespace AntennaSwitchWPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly UdpListener _udpListener;
    private readonly BandDecoder _bandDecoder;

    public MainWindow()
    {
        InitializeComponent();
        _bandDecoder = new BandDecoder();
        _udpListener = new UdpListener();
        _udpListener.RxFrequencyReceived += OnRxFrequencyReceived;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _udpListener.StartListeningAsync();
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _udpListener.Stop();
    }

    private void OnRxFrequencyReceived(object? sender, string? frequency)
    {
        Dispatcher.Invoke(() =>
        {
            _bandDecoder.DecodeBand(frequency);
            UpdateAntennaSelection(_bandDecoder.BandNumber);
        });
    }

    private void UpdateAntennaSelection(int bandNumber)
    {
        // TODO: Implement antenna selection logic based on bandNumber
        // This is where you would add code to control your antenna switch
        // For now, we'll just update a label with the band number
        BandLabel.Content = $"Current Band: {bandNumber}";
    }
    private void UpdateRxFrequency(string frequency)
    {
        RxFrequencyLabel.Content = frequency;
    }

    private void UpdateTxFrequency(string frequency)
    {
        TxFrequencyLabel.Content = frequency;
    }
}
