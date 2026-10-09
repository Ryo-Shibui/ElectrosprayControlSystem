namespace ElectrosprayControlSystem.Services.SyringePump
{
    public sealed class Ne1000RateUnit
    {
        public static readonly Ne1000RateUnit MicrolitersPerMinute = new Ne1000RateUnit("uL/min", "UM");
        public static readonly Ne1000RateUnit MillilitersPerMinute = new Ne1000RateUnit("mL/min", "MM");
        public static readonly Ne1000RateUnit MicrolitersPerHour = new Ne1000RateUnit("uL/hr", "UH");
        public static readonly Ne1000RateUnit MillilitersPerHour = new Ne1000RateUnit("mL/hr", "MH");

        public Ne1000RateUnit(string displayName, string commandCode)
        {
            DisplayName = displayName;
            CommandCode = commandCode;
        }

        public string DisplayName { get; }
        public string CommandCode { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
