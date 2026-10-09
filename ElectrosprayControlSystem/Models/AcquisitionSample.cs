using System;

namespace ElectrosprayControlSystem.Models
{
    public class AcquisitionSample
    {
        public DateTime TimestampUtc { get; set; }
        public double ElapsedSeconds { get; set; }
        public double ControlVoltageV { get; set; }
        public double VmoniRawV { get; set; }
        public double ImoniRawV { get; set; }
        public double VoltageScaled { get; set; }
        public double CurrentScaled { get; set; }
    }
}
