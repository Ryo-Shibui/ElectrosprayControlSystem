using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public interface ICameraService
    {
        IReadOnlyList<CameraDeviceInfo> GetAvailableDevices();
        Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken);
        Task<BitmapSource> GetPreviewFrameAsync(CancellationToken cancellationToken);
        Task SaveStillAsync(string filePath, CancellationToken cancellationToken);
        Task SetLightEnabledAsync(bool enabled, CancellationToken cancellationToken);
        Task ShutdownAsync(CancellationToken cancellationToken);
        string ActiveDeviceName { get; }
        string BackendName { get; }
        bool SupportsLightControl { get; }
    }
}
