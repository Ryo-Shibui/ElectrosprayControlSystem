using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class MeasurementSessionService
    {
        private readonly IDaqService _daqService;
        private readonly ICameraService _cameraService;

        public MeasurementSessionService(IDaqService daqService, ICameraService cameraService)
        {
            _daqService = daqService;
            _cameraService = cameraService;
        }

        public event EventHandler<AcquisitionSample> SampleAcquired;
        public event EventHandler<string> StatusChanged;

        public async Task<IReadOnlyList<AcquisitionSample>> RunAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(settings.SaveFolderPath);
            var samples = new List<AcquisitionSample>();
            string sessionFolder = Path.Combine(settings.SaveFolderPath, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(sessionFolder);

            string csvPath = Path.Combine(sessionFolder, "daq_samples.csv");
            string metadataPath = Path.Combine(sessionFolder, "metadata.txt");
            string midpointImagePath = Path.Combine(sessionFolder, "snapshot_midpoint.png");

            double controlVoltage = settings.ComputeControlVoltage();
            bool midpointCaptured = false;
            double midpointTime = settings.TotalSamplingTimeSeconds * 0.5;

            await _daqService.InitializeAsync(settings, cancellationToken);
            await _daqService.SetControlVoltageAsync(controlVoltage, cancellationToken);
            StatusChanged?.Invoke(this, $"Output enabled at {controlVoltage:F3} V control voltage.");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                while (stopwatch.Elapsed.TotalSeconds <= settings.TotalSamplingTimeSeconds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    DaqReading reading = await _daqService.ReadMonitorsAsync(cancellationToken);
                    var sample = new AcquisitionSample
                    {
                        TimestampUtc = DateTime.UtcNow,
                        ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                        ControlVoltageV = controlVoltage,
                        VmoniRawV = reading.VmoniV,
                        ImoniRawV = reading.ImoniV,
                        VoltageScaled = settings.ConvertVmoniToVoltage(reading.VmoniV),
                        CurrentScaled = settings.ConvertImoniToCurrent(reading.ImoniV)
                    };

                    samples.Add(sample);
                    SampleAcquired?.Invoke(this, sample);

                    if (!midpointCaptured && sample.ElapsedSeconds >= midpointTime)
                    {
                        await _cameraService.SaveStillAsync(midpointImagePath, cancellationToken);
                        midpointCaptured = true;
                        StatusChanged?.Invoke(this, $"Midpoint snapshot saved: {midpointImagePath}");
                    }

                    await Task.Delay(settings.SampleIntervalMilliseconds, cancellationToken);
                }
            }
            finally
            {
                try
                {
                    await _daqService.SetControlVoltageAsync(0.0, CancellationToken.None);
                }
                catch
                {
                }

                await _daqService.ShutdownAsync(CancellationToken.None);
            }

            await SaveCsvAsync(csvPath, samples, cancellationToken);
            await SaveMetadataAsync(metadataPath, settings, controlVoltage, midpointImagePath, csvPath, cancellationToken);
            StatusChanged?.Invoke(this, $"Session completed. Data saved under {sessionFolder}");
            return samples;
        }

        private static Task SaveCsvAsync(string csvPath, IEnumerable<AcquisitionSample> samples, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine("timestamp_utc,elapsed_s,control_voltage_v,vmoni_raw_v,imoni_raw_v,voltage_scaled,current_scaled");
            foreach (AcquisitionSample s in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"{s.TimestampUtc:O},{s.ElapsedSeconds:F6},{s.ControlVoltageV:F6},{s.VmoniRawV:F6},{s.ImoniRawV:F6},{s.VoltageScaled:F6},{s.CurrentScaled:F6}");
            }
            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            return Task.CompletedTask;
        }

        private static Task SaveMetadataAsync(
            string metadataPath,
            MeasurementSettings settings,
            double controlVoltage,
            string midpointImagePath,
            string csvPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sb = new StringBuilder();
            sb.AppendLine($"created_local={DateTime.Now:O}");
            sb.AppendLine($"daq_backend=NI-DAQmx or fallback");
            sb.AppendLine($"hv_min_kv={settings.HvMinimumKv}");
            sb.AppendLine($"hv_max_kv={settings.HvMaximumKv}");
            sb.AppendLine($"control_signal_max_v={settings.ControlSignalMaximumV}");
            sb.AppendLine($"target_applied_voltage_kv={settings.TargetAppliedVoltageKv}");
            sb.AppendLine($"computed_control_voltage_v={controlVoltage}");
            sb.AppendLine($"total_sampling_time_s={settings.TotalSamplingTimeSeconds}");
            sb.AppendLine($"sample_interval_ms={settings.SampleIntervalMilliseconds}");
            sb.AppendLine($"daq_device={settings.DaqDeviceName}");
            sb.AppendLine($"ao_channel={settings.AnalogOutputChannel}");
            sb.AppendLine($"vmoni_channel={settings.VmoniChannel}");
            sb.AppendLine($"imoni_positive_channel={settings.ImoniPositiveChannel}");
            sb.AppendLine($"imoni_negative_channel={settings.ImoniNegativeChannel}");
            sb.AppendLine($"vmoni_scale={settings.VmoniScale}");
            sb.AppendLine($"vmoni_offset={settings.VmoniOffset}");
            sb.AppendLine($"imoni_scale={settings.ImoniScale}");
            sb.AppendLine($"imoni_offset={settings.ImoniOffset}");
            sb.AppendLine($"preferred_camera_name={settings.PreferredCameraName}");
            sb.AppendLine($"csv_path={csvPath}");
            sb.AppendLine($"midpoint_image_path={midpointImagePath}");
            File.WriteAllText(metadataPath, sb.ToString(), Encoding.UTF8);
            return Task.CompletedTask;
        }
    }
}
