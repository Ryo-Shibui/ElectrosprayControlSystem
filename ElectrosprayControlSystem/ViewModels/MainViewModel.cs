using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ScottPlot;
using ScottPlot.WPF;
using ElectrosprayControlSystem.Models;
using ElectrosprayControlSystem.Services;
using ElectrosprayControlSystem.Utilities;
using Forms = System.Windows.Forms;

namespace ElectrosprayControlSystem.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private const int MaxTrendSamples = 200;

        private readonly IDaqService _daqService;
        private readonly ICameraService _cameraService;
        private readonly ObservableCollection<AcquisitionSample> _recentSamples = new ObservableCollection<AcquisitionSample>();
        private readonly object _measurementSync = new object();

        private CancellationTokenSource _monitorCts;
        private CancellationTokenSource _previewCts;
        private CancellationTokenSource _measurementCts;
        private Task _monitorLoopTask;
        private Task _previewLoopTask;
        private Task _measurementTask;

        private string _statusMessage;
        private double _latestVoltageScaled;
        private double _latestCurrentScaled;
        private double _latestVmoniRaw;
        private double _latestImoniRaw;
        private double _monitorElapsedSeconds;
        private int _measurementSampleCount;
        private bool _isApplying;
        private bool _isMeasuring;
        private bool _isLightOn;
        private BitmapSource _currentPreviewFrame;
        private WpfPlot _voltagePlotControl;
        private WpfPlot _currentPlotControl;
        private string _hardwareSummary;
        private string _previewStatusText;
        private string _availableCameraText;
        private double _appliedControlVoltage;
        private double _appliedTargetVoltageKv;
        private DateTime _monitorStartUtc;
        private bool _daqInitialized;
        private bool _isShuttingDown;
        private MeasurementCaptureState _activeMeasurement;

        public MainViewModel()
        {
            Settings = new MeasurementSettings();
            Settings.PropertyChanged += (_, __) => RefreshComputedOutputs();

            _daqService = HardwareFactory.CreateDaqService();
            _cameraService = HardwareFactory.CreateCameraService();
            Pump = new SyringePumpViewModel();

            ApplyCommand = new RelayCommand(async () => await ApplyVoltageAsync());
            StopApplyCommand = new RelayCommand(async () => await StopApplyAsync(), () => IsApplying);
            MeasureCommand = new RelayCommand(async () => await StartMeasurementAsync(), () => !IsMeasuring);
            StopMeasureCommand = new RelayCommand(async () => await StopMeasurementAsync(), () => IsMeasuring);
            ToggleLightCommand = new RelayCommand(async () => await ToggleLightAsync());
            BrowsePathCommand = new RelayCommand(BrowseForSaveFolder);
            RefreshHardwareCommand = new RelayCommand(async () => await RefreshHardwareAsync());

            RefreshComputedOutputs();
            RefreshCameraListSummary();
            PreviewStatusText = "camera not started";
            UpdateHardwareSummary();
            StatusMessage = "Ready. Live Vmoni/Imoni monitoring will start when the window opens.";
        }

        public MeasurementSettings Settings { get; }
        public SyringePumpViewModel Pump { get; }
        public RelayCommand ApplyCommand { get; }
        public RelayCommand StopApplyCommand { get; }
        public RelayCommand MeasureCommand { get; }
        public RelayCommand StopMeasureCommand { get; }
        public RelayCommand ToggleLightCommand { get; }
        public RelayCommand BrowsePathCommand { get; }
        public RelayCommand RefreshHardwareCommand { get; }

        public string ComputedControlVoltageText => $"{Settings.ComputeControlVoltage():F3}";
        public string AppliedStateText => IsApplying
            ? $"Control output active at {_appliedControlVoltage:F3} V (target HV {_appliedTargetVoltageKv:F3} kV)."
            : "Control output is 0 V.";
        public string MeasurementStateText => BuildMeasurementStateText();

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string HardwareSummary
        {
            get => _hardwareSummary;
            set => SetProperty(ref _hardwareSummary, value);
        }

        public string PreviewStatusText
        {
            get => _previewStatusText;
            set => SetProperty(ref _previewStatusText, value);
        }

        public string AvailableCameraText
        {
            get => _availableCameraText;
            set => SetProperty(ref _availableCameraText, value);
        }

        public string LatestVoltageText => _latestVoltageScaled.ToString("F4");
        public string LatestVoltageSubText => $"raw Vmoni = {_latestVmoniRaw:F4} V";
        public string LatestCurrentText => _latestCurrentScaled.ToString("F4");
        public string LatestCurrentSubText => $"raw Imoni = {_latestImoniRaw:F4} V";
        public string MonitorElapsedSecondsText => _monitorElapsedSeconds.ToString("F2");
        public string MeasurementSampleCountText => _measurementSampleCount.ToString();
        public string LightToggleButtonText => _isLightOn ? "Light Off" : "Light On";

        public bool IsLightOn
        {
            get => _isLightOn;
            set
            {
                if (SetProperty(ref _isLightOn, value))
                {
                    OnPropertyChanged(nameof(LightToggleButtonText));
                    UpdateHardwareSummary();
                }
            }
        }

        public bool IsApplying
        {
            get => _isApplying;
            set
            {
                if (SetProperty(ref _isApplying, value))
                {
                    StopApplyCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(AppliedStateText));
                    UpdateHardwareSummary();
                }
            }
        }

        public bool IsMeasuring
        {
            get => _isMeasuring;
            set
            {
                if (SetProperty(ref _isMeasuring, value))
                {
                    MeasureCommand.RaiseCanExecuteChanged();
                    StopMeasureCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(MeasurementStateText));
                    UpdateHardwareSummary();
                }
            }
        }

        public BitmapSource CurrentPreviewFrame
        {
            get => _currentPreviewFrame;
            set => SetProperty(ref _currentPreviewFrame, value);
        }

        public void AttachPlots(WpfPlot voltagePlotControl, WpfPlot currentPlotControl)
        {
            _voltagePlotControl = voltagePlotControl;
            _currentPlotControl = currentPlotControl;

            ConfigurePlot(_voltagePlotControl, "Time [s]", "Applied voltage [V]", Colors.DodgerBlue);
            ConfigurePlot(_currentPlotControl, "Time [s]", "Current [nA]", Colors.FireBrick);
            RefreshTrendPlots();
        }

        public async Task InitializeAsync()
        {
            try
            {
                await EnsureMonitorLoopAsync();
                StatusMessage = "Live DAQ monitoring started.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"DAQ monitor start error: {ex.Message}";
                MessageBox.Show(ex.ToString(), "DAQ initialization error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            try
            {
                await EnsurePreviewLoopAsync();
            }
            catch (Exception ex)
            {
                PreviewStatusText = "camera not started";
                StatusMessage = $"DAQ monitoring may be active, but camera preview failed: {ex.Message}";
            }
        }

        public async Task ShutdownAsync()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;

            try
            {
                if (IsMeasuring)
                {
                    await StopMeasurementAsync();
                }

                await StopApplySilentlyAsync();

                if (_monitorCts != null)
                {
                    _monitorCts.Cancel();
                    try
                    {
                        if (_monitorLoopTask != null)
                        {
                            await _monitorLoopTask;
                        }
                    }
                    catch
                    {
                    }
                    _monitorCts.Dispose();
                    _monitorCts = null;
                }

                if (_previewCts != null)
                {
                    _previewCts.Cancel();
                    try
                    {
                        if (_previewLoopTask != null)
                        {
                            await _previewLoopTask;
                        }
                    }
                    catch
                    {
                    }
                    _previewCts.Dispose();
                    _previewCts = null;
                }

                try
                {
                    await _cameraService.ShutdownAsync(CancellationToken.None);
                }
                catch
                {
                }

                try
                {
                    await _daqService.ShutdownAsync(CancellationToken.None);
                }
                catch
                {
                }

                try
                {
                    Pump?.Dispose();
                }
                catch
                {
                }
            }
            finally
            {
                _isShuttingDown = false;
            }
        }

        private void ConfigurePlot(WpfPlot plotControl, string xLabel, string yLabel, Color lineColor, double[] xs = null, double[] ys = null)
        {
            if (plotControl == null)
            {
                return;
            }

            xs ??= new[] { 0.0 };
            ys ??= new[] { 0.0 };

            lock (plotControl.Plot.Sync)
            {
                plotControl.Plot.Clear();
                plotControl.Plot.XLabel(xLabel);
                plotControl.Plot.YLabel(yLabel);
                plotControl.Plot.Axes.Margins(0.05, 0.15);
                plotControl.Plot.Grid.IsVisible = true;
                plotControl.Plot.Legend.IsVisible = false;

                var scatter = plotControl.Plot.Add.Scatter(xs, ys, lineColor);
                scatter.LineWidth = 2;
                scatter.MarkerSize = 0;
                plotControl.Plot.Axes.AutoScale();
            }

            plotControl.Refresh();
        }

        private void RefreshTrendPlots()
        {
            RefreshPlot(_voltagePlotControl, sample => sample.VoltageScaled, "Applied voltage [V]", Colors.DodgerBlue);
            RefreshPlot(_currentPlotControl, sample => sample.CurrentScaled, "Current [nA]", Colors.FireBrick);
        }

        private void RefreshPlot(WpfPlot plotControl, Func<AcquisitionSample, double> selector, string yLabel, Color lineColor)
        {
            if (plotControl == null)
            {
                return;
            }

            double[] xs;
            double[] ys;

            if (_recentSamples.Count == 0)
            {
                xs = new[] { 0.0 };
                ys = new[] { 0.0 };
            }
            else
            {
                xs = _recentSamples.Select(sample => sample.ElapsedSeconds).ToArray();
                ys = _recentSamples.Select(selector).ToArray();
            }

            ConfigurePlot(plotControl, "Time [s]", yLabel, lineColor, xs, ys);
        }

        private async Task EnsureMonitorLoopAsync()
        {
            if (_monitorLoopTask != null)
            {
                return;
            }

            await _daqService.InitializeAsync(Settings, CancellationToken.None);
            _daqInitialized = true;
            _monitorStartUtc = DateTime.UtcNow;
            _monitorCts = new CancellationTokenSource();
            UpdateHardwareSummary();

            var dispatcher = Application.Current?.Dispatcher;

            _monitorLoopTask = Task.Run(async () =>
            {
                try
                {
                    while (_monitorCts != null && !_monitorCts.IsCancellationRequested)
                    {
                        DaqReading reading = await _daqService.ReadMonitorsAsync(_monitorCts.Token);
                        DateTime nowUtc = DateTime.UtcNow;
                        double monitorElapsed = (nowUtc - _monitorStartUtc).TotalSeconds;
                        double controlVoltage = _appliedControlVoltage;

                        var liveSample = new AcquisitionSample
                        {
                            TimestampUtc = nowUtc,
                            ElapsedSeconds = monitorElapsed,
                            ControlVoltageV = controlVoltage,
                            VmoniRawV = reading.VmoniV,
                            ImoniRawV = reading.ImoniV,
                            VoltageScaled = Settings.ConvertVmoniToVoltage(reading.VmoniV),
                            CurrentScaled = Settings.ConvertImoniToCurrent(reading.ImoniV)
                        };

                        MeasurementCaptureState measurementState = null;
                        bool shouldCaptureMidpoint = false;

                        lock (_measurementSync)
                        {
                            if (_activeMeasurement != null)
                            {
                                double measurementElapsed = (nowUtc - _activeMeasurement.StartUtc).TotalSeconds;
                                if (measurementElapsed >= 0.0 && measurementElapsed <= _activeMeasurement.DurationSeconds)
                                {
                                    var measurementSample = new AcquisitionSample
                                    {
                                        TimestampUtc = nowUtc,
                                        ElapsedSeconds = measurementElapsed,
                                        ControlVoltageV = controlVoltage,
                                        VmoniRawV = reading.VmoniV,
                                        ImoniRawV = reading.ImoniV,
                                        VoltageScaled = liveSample.VoltageScaled,
                                        CurrentScaled = liveSample.CurrentScaled
                                    };

                                    _activeMeasurement.Samples.Add(measurementSample);
                                    measurementState = _activeMeasurement;
                                    if (measurementElapsed >= _activeMeasurement.DurationSeconds * 0.5 &&
                                        Interlocked.Exchange(ref _activeMeasurement.MidpointCaptureTriggered, 1) == 0)
                                    {
                                        shouldCaptureMidpoint = true;
                                    }
                                }
                            }
                        }

                        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        {
                            await dispatcher.InvokeAsync(() => UpdateLiveUi(liveSample, measurementState));
                        }

                        if (shouldCaptureMidpoint && measurementState != null)
                        {
                            measurementState.SnapshotTask = CaptureMidpointSnapshotAsync(measurementState);
                        }

                        await Task.Delay(Math.Max(20, Settings.SampleIntervalMilliseconds), _monitorCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    {
                        await dispatcher.InvokeAsync(() =>
                        {
                            StatusMessage = $"DAQ monitor loop error: {ex.Message}";
                        });
                    }
                }
                finally
                {
                    _monitorLoopTask = null;
                }
            }, _monitorCts.Token);
        }

        private async Task EnsurePreviewLoopAsync()
        {
            if (_previewLoopTask != null)
            {
                return;
            }

            _previewCts = new CancellationTokenSource();
            try
            {
                await _cameraService.InitializeAsync(Settings, _previewCts.Token);

                IsLightOn = true;
            }
            catch
            {
                _previewCts.Dispose();
                _previewCts = null;
                throw;
            }

            RefreshCameraListSummary();
            PreviewStatusText = $"live: {_cameraService.ActiveDeviceName} | {_cameraService.BackendName}";
            UpdateHardwareSummary();

            var dispatcher = Application.Current?.Dispatcher;

            _previewLoopTask = Task.Run(async () =>
            {
                try
                {
                    while (_previewCts != null && !_previewCts.IsCancellationRequested)
                    {
                        BitmapSource frame = await _cameraService.GetPreviewFrameAsync(_previewCts.Token);

                        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                CurrentPreviewFrame = frame;
                            });
                        }

                        await Task.Delay(40, _previewCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    {
                        await dispatcher.InvokeAsync(() =>
                        {
                            PreviewStatusText = "preview error";
                            StatusMessage = $"Preview error: {ex.Message}";
                        });
                    }
                }
                finally
                {
                    _previewLoopTask = null;
                }
            }, _previewCts.Token);
        }
        private async Task ApplyVoltageAsync()
        {
            string validation = ValidateForApply();
            if (!string.IsNullOrEmpty(validation))
            {
                MessageBox.Show(validation, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await EnsureMonitorLoopAsync();
                double controlVoltage = Settings.ComputeControlVoltage();
                await _daqService.SetControlVoltageAsync(controlVoltage, CancellationToken.None);
                _appliedControlVoltage = controlVoltage;
                _appliedTargetVoltageKv = Settings.TargetAppliedVoltageKv;
                IsApplying = controlVoltage > 0.0;
                RefreshComputedOutputs();
                StatusMessage = $"Apply executed. Control output set to {controlVoltage:F3} V for target HV {Settings.TargetAppliedVoltageKv:F3} kV.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Apply error: {ex.Message}";
                MessageBox.Show(ex.ToString(), "Apply error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopApplyAsync()
        {
            try
            {
                await StopApplySilentlyAsync();
                StatusMessage = "Apply stopped. Control output returned to 0 V.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Stop Apply error: {ex.Message}";
                MessageBox.Show(ex.ToString(), "Stop Apply error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopApplySilentlyAsync()
        {
            if (!_daqInitialized)
            {
                _appliedControlVoltage = 0.0;
                _appliedTargetVoltageKv = 0.0;
                IsApplying = false;
                RefreshComputedOutputs();
                return;
            }

            await _daqService.SetControlVoltageAsync(0.0, CancellationToken.None);
            _appliedControlVoltage = 0.0;
            _appliedTargetVoltageKv = 0.0;
            IsApplying = false;
            RefreshComputedOutputs();
        }

        private async Task StartMeasurementAsync()
        {
            string validation = ValidateForMeasurement();
            if (!string.IsNullOrEmpty(validation))
            {
                MessageBox.Show(validation, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsMeasuring)
            {
                return;
            }

            try
            {
                await EnsureMonitorLoopAsync();
                await EnsurePreviewLoopAsync();
                Directory.CreateDirectory(Settings.SaveFolderPath);

                var session = CreateMeasurementSession();
                lock (_measurementSync)
                {
                    _activeMeasurement = session;
                }

                _measurementSampleCount = 0;
                OnPropertyChanged(nameof(MeasurementSampleCountText));
                OnPropertyChanged(nameof(MeasurementStateText));

                _measurementCts = new CancellationTokenSource();
                IsMeasuring = true;
                StatusMessage = $"Measurement started. Saving to {session.SessionFolder}";
                _measurementTask = RunMeasurementLifecycleAsync(session, _measurementCts.Token);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Measurement start error: {ex.Message}";
                MessageBox.Show(ex.ToString(), "Measurement start error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopMeasurementAsync()
        {
            if (!IsMeasuring)
            {
                return;
            }

            try
            {
                _measurementCts?.Cancel();
                if (_measurementTask != null)
                {
                    await _measurementTask;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Stop Measure error: {ex.Message}";
                MessageBox.Show(ex.ToString(), "Stop Measure error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RunMeasurementLifecycleAsync(MeasurementCaptureState session, CancellationToken cancellationToken)
        {
            bool interrupted = false;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(session.DurationSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                interrupted = true;
            }

            await FinalizeMeasurementAsync(session, interrupted);
        }

        private async Task CaptureMidpointSnapshotAsync(MeasurementCaptureState session)
        {
            try
            {
                await _cameraService.SaveStillAsync(session.MidpointImagePath, CancellationToken.None);
                session.MidpointSnapshotSaved = true;
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Midpoint snapshot saved: {session.MidpointImagePath}");
            }
            catch (Exception ex)
            {
                session.MidpointSnapshotError = ex.Message;
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Midpoint snapshot error: {ex.Message}");
            }
        }

        private async Task FinalizeMeasurementAsync(MeasurementCaptureState session, bool interrupted)
        {
            if (Interlocked.Exchange(ref session.FinalizeStarted, 1) != 0)
            {
                return;
            }

            lock (_measurementSync)
            {
                if (ReferenceEquals(_activeMeasurement, session))
                {
                    _activeMeasurement = null;
                }
            }

            if (session.SnapshotTask != null)
            {
                try
                {
                    await session.SnapshotTask;
                }
                catch
                {
                }
            }

            List<AcquisitionSample> capturedSamples;
            lock (_measurementSync)
            {
                capturedSamples = session.Samples.ToList();
            }
            await SaveMeasurementFilesAsync(session, capturedSamples, interrupted);

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsMeasuring = false;
                _measurementSampleCount = capturedSamples.Count;
                OnPropertyChanged(nameof(MeasurementSampleCountText));
                OnPropertyChanged(nameof(MeasurementStateText));
            });

            _measurementCts?.Dispose();
            _measurementCts = null;
            _measurementTask = null;

            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = interrupted
                    ? $"Measurement stopped. Partial data saved under {session.SessionFolder}"
                    : $"Measurement completed. Data saved under {session.SessionFolder}";
            });
        }

        private Task SaveMeasurementFilesAsync(MeasurementCaptureState session, IReadOnlyList<AcquisitionSample> samples, bool interrupted)
        {
            Directory.CreateDirectory(session.SessionFolder);
            File.WriteAllText(session.CsvPath, BuildCsv(samples), Encoding.UTF8);
            File.WriteAllText(session.MetadataPath, BuildMetadata(session, samples, interrupted), Encoding.UTF8);
            return Task.CompletedTask;
        }

        private static string BuildCsv(IEnumerable<AcquisitionSample> samples)
        {
            var sb = new StringBuilder();
            sb.AppendLine("timestamp_utc,elapsed_s,control_voltage_v,vmoni_raw_v,imoni_raw_v,voltage_scaled,current_scaled");
            foreach (AcquisitionSample sample in samples)
            {
                sb.AppendLine($"{sample.TimestampUtc:O},{sample.ElapsedSeconds:F6},{sample.ControlVoltageV:F6},{sample.VmoniRawV:F6},{sample.ImoniRawV:F6},{sample.VoltageScaled:F6},{sample.CurrentScaled:F6}");
            }
            return sb.ToString();
        }

        private string BuildMetadata(MeasurementCaptureState session, IReadOnlyList<AcquisitionSample> samples, bool interrupted)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"created_local={DateTime.Now:O}");
            sb.AppendLine($"interrupted={interrupted}");
            sb.AppendLine($"sample_count={samples.Count}");
            sb.AppendLine($"measurement_duration_s={session.DurationSeconds}");
            sb.AppendLine($"measurement_start_utc={session.StartUtc:O}");
            sb.AppendLine($"hv_min_kv={Settings.HvMinimumKv}");
            sb.AppendLine($"hv_max_kv={Settings.HvMaximumKv}");
            sb.AppendLine($"control_signal_max_v={Settings.ControlSignalMaximumV}");
            sb.AppendLine($"target_applied_voltage_kv={Settings.TargetAppliedVoltageKv}");
            sb.AppendLine($"control_voltage_at_measure_start_v={session.ControlVoltageAtStart}");
            sb.AppendLine($"output_was_active_at_measure_start={session.OutputWasActiveAtStart}");
            sb.AppendLine($"sample_interval_ms={Settings.SampleIntervalMilliseconds}");
            sb.AppendLine($"daq_device={Settings.DaqDeviceName}");
            sb.AppendLine($"ao_channel={Settings.AnalogOutputChannel}");
            sb.AppendLine($"vmoni_channel={Settings.VmoniChannel}");
            sb.AppendLine($"imoni_positive_channel={Settings.ImoniPositiveChannel}");
            sb.AppendLine($"imoni_negative_channel={Settings.ImoniNegativeChannel}");
            sb.AppendLine($"vmoni_scale={Settings.VmoniScale}");
            sb.AppendLine($"vmoni_offset={Settings.VmoniOffset}");
            sb.AppendLine($"imoni_scale={Settings.ImoniScale}");
            sb.AppendLine($"imoni_offset={Settings.ImoniOffset}");
            sb.AppendLine($"preferred_camera_name={Settings.PreferredCameraName}");
            sb.AppendLine($"camera_preview_zoom_factor={Settings.CameraPreviewZoomFactor}");
            sb.AppendLine($"camera_backend={_cameraService?.BackendName ?? string.Empty}");
            sb.AppendLine($"csv_path={session.CsvPath}");
            sb.AppendLine($"midpoint_image_path={session.MidpointImagePath}");
            sb.AppendLine($"midpoint_snapshot_saved={session.MidpointSnapshotSaved}");
            sb.AppendLine($"midpoint_snapshot_error={session.MidpointSnapshotError ?? string.Empty}");
            return sb.ToString();
        }

        private MeasurementCaptureState CreateMeasurementSession()
        {
            string sessionFolder = Path.Combine(Settings.SaveFolderPath, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
            return new MeasurementCaptureState
            {
                SessionFolder = sessionFolder,
                CsvPath = Path.Combine(sessionFolder, "daq_samples.csv"),
                MetadataPath = Path.Combine(sessionFolder, "metadata.txt"),
                MidpointImagePath = Path.Combine(sessionFolder, "snapshot_midpoint.png"),
                StartUtc = DateTime.UtcNow,
                DurationSeconds = Settings.TotalSamplingTimeSeconds,
                ControlVoltageAtStart = _appliedControlVoltage,
                OutputWasActiveAtStart = IsApplying
            };
        }

        private void UpdateLiveUi(AcquisitionSample liveSample, MeasurementCaptureState measurementState)
        {
            _latestVmoniRaw = liveSample.VmoniRawV;
            _latestImoniRaw = liveSample.ImoniRawV;
            _latestVoltageScaled = liveSample.VoltageScaled;
            _latestCurrentScaled = liveSample.CurrentScaled;
            _monitorElapsedSeconds = liveSample.ElapsedSeconds;
            _recentSamples.Add(liveSample);
            while (_recentSamples.Count > MaxTrendSamples)
            {
                _recentSamples.RemoveAt(0);
            }

            _measurementSampleCount = measurementState?.Samples.Count ?? _measurementSampleCount;
            RefreshTrendPlots();
            OnPropertyChanged(nameof(LatestVoltageText));
            OnPropertyChanged(nameof(LatestVoltageSubText));
            OnPropertyChanged(nameof(LatestCurrentText));
            OnPropertyChanged(nameof(LatestCurrentSubText));
            OnPropertyChanged(nameof(MonitorElapsedSecondsText));
            OnPropertyChanged(nameof(MeasurementSampleCountText));
            OnPropertyChanged(nameof(MeasurementStateText));
        }

        private void RefreshComputedOutputs()
        {
            OnPropertyChanged(nameof(ComputedControlVoltageText));
            OnPropertyChanged(nameof(AppliedStateText));
            RefreshCameraListSummary();
        }

        private void RefreshCameraListSummary()
        {
            IReadOnlyList<CameraDeviceInfo> cameras = _cameraService.GetAvailableDevices();
            AvailableCameraText = cameras.Count == 0
                ? "detected cameras: 0"
                : $"detected cameras: {cameras.Count} | preferred filter: {Settings.PreferredCameraName} | backend priority: Dino-Lite SDK -> DirectShow fallback";
        }

        private void UpdateHardwareSummary()
        {
            string cameraName = _cameraService?.ActiveDeviceName ?? "not connected";
            string cameraBackend = _cameraService?.BackendName ?? "not initialized";
            string daqName = _daqService?.BackendName ?? "not initialized";
            string applyText = IsApplying ? $"{_appliedControlVoltage:F3} V" : "0.000 V";
            string measureText = IsMeasuring ? "recording" : "idle";
            string lightText = _cameraService?.SupportsLightControl == true ? (_isLightOn ? "on" : "off") : "n/a";
            string pumpText = Pump?.ConnectionText ?? "not connected";
            HardwareSummary = $"DAQ: {daqName} | Camera: {cameraName} | Camera backend: {cameraBackend} | Light: {lightText} | Pump: {pumpText} | AO: {applyText} | Measure: {measureText}";
        }

        private string BuildMeasurementStateText()
        {
            lock (_measurementSync)
            {
                if (_activeMeasurement == null)
                {
                    return "No measurement recording is active.";
                }

                return $"Recording to {_activeMeasurement.SessionFolder}";
            }
        }

        private string ValidateForApply()
        {
            if (Settings.HvMaximumKv <= Settings.HvMinimumKv)
                return "HV maximum must be larger than HV minimum.";
            if (Settings.ControlSignalMaximumV <= 0.0)
                return "Control signal maximum must be positive.";
            if (Settings.ControlSignalMaximumV > 10.0)
                return "USB-6001 analog output must stay within ±10 V.";
            if (TargetOutsideRange())
                return "Target voltage must lie within the HV module range.";
            if (string.IsNullOrWhiteSpace(Settings.DaqDeviceName))
                return "DAQ device name is required.";
            if (string.IsNullOrWhiteSpace(Settings.AnalogOutputChannel))
                return "Analog output channel is required.";
            if (string.IsNullOrWhiteSpace(Settings.VmoniChannel) || string.IsNullOrWhiteSpace(Settings.ImoniPositiveChannel))
                return "Both analog input channels are required.";
            if (string.IsNullOrWhiteSpace(Settings.ImoniNegativeChannel))
                return "Imoni differential pair must use ai0/ai4, ai1/ai5, ai2/ai6, or ai3/ai7.";
            return string.Empty;
        }

        private string ValidateForMeasurement()
        {
            string applyValidation = ValidateForApply();
            if (!string.IsNullOrEmpty(applyValidation))
            {
                return applyValidation;
            }
            if (Settings.TotalSamplingTimeSeconds <= 0.0)
                return "Measurement duration must be positive.";
            if (Settings.SampleIntervalMilliseconds < 20)
                return "Sample interval should be 20 ms or longer for stable monitoring.";
            if (string.IsNullOrWhiteSpace(Settings.SaveFolderPath))
                return "Save folder path is required.";
            if (Settings.CameraPreviewZoomFactor < 1.0 || Settings.CameraPreviewZoomFactor > 4.0)
                return "Preview zoom factor must be between 1.0 and 4.0.";
            return string.Empty;
        }

        private bool TargetOutsideRange()
        {
            return Settings.TargetAppliedVoltageKv < Settings.HvMinimumKv ||
                   Settings.TargetAppliedVoltageKv > Settings.HvMaximumKv;
        }


        private async Task ToggleLightAsync()
        {
            try
            {
                if (_cameraService == null)
                {
                    StatusMessage = "Camera service is not available.";
                    return;
                }

                if (!_cameraService.SupportsLightControl)
                {
                    StatusMessage = "Light control is not available with the current camera backend.";
                    return;
                }

                bool nextState = !IsLightOn;
                await _cameraService.SetLightEnabledAsync(nextState, CancellationToken.None);
                IsLightOn = nextState;
                UpdateHardwareSummary();
                StatusMessage = IsLightOn
                    ? "Dino-Lite light turned on."
                    : "Dino-Lite light turned off.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to change Dino-Lite light state: {ex.Message}";
            }
        }

        private async Task RefreshHardwareAsync()
        {
            var messages = new List<string>();

            try
            {
                await EnsureMonitorLoopAsync();
                messages.Add("DAQ monitor ready");
            }
            catch (Exception ex)
            {
                messages.Add($"DAQ unavailable: {ex.Message}");
            }

            try
            {
                RefreshCameraListSummary();
                await EnsurePreviewLoopAsync();
                messages.Add("camera preview ready");
            }
            catch (Exception ex)
            {
                PreviewStatusText = "camera not started";
                messages.Add($"camera unavailable: {ex.Message}");
            }

            try
            {
                Pump.RefreshPorts();
                messages.Add(Pump.AvailablePorts.Count == 0
                    ? "pump COM ports: none"
                    : $"pump COM ports: {Pump.AvailablePorts.Count}");
            }
            catch (Exception ex)
            {
                messages.Add($"pump refresh error: {ex.Message}");
            }

            UpdateHardwareSummary();
            StatusMessage = "Refresh: " + string.Join(" | ", messages);
        }

        private void BrowseForSaveFolder()
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a folder for measurement data and Dino-Lite snapshots";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(Settings.SaveFolderPath) && Directory.Exists(Settings.SaveFolderPath))
                {
                    dialog.SelectedPath = Settings.SaveFolderPath;
                }

                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    Settings.SaveFolderPath = dialog.SelectedPath;
                    StatusMessage = $"Save folder set to {dialog.SelectedPath}";
                }
            }
        }

        private sealed class MeasurementCaptureState
        {
            public string SessionFolder { get; set; }
            public string CsvPath { get; set; }
            public string MetadataPath { get; set; }
            public string MidpointImagePath { get; set; }
            public DateTime StartUtc { get; set; }
            public double DurationSeconds { get; set; }
            public double ControlVoltageAtStart { get; set; }
            public bool OutputWasActiveAtStart { get; set; }
            public List<AcquisitionSample> Samples { get; } = new List<AcquisitionSample>();
            public bool MidpointSnapshotSaved { get; set; }
            public string MidpointSnapshotError { get; set; }
            public int MidpointCaptureTriggered;
            public int FinalizeStarted;
            public Task SnapshotTask { get; set; }
        }
    }
}
