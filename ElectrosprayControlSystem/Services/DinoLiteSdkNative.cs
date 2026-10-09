using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ElectrosprayControlSystem.Services
{
    internal sealed class DinoLiteSdkNative : IDisposable
    {
        private IntPtr _moduleHandle;
        private bool _initialized;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool InitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetVideoDeviceCountDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetVideoDeviceNameDelegate(int deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetVideoDeviceIndexDelegate(int deviceIndex);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetLEDStateDelegate(int deviceIndex, int ledState);

        private readonly InitDelegate _init;
        private readonly GetVideoDeviceCountDelegate _getVideoDeviceCount;
        private readonly GetVideoDeviceNameDelegate _getVideoDeviceName;
        private readonly SetVideoDeviceIndexDelegate _setVideoDeviceIndex;
        private readonly SetLEDStateDelegate _setLEDState;

        public DinoLiteSdkNative(string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath))
            {
                throw new ArgumentException("A valid DNX64.dll path is required.", nameof(dllPath));
            }

            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException("DNX64.dll was not found.", dllPath);
            }

            string folder = Path.GetDirectoryName(Path.GetFullPath(dllPath));
            if (!string.IsNullOrWhiteSpace(folder))
            {
                SetDllDirectory(folder);
            }

            _moduleHandle = LoadLibrary(dllPath);
            if (_moduleHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load DNX64.dll from {dllPath}. Last Win32 error = {Marshal.GetLastWin32Error()}.");
            }

            _init = GetDelegate<InitDelegate>("Init");
            _getVideoDeviceCount = GetDelegate<GetVideoDeviceCountDelegate>("GetVideoDeviceCount");
            _getVideoDeviceName = GetDelegate<GetVideoDeviceNameDelegate>("GetVideoDeviceName");
            _setVideoDeviceIndex = GetDelegate<SetVideoDeviceIndexDelegate>("SetVideoDeviceIndex");
            _setLEDState = GetDelegate<SetLEDStateDelegate>("SetLEDState");
        }

        public bool Initialize()
        {
            _initialized = _init();
            return _initialized;
        }

        public int GetVideoDeviceCount()
        {
            EnsureInitialized();
            return _getVideoDeviceCount();
        }

        public string GetVideoDeviceName(int deviceIndex)
        {
            EnsureInitialized();
            IntPtr ptr = _getVideoDeviceName(deviceIndex);
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(ptr);
        }

        public void SetVideoDeviceIndex(int deviceIndex)
        {
            EnsureInitialized();
            _setVideoDeviceIndex(deviceIndex);
        }

        public void SetLEDState(int deviceIndex, int ledState)
        {
            EnsureInitialized();
            _setLEDState(deviceIndex, ledState);
        }

        public void Dispose()
        {
            if (_moduleHandle != IntPtr.Zero)
            {
                FreeLibrary(_moduleHandle);
                _moduleHandle = IntPtr.Zero;
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("DNX64 SDK has not been initialized.");
            }
        }

        private T GetDelegate<T>(string exportName) where T : class
        {
            IntPtr proc = GetProcAddress(_moduleHandle, exportName);
            if (proc == IntPtr.Zero)
            {
                throw new MissingMethodException($"DNX64.dll does not expose required export '{exportName}'.");
            }

            return Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);
    }
}
