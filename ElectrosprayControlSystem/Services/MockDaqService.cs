using System;
using System.Threading;
using System.Threading.Tasks;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class MockDaqService : IDaqService
    {
        private readonly Random _random = new Random();
        private double _controlVoltageV;
        private DateTime _startUtc;

        public string BackendName => "Mock DAQ";

        public Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            _startUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task SetControlVoltageAsync(double voltageV, CancellationToken cancellationToken)
        {
            _controlVoltageV = voltageV;
            return Task.CompletedTask;
        }

        public Task<DaqReading> ReadMonitorsAsync(CancellationToken cancellationToken)
        {
            double t = (DateTime.UtcNow - _startUtc).TotalSeconds;
            double vmoni = _controlVoltageV * 0.95 + 0.03 * Math.Sin(2 * Math.PI * 0.7 * t) + 0.01 * NextCentered();
            double imoni = 0.20 * _controlVoltageV + 0.02 * Math.Sin(2 * Math.PI * 1.6 * t) + 0.005 * NextCentered();
            return Task.FromResult(new DaqReading
            {
                VmoniV = Math.Max(0.0, vmoni),
                ImoniV = Math.Max(0.0, imoni)
            });
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _controlVoltageV = 0.0;
            return Task.CompletedTask;
        }

        private double NextCentered() => _random.NextDouble() - 0.5;
    }
}
