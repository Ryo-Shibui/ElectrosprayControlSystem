using System.Threading;
using System.Threading.Tasks;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public interface IDaqService
    {
        Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken);
        Task SetControlVoltageAsync(double voltageV, CancellationToken cancellationToken);
        Task<DaqReading> ReadMonitorsAsync(CancellationToken cancellationToken);
        Task ShutdownAsync(CancellationToken cancellationToken);
        string BackendName { get; }
    }
}
