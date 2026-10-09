using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ElectrosprayControlSystem.Services.SyringePump
{
    public interface ISerialTransport : IDisposable
    {
        bool IsConnected { get; }
        string PortName { get; }

        IReadOnlyList<string> GetAvailablePortNames();
        Task ConnectAsync(string portName, int baudRate, CancellationToken cancellationToken);
        void Disconnect();
        Task<string> SendCommandAsync(string commandWithTerminator, TimeSpan timeout, CancellationToken cancellationToken);
    }
}
