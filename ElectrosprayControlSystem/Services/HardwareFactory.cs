namespace ElectrosprayControlSystem.Services
{
    public static class HardwareFactory
    {
        public static IDaqService CreateDaqService()
        {
            return new NiUsb6001DaqService();
        }

        public static ICameraService CreateCameraService()
        {
            return new AutoSwitchCameraService();
        }
    }
}
