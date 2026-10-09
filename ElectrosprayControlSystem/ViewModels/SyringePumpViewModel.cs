using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ElectrosprayControlSystem.Services.SyringePump;
using ElectrosprayControlSystem.Utilities;

namespace ElectrosprayControlSystem.ViewModels
{
    public sealed class SyringePumpViewModel : ViewModelBase, IDisposable
    {
        private readonly ISerialTransport _transport;
        private readonly Ne1000Controller _controller;
        private bool _isConnected;
        private bool _isBusy;
        private bool _isSettingsExpanded;
        private string _selectedPortName;
        private int _baudRate = 19200;
        private string _address = "00";
        private double _syringeDiameterMm = 10.2;
        private double _flowRate = 100.0;
        private Ne1000RateUnit _selectedRateUnit;
        private double _volume = 0.0;
        private bool _continuous = true;
        private bool _isInfuse = true;
        private string _pumpStateText = "idle";
        private string _dispensedText = "-";
        private string _alarmText = "None";
        private string _statusText = "Pump not connected.";
        private string _communicationLog = string.Empty;

        public SyringePumpViewModel()
            : this(new SerialTransport())
        {
        }

        public SyringePumpViewModel(ISerialTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _controller = new Ne1000Controller(_transport);

            RateUnits = new ObservableCollection<Ne1000RateUnit>
            {
                Ne1000RateUnit.MicrolitersPerHour,
                Ne1000RateUnit.MicrolitersPerMinute,
                Ne1000RateUnit.MillilitersPerHour,
                Ne1000RateUnit.MillilitersPerMinute
            };
            _selectedRateUnit = RateUnits.First();

            RefreshPortsCommand = new RelayCommand(RefreshPorts);
            ConnectCommand = new RelayCommand(async () => await ToggleConnectionAsync(), () => !IsBusy);
            StartCommand = new RelayCommand(async () => await StartAsync(), () => IsConnected && !IsBusy);
            StopCommand = new RelayCommand(async () => await StopAsync(), () => IsConnected && !IsBusy);
            PurgeCommand = new RelayCommand(async () => await PurgeAsync(), () => IsConnected && !IsBusy);
            RefreshStatusCommand = new RelayCommand(async () => await RefreshStatusAsync(), () => IsConnected && !IsBusy);

            RefreshPorts();
        }

        public ObservableCollection<string> AvailablePorts { get; } = new ObservableCollection<string>();
        public ObservableCollection<Ne1000RateUnit> RateUnits { get; }
        public RelayCommand RefreshPortsCommand { get; }
        public RelayCommand ConnectCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand PurgeCommand { get; }
        public RelayCommand RefreshStatusCommand { get; }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionText));
                    OnPropertyChanged(nameof(ConnectButtonText));
                    RaiseCommandStates();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool IsSettingsExpanded
        {
            get => _isSettingsExpanded;
            set => SetProperty(ref _isSettingsExpanded, value);
        }

        public string SelectedPortName
        {
            get => _selectedPortName;
            set => SetProperty(ref _selectedPortName, value);
        }

        public int BaudRate
        {
            get => _baudRate;
            set => SetProperty(ref _baudRate, value);
        }

        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        public double SyringeDiameterMm
        {
            get => _syringeDiameterMm;
            set => SetProperty(ref _syringeDiameterMm, value);
        }

        public double FlowRate
        {
            get => _flowRate;
            set => SetProperty(ref _flowRate, value);
        }

        public Ne1000RateUnit SelectedRateUnit
        {
            get => _selectedRateUnit;
            set => SetProperty(ref _selectedRateUnit, value);
        }

        public double Volume
        {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        public bool Continuous
        {
            get => _continuous;
            set => SetProperty(ref _continuous, value);
        }

        public bool IsInfuse
        {
            get => _isInfuse;
            set
            {
                if (SetProperty(ref _isInfuse, value))
                {
                    OnPropertyChanged(nameof(DirectionText));
                }
            }
        }

        public string PumpStateText
        {
            get => _pumpStateText;
            private set => SetProperty(ref _pumpStateText, value);
        }

        public string DispensedText
        {
            get => _dispensedText;
            private set => SetProperty(ref _dispensedText, value);
        }

        public string AlarmText
        {
            get => _alarmText;
            private set => SetProperty(ref _alarmText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string CommunicationLog
        {
            get => _communicationLog;
            private set => SetProperty(ref _communicationLog, value);
        }

        public string ConnectionText => IsConnected
            ? $"Connected: {_transport.PortName}"
            : "Not connected";

        public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";
        public string DirectionText => IsInfuse ? "Infuse" : "Withdraw";

        public void RefreshPorts()
        {
            string previous = SelectedPortName;
            AvailablePorts.Clear();

            foreach (string port in _transport.GetAvailablePortNames())
            {
                AvailablePorts.Add(port);
            }

            if (!string.IsNullOrWhiteSpace(previous) && AvailablePorts.Contains(previous))
            {
                SelectedPortName = previous;
            }
            else
            {
                SelectedPortName = AvailablePorts.FirstOrDefault();
            }

            StatusText = AvailablePorts.Count == 0
                ? "No COM ports detected. Connect the USB-RS232 adapter, then press Refresh."
                : $"Detected {AvailablePorts.Count} COM port(s).";
        }

        public string ValidateStartSettings()
        {
            if (SyringeDiameterMm <= 0.0)
            {
                return "Syringe diameter must be positive.";
            }

            if (FlowRate <= 0.0)
            {
                return "Flow rate must be positive.";
            }

            if (SelectedRateUnit == null)
            {
                return "Flow rate unit is required.";
            }

            if (!Continuous && Volume <= 0.0)
            {
                return "Volume must be positive, or Continuous must be checked.";
            }

            return string.Empty;
        }

        public async Task ToggleConnectionAsync()
        {
            if (IsConnected)
            {
                _transport.Disconnect();
                IsConnected = false;
                PumpStateText = "idle";
                StatusText = "Pump disconnected.";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedPortName))
            {
                StatusText = "Select a COM port before connecting.";
                return;
            }

            await RunPumpOperationAsync(async token =>
            {
                await _transport.ConnectAsync(SelectedPortName, BaudRate, token);
                IsConnected = true;
                StatusText = $"Pump serial port opened on {SelectedPortName}.";
                try
                {
                    string status = await SendLoggedAsync("DIS", token);
                    DispensedText = status;
                    StatusText = $"Pump connected. {status}";
                }
                catch (Exception ex)
                {
                    StatusText = $"Serial port opened, but pump did not answer DIS: {ex.Message}";
                }
            }, disconnectOnError: true);
        }

        public async Task StartAsync()
        {
            string validation = ValidateStartSettings();
            if (!string.IsNullOrEmpty(validation))
            {
                StatusText = validation;
                MessageBox.Show(validation, "Invalid pump settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RunPumpOperationAsync(async token =>
            {
                await ConfigurePumpAsync(token);
                string response = await SendLoggedAsync("RUN", token);
                PumpStateText = "running";
                StatusText = $"Pump started. {response}";
                await RefreshStatusCoreAsync(token);
            });
        }

        public async Task StopAsync()
        {
            await RunPumpOperationAsync(async token =>
            {
                string response = await SendLoggedAsync("STP", token);
                PumpStateText = "stopped";
                StatusText = $"Pump stopped. {response}";
                await RefreshStatusCoreAsync(token);
            });
        }

        public async Task PurgeAsync()
        {
            string validation = ValidateStartSettings();
            if (!string.IsNullOrEmpty(validation))
            {
                StatusText = validation;
                MessageBox.Show(validation, "Invalid pump settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RunPumpOperationAsync(async token =>
            {
                await ConfigurePumpAsync(token);
                string response = await SendLoggedAsync("PUR", token);
                PumpStateText = "purging";
                StatusText = $"Purge started. Press Stop to end purge. {response}";
            });
        }

        public Task RefreshStatusAsync()
        {
            return RunPumpOperationAsync(RefreshStatusCoreAsync);
        }

        private async Task ConfigurePumpAsync(CancellationToken cancellationToken)
        {
            await SendLoggedAsync("DIA " + SyringeDiameterMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            await SendLoggedAsync("RAT " + FlowRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " + SelectedRateUnit.CommandCode, cancellationToken);
            await SendLoggedAsync("VOL " + (Continuous ? "0" : Volume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
            await SendLoggedAsync(IsInfuse ? "DIR INF" : "DIR WDR", cancellationToken);
        }

        private async Task RefreshStatusCoreAsync(CancellationToken cancellationToken)
        {
            string response = await SendLoggedAsync("DIS", cancellationToken);
            DispensedText = response;
            AlarmText = response.IndexOf("A", StringComparison.OrdinalIgnoreCase) >= 0
                ? response
                : "None";
        }

        private async Task<string> SendLoggedAsync(string command, CancellationToken cancellationToken)
        {
            AppendLog(">> " + command);
            string response = await _controller.SendAsync(command, cancellationToken);
            AppendLog("<< " + response);
            return response;
        }

        private async Task RunPumpOperationAsync(Func<CancellationToken, Task> operation, bool disconnectOnError = false)
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await operation(CancellationToken.None);
            }
            catch (Exception ex)
            {
                StatusText = $"Pump error: {ex.Message}";
                AppendLog("!! " + ex.Message);
                if (disconnectOnError)
                {
                    _transport.Disconnect();
                    IsConnected = false;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void AppendLog(string line)
        {
            string next = string.IsNullOrEmpty(CommunicationLog)
                ? line
                : CommunicationLog + Environment.NewLine + line;

            if (next.Length > 8000)
            {
                next = next.Substring(next.Length - 8000);
            }

            CommunicationLog = next;
        }

        private void RaiseCommandStates()
        {
            ConnectCommand.RaiseCanExecuteChanged();
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            PurgeCommand.RaiseCanExecuteChanged();
            RefreshStatusCommand.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
