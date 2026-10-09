namespace ElectrosprayControlSystem.Models
{
    public class CameraDeviceInfo
    {
        public string Name { get; set; }
        public string MonikerString { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
