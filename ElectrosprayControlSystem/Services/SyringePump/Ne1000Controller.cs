using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ElectrosprayControlSystem.Services.SyringePump
{
    public sealed class Ne1000Controller
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
        private readonly ISerialTransport _transport;

        public Ne1000Controller(ISerialTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public static string FormatBasicCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command is required.", nameof(command));
            }

            return command.Trim() + "\r";
        }

        public Task<string> SendAsync(string command, CancellationToken cancellationToken)
        {
            return _transport.SendCommandAsync(FormatBasicCommand(command), DefaultTimeout, cancellationToken);
        }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken)
        {
            return SendAsync("VER", cancellationToken);
        }

        public Task<string> SetDiameterAsync(double diameterMillimeters, CancellationToken cancellationToken)
        {
            return SendAsync("DIA " + FormatNumber(diameterMillimeters), cancellationToken);
        }

        public Task<string> SetRateAsync(double rate, Ne1000RateUnit unit, CancellationToken cancellationToken)
        {
            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }

            return SendAsync("RAT " + FormatNumber(rate) + " " + unit.CommandCode, cancellationToken);
        }

        public Task<string> SetVolumeAsync(double volume, CancellationToken cancellationToken)
        {
            return SendAsync("VOL " + FormatNumber(volume), cancellationToken);
        }

        public Task<string> SetDirectionAsync(bool infuse, CancellationToken cancellationToken)
        {
            return SendAsync(infuse ? "DIR INF" : "DIR WDR", cancellationToken);
        }

        public Task<string> StartAsync(CancellationToken cancellationToken)
        {
            return SendAsync("RUN", cancellationToken);
        }

        public Task<string> StopAsync(CancellationToken cancellationToken)
        {
            return SendAsync("STP", cancellationToken);
        }

        public Task<string> PurgeAsync(CancellationToken cancellationToken)
        {
            return SendAsync("PUR", cancellationToken);
        }

        public Task<string> GetDispensedAsync(CancellationToken cancellationToken)
        {
            return SendAsync("DIS", cancellationToken);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
