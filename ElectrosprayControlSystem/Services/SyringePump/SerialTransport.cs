using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ElectrosprayControlSystem.Services.SyringePump
{
    public sealed class SerialTransport : ISerialTransport
    {
        private readonly SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1);
        private SerialPort _serialPort;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
        public string PortName => _serialPort?.PortName ?? string.Empty;

        public IReadOnlyList<string> GetAvailablePortNames()
        {
            return SerialPort.GetPortNames()
                .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public Task ConnectAsync(string portName, int baudRate, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("COM port is required.", nameof(portName));
            }

            Disconnect();

            cancellationToken.ThrowIfCancellationRequested();

            var serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.ASCII,
                Handshake = Handshake.None,
                ReadTimeout = 100,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };

            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();
            _serialPort = serialPort;

            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            SerialPort oldPort = _serialPort;
            _serialPort = null;

            if (oldPort == null)
            {
                return;
            }

            try
            {
                if (oldPort.IsOpen)
                {
                    oldPort.Close();
                }
            }
            finally
            {
                oldPort.Dispose();
            }
        }

        public async Task<string> SendCommandAsync(string commandWithTerminator, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(commandWithTerminator))
            {
                throw new ArgumentException("Command is required.", nameof(commandWithTerminator));
            }

            await _commandLock.WaitAsync(cancellationToken);
            try
            {
                SerialPort port = _serialPort;
                if (port == null || !port.IsOpen)
                {
                    throw new InvalidOperationException("Serial port is not connected.");
                }

                return await Task.Run(() =>
                {
                    port.DiscardInBuffer();
                    port.Write(commandWithTerminator);
                    return ReadStxEtxPacket(port, timeout, cancellationToken);
                }, cancellationToken);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public static IReadOnlyList<string> ExtractPacketsForTest(byte[] bytes)
        {
            var packets = new List<string>();
            var current = new List<byte>();
            bool inPacket = false;

            foreach (byte value in bytes ?? Array.Empty<byte>())
            {
                if (value == 0x02)
                {
                    current.Clear();
                    inPacket = true;
                    continue;
                }

                if (value == 0x03)
                {
                    if (inPacket)
                    {
                        packets.Add(Encoding.ASCII.GetString(current.ToArray()));
                    }

                    current.Clear();
                    inPacket = false;
                    continue;
                }

                if (inPacket)
                {
                    current.Add(value);
                }
            }

            return packets;
        }

        private static string ReadStxEtxPacket(SerialPort port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            DateTime deadlineUtc = DateTime.UtcNow + timeout;
            var payload = new List<byte>();
            bool inPacket = false;

            while (DateTime.UtcNow < deadlineUtc)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int nextByte;
                try
                {
                    nextByte = port.ReadByte();
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (nextByte == 0x02)
                {
                    payload.Clear();
                    inPacket = true;
                    continue;
                }

                if (nextByte == 0x03)
                {
                    if (inPacket)
                    {
                        return Encoding.ASCII.GetString(payload.ToArray());
                    }

                    continue;
                }

                if (inPacket)
                {
                    payload.Add((byte)nextByte);
                }
            }

            throw new TimeoutException("Timed out waiting for an NE-1000 response packet.");
        }

        public void Dispose()
        {
            Disconnect();
            _commandLock.Dispose();
        }
    }
}
