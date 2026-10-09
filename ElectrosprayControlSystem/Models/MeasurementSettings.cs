using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace ElectrosprayControlSystem.Models
{
    public class MeasurementSettings : INotifyPropertyChanged
    {
        private double _hvMinimumKv = 0.0;
        private double _hvMaximumKv = 6.0;
        private double _controlSignalMaximumV = 5.0;
        private double _targetAppliedVoltageKv = 1.0;
        private double _totalSamplingTimeSeconds = 30.0;
        private int _sampleIntervalMilliseconds = 50;
        private string _saveFolderPath = GetDefaultSaveFolderPath();
        private string _daqDeviceName = "Dev3";
        private string _analogOutputChannel = "ao0";
        private string _vmoniChannel = "ai3";
        private string _imoniPositiveChannel = "ai0";
        private double _vmoniScale = 1200.0;
        private double _vmoniOffset = 0.0;
        private double _imoniScale = 100.0;
        private double _imoniOffset = 0.0;
        private string _preferredCameraName = "Dino";
        private double _cameraPreviewZoomFactor = 2.0;

        public event PropertyChangedEventHandler PropertyChanged;

        public double HvMinimumKv { get => _hvMinimumKv; set => SetField(ref _hvMinimumKv, value); }
        public double HvMaximumKv { get => _hvMaximumKv; set => SetField(ref _hvMaximumKv, value); }
        public double ControlSignalMaximumV { get => _controlSignalMaximumV; set => SetField(ref _controlSignalMaximumV, value); }
        public double TargetAppliedVoltageKv { get => _targetAppliedVoltageKv; set => SetField(ref _targetAppliedVoltageKv, value); }
        public double TotalSamplingTimeSeconds { get => _totalSamplingTimeSeconds; set => SetField(ref _totalSamplingTimeSeconds, value); }
        public int SampleIntervalMilliseconds { get => _sampleIntervalMilliseconds; set => SetField(ref _sampleIntervalMilliseconds, value); }
        public string SaveFolderPath { get => _saveFolderPath; set => SetField(ref _saveFolderPath, value); }
        public string DaqDeviceName { get => _daqDeviceName; set => SetField(ref _daqDeviceName, value); }
        public string AnalogOutputChannel { get => _analogOutputChannel; set => SetField(ref _analogOutputChannel, value); }
        public string VmoniChannel { get => _vmoniChannel; set => SetField(ref _vmoniChannel, value); }

        public string ImoniPositiveChannel
        {
            get => _imoniPositiveChannel;
            set
            {
                if (_imoniPositiveChannel == value)
                    return;

                _imoniPositiveChannel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImoniPositiveChannel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImoniNegativeChannel)));
            }
        }

        public string ImoniNegativeChannel
        {
            get
            {
                string ch = (_imoniPositiveChannel ?? string.Empty).Trim().ToLowerInvariant();
                switch (ch)
                {
                    case "ai0": return "ai4";
                    case "ai1": return "ai5";
                    case "ai2": return "ai6";
                    case "ai3": return "ai7";
                    default: return string.Empty;
                }
            }
        }

        public double VmoniScale { get => _vmoniScale; set => SetField(ref _vmoniScale, value); }
        public double VmoniOffset { get => _vmoniOffset; set => SetField(ref _vmoniOffset, value); }
        public double ImoniScale { get => _imoniScale; set => SetField(ref _imoniScale, value); }
        public double ImoniOffset { get => _imoniOffset; set => SetField(ref _imoniOffset, value); }
        public string PreferredCameraName { get => _preferredCameraName; set => SetField(ref _preferredCameraName, value); }
        public double CameraPreviewZoomFactor { get => _cameraPreviewZoomFactor; set => SetField(ref _cameraPreviewZoomFactor, value); }

        public double ComputeControlVoltage()
        {
            if (HvMaximumKv <= HvMinimumKv)
            {
                return 0.0;
            }

            double normalized = (TargetAppliedVoltageKv - HvMinimumKv) / (HvMaximumKv - HvMinimumKv);
            normalized = Math.Max(0.0, Math.Min(1.0, normalized));
            return normalized * ControlSignalMaximumV;
        }

        public double ConvertVmoniToVoltage(double vmoniRawV)
        {
            return vmoniRawV * VmoniScale + VmoniOffset;
        }

        public double ConvertImoniToCurrent(double imoniRawV)
        {
            return imoniRawV * ImoniScale + ImoniOffset;
        }

        public string Validate()
        {
            if (HvMaximumKv <= HvMinimumKv)
                return "HV maximum must be larger than HV minimum.";
            if (ControlSignalMaximumV <= 0)
                return "Control signal maximum must be positive.";
            if (ControlSignalMaximumV > 10.0)
                return "USB-6001 AO can output within ±10 V only. Keep control signal maximum at 10 V or below.";
            if (TargetAppliedVoltageKv < HvMinimumKv || TargetAppliedVoltageKv > HvMaximumKv)
                return "Target voltage must lie within the HV module range.";
            if (TotalSamplingTimeSeconds <= 0)
                return "Total sampling time must be positive.";
            if (SampleIntervalMilliseconds < 20)
                return "Sample interval should be 20 ms or longer for stable UI updates.";
            if (string.IsNullOrWhiteSpace(SaveFolderPath))
                return "Save folder path is required.";
            if (string.IsNullOrWhiteSpace(DaqDeviceName))
                return "DAQ device name is required (for example Dev1).";
            if (string.IsNullOrWhiteSpace(AnalogOutputChannel) || string.IsNullOrWhiteSpace(VmoniChannel) || string.IsNullOrWhiteSpace(ImoniPositiveChannel))
                return "DAQ channel names are required.";
            if (string.IsNullOrWhiteSpace(ImoniNegativeChannel))
                return "Imoni differential channel must be ai0, ai1, ai2, or ai3.";
            if (CameraPreviewZoomFactor < 1.0 || CameraPreviewZoomFactor > 4.0)
                return "Preview zoom factor should be between 1.0 and 4.0.";
            return string.Empty;
        }

        private static string GetDefaultSaveFolderPath()
        {
            string current = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);

            for (int i = 0; i < 8; i++)
            {
                string dataPath = Path.Combine(current, "Data");

                if (Directory.Exists(dataPath) || File.Exists(Path.Combine(current, "ElectrosprayControlSystem.sln")))
                {
                    return dataPath;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                    break;

                current = parent.FullName;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            if (string.Equals(propertyName, nameof(ImoniPositiveChannel), StringComparison.Ordinal))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImoniNegativeChannel)));
            }
        }
    }
}