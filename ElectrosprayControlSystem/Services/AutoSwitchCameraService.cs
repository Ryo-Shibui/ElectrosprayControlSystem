using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class AutoSwitchCameraService : ICameraService
    {
        private readonly ICameraService _sdkService = new DinoLiteSdkCameraService();
        private readonly ICameraService _directShowService = new AForgeCameraService();
        private ICameraService _activeService;
        private string _backendName = "not initialized";
        private string _activeDeviceName = "not connected";
        private string _lastFallbackReason;

        public string ActiveDeviceName => _activeDeviceName;
        public string BackendName => _activeService?.BackendName ?? _backendName;
        public bool SupportsLightControl => _activeService?.SupportsLightControl ?? false;

        public IReadOnlyList<CameraDeviceInfo> GetAvailableDevices()
        {
            var all = new List<CameraDeviceInfo>();
            TryAdd(all, _sdkService.GetAvailableDevices());
            TryAdd(all, _directShowService.GetAvailableDevices());
            return all
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        public async Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            await ShutdownAsync(cancellationToken);

            try
            {
                await StartAsync(_sdkService, settings, cancellationToken);
                _lastFallbackReason = null;
            }
            catch (Exception ex)
            {
                _lastFallbackReason = ex.Message;
                await StartAsync(_directShowService, settings, cancellationToken);
                _backendName = $"{_activeService.BackendName} (SDK fallback: {_lastFallbackReason})";
            }
        }

        public Task<BitmapSource> GetPreviewFrameAsync(CancellationToken cancellationToken)
        {
            if (_activeService == null)
            {
                throw new InvalidOperationException("Camera service has not been initialized.");
            }

            return _activeService.GetPreviewFrameAsync(cancellationToken);
        }

        public Task SaveStillAsync(string filePath, CancellationToken cancellationToken)
        {
            if (_activeService == null)
            {
                throw new InvalidOperationException("Camera service has not been initialized.");
            }

            return _activeService.SaveStillAsync(filePath, cancellationToken);
        }

        public Task SetLightEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            if (_activeService == null)
            {
                throw new InvalidOperationException("Camera service has not been initialized.");
            }

            return _activeService.SetLightEnabledAsync(enabled, cancellationToken);
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            if (_activeService != null)
            {
                await _activeService.ShutdownAsync(cancellationToken);
            }

            _activeService = null;
            _backendName = "not initialized";
            _activeDeviceName = "not connected";
        }

        private async Task StartAsync(ICameraService service, MeasurementSettings settings, CancellationToken cancellationToken)
        {
            await service.InitializeAsync(settings, cancellationToken);
            _activeService = service;
            _backendName = service.BackendName;
            _activeDeviceName = service.ActiveDeviceName;
        }

        private static void TryAdd(List<CameraDeviceInfo> target, IReadOnlyList<CameraDeviceInfo> source)
        {
            if (source == null)
            {
                return;
            }

            target.AddRange(source.Where(x => x != null));
        }
    }
}
