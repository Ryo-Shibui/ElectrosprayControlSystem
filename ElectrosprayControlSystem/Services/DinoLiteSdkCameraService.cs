using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class DinoLiteSdkCameraService : ICameraService
    {
        private readonly AForgeCameraService _frameCapture = new AForgeCameraService();
        private readonly object _sdkSync = new object();
        private DinoLiteSdkNative _native;
        private string _sdkDllPath;
        private string _activeDeviceName = "not connected";
        private int _selectedIndex;
        private bool _lightEnabled = true;

        public string ActiveDeviceName => _activeDeviceName;
        public string BackendName => string.IsNullOrWhiteSpace(_sdkDllPath)
            ? "Dino-Lite SDK (DNX64)"
            : $"Dino-Lite SDK (DNX64) [{System.IO.Path.GetFileName(_sdkDllPath)}]";

        public bool SupportsLightControl => true;

        public IReadOnlyList<CameraDeviceInfo> GetAvailableDevices()
        {
            var fallbackDevices = _frameCapture.GetAvailableDevices();

            try
            {
                using (var native = TryCreateNative())
                {
                    if (native == null || !native.Initialize())
                    {
                        return fallbackDevices;
                    }

                    int count = native.GetVideoDeviceCount();
                    var devices = new List<CameraDeviceInfo>();
                    for (int i = 0; i < count; i++)
                    {
                        string name = native.GetVideoDeviceName(i);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            devices.Add(new CameraDeviceInfo { Name = $"[{i}] {name}", MonikerString = $"dnx64://{i}" });
                        }
                    }

                    return devices.Count > 0 ? devices : fallbackDevices;
                }
            }
            catch
            {
                return fallbackDevices;
            }
        }

        public async Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            _native = TryCreateNative();
            if (_native == null)
            {
                throw new InvalidOperationException(BuildSdkMissingMessage());
            }

            _sdkDllPath = DinoLiteSdkLocator.ResolveDllPath();
            if (!_native.Initialize())
            {
                throw new InvalidOperationException("DNX64.dll was loaded, but SDK initialization failed. Confirm the Dino-Lite is supported and connected.");
            }

            var sdkDevices = EnumerateSdkDevices(_native);
            if (sdkDevices.Count == 0)
            {
                throw new InvalidOperationException("DNX64 initialized, but no Dino-Lite-compatible video device was reported by the SDK.");
            }

            int selectedIndex = SelectDeviceIndex(settings, sdkDevices);
            _selectedIndex = selectedIndex;
            _native.SetVideoDeviceIndex(selectedIndex);
            await _frameCapture.InitializeAsync(settings, cancellationToken);
            _activeDeviceName = $"{_frameCapture.ActiveDeviceName} via SDK index {selectedIndex}";
            try
            {
                // SDK examples note a short buffer after selecting the device.
                await Task.Delay(100, cancellationToken);
                lock (_sdkSync)
                {
                    _native.SetVideoDeviceIndex(_selectedIndex);
                    _native.SetLEDState(_selectedIndex, 1);
                }
                _lightEnabled = true;
            }
            catch
            {
                _lightEnabled = true;
            }
        }

        public Task<BitmapSource> GetPreviewFrameAsync(CancellationToken cancellationToken)
        {
            return _frameCapture.GetPreviewFrameAsync(cancellationToken);
        }

        public Task SaveStillAsync(string filePath, CancellationToken cancellationToken)
        {
            return _frameCapture.SaveStillAsync(filePath, cancellationToken);
        }

        public async Task SetLightEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_native == null)
            {
                throw new InvalidOperationException("Dino-Lite SDK camera is not initialized.");
            }

            // DNX64 documentation and examples require the correct device index to be selected
            // before camera operations, and they recommend a short buffer between operations.
            lock (_sdkSync)
            {
                _native.SetVideoDeviceIndex(_selectedIndex);
                _native.SetLEDState(_selectedIndex, enabled ? 1 : 0);
            }

            await Task.Delay(enabled ? 120 : 60, cancellationToken);

            // Turning the LED back on is sometimes unreliable unless the device is re-selected
            // and the command is sent again after a short delay.
            if (enabled)
            {
                lock (_sdkSync)
                {
                    _native.SetVideoDeviceIndex(_selectedIndex);
                    _native.SetLEDState(_selectedIndex, 1);
                }
                await Task.Delay(60, cancellationToken);
            }

            _lightEnabled = enabled;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await _frameCapture.ShutdownAsync(cancellationToken);
            _native?.Dispose();
            _native = null;
            _activeDeviceName = "not connected";
        }

        internal static string BuildSdkMissingMessage()
        {
            var candidates = DinoLiteSdkLocator.GetSearchCandidates();
            return "DNX64.dll was not found. Put DNX64.dll and libusbK.dll under ElectrosprayControlSystem/ThirdParty/DinoLite/ before building. Searched:\n - " + string.Join("\n - ", candidates);
        }

        private static DinoLiteSdkNative TryCreateNative()
        {
            string dllPath = DinoLiteSdkLocator.ResolveDllPath();
            return string.IsNullOrWhiteSpace(dllPath) ? null : new DinoLiteSdkNative(dllPath);
        }

        private static List<CameraDeviceInfo> EnumerateSdkDevices(DinoLiteSdkNative native)
        {
            int count = native.GetVideoDeviceCount();
            var devices = new List<CameraDeviceInfo>();
            for (int i = 0; i < count; i++)
            {
                string name = native.GetVideoDeviceName(i);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    devices.Add(new CameraDeviceInfo { Name = name, MonikerString = $"dnx64://{i}" });
                }
            }

            return devices;
        }

        private static int SelectDeviceIndex(MeasurementSettings settings, IReadOnlyList<CameraDeviceInfo> sdkDevices)
        {
            if (sdkDevices == null || sdkDevices.Count == 0)
            {
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(settings?.PreferredCameraName))
            {
                for (int i = 0; i < sdkDevices.Count; i++)
                {
                    if (sdkDevices[i].Name.IndexOf(settings.PreferredCameraName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
            }

            return 0;
        }
    }
}
