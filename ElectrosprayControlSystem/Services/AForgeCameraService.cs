using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class AForgeCameraService : ICameraService
    {
        private readonly object _sync = new object();
        private VideoCaptureDevice _device;
        private Bitmap _latestFrame;
        private string _activeDeviceName = "not connected";
        private MeasurementSettings _settings;

        public string ActiveDeviceName => _activeDeviceName;
        public string BackendName => "DirectShow (AForge)";
        public bool SupportsLightControl => false;

        public IReadOnlyList<CameraDeviceInfo> GetAvailableDevices()
        {
            try
            {
                var filters = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                return filters.Cast<FilterInfo>()
                    .Select(f => new CameraDeviceInfo { Name = f.Name, MonikerString = f.MonikerString })
                    .ToList();
            }
            catch
            {
                return Array.Empty<CameraDeviceInfo>();
            }
        }

        public async Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            _settings = settings;

            if (_device != null && _device.IsRunning)
            {
                return;
            }

            var devices = GetAvailableDevices();
            if (devices.Count == 0)
            {
                throw new InvalidOperationException("No video input device was found. Confirm that the Dino-Lite is connected and visible in Windows camera devices.");
            }

            CameraDeviceInfo selected = devices.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(settings?.PreferredCameraName) &&
                d.Name.IndexOf(settings.PreferredCameraName, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? devices.First();

            _device = new VideoCaptureDevice(selected.MonikerString);

            try
            {
                var caps = _device.VideoCapabilities;
                if (caps != null && caps.Length > 0)
                {
                    var best = caps
                        .OrderByDescending(c => c.FrameSize.Width * c.FrameSize.Height)
                        .ThenByDescending(c => c.AverageFrameRate)
                        .First();

                    _device.VideoResolution = best;
                    _activeDeviceName = $"{selected.Name} ({best.FrameSize.Width}x{best.FrameSize.Height} @ {best.AverageFrameRate}fps)";
                }
                else
                {
                    _activeDeviceName = selected.Name;
                }
            }
            catch
            {
                _activeDeviceName = selected.Name;
            }

            _device.NewFrame += OnNewFrame;
            _device.Start();

            await WaitForFirstFrameAsync(cancellationToken);
        }

        public Task<BitmapSource> GetPreviewFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_latestFrame == null)
                {
                    return Task.FromResult<BitmapSource>(null);
                }

                using (Bitmap displayFrame = CreateZoomedFrame(_latestFrame, CurrentZoomFactor))
                {
                    IntPtr hBitmap = displayFrame.GetHbitmap();
                    try
                    {
                        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        return Task.FromResult(source);
                    }
                    finally
                    {
                        NativeMethods.DeleteObject(hBitmap);
                    }
                }
            }
        }

        public Task SaveStillAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_latestFrame == null)
                {
                    throw new InvalidOperationException("No camera frame is available yet.");
                }

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (Bitmap stillFrame = CreateZoomedFrame(_latestFrame, CurrentZoomFactor))
                {
                    stillFrame.Save(filePath, ImageFormat.Png);
                }
            }

            return Task.CompletedTask;
        }

        public Task SetLightEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_device != null)
            {
                _device.NewFrame -= OnNewFrame;
                if (_device.IsRunning)
                {
                    _device.SignalToStop();
                    _device.WaitForStop();
                }
                _device = null;
            }

            lock (_sync)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }

            _activeDeviceName = "not connected";
            return Task.CompletedTask;
        }

        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            lock (_sync)
            {
                _latestFrame?.Dispose();
                _latestFrame = (Bitmap)eventArgs.Frame.Clone();
            }
        }


        private double CurrentZoomFactor => SanitizeZoom(_settings?.CameraPreviewZoomFactor ?? 1.0);

        private static double SanitizeZoom(double zoomFactor)
        {
            if (double.IsNaN(zoomFactor) || double.IsInfinity(zoomFactor))
            {
                return 1.0;
            }

            return Math.Max(1.0, Math.Min(4.0, zoomFactor));
        }

        private static Bitmap CreateZoomedFrame(Bitmap source, double zoomFactor)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            double effectiveZoom = SanitizeZoom(zoomFactor);
            if (effectiveZoom <= 1.001)
            {
                return (Bitmap)source.Clone();
            }

            int cropWidth = Math.Max(1, (int)Math.Round(source.Width / effectiveZoom));
            int cropHeight = Math.Max(1, (int)Math.Round(source.Height / effectiveZoom));
            int cropX = Math.Max(0, (source.Width - cropWidth) / 2);
            int cropY = Math.Max(0, (source.Height - cropHeight) / 2);
            var cropRect = new Rectangle(cropX, cropY, cropWidth, cropHeight);

            var zoomed = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(zoomed))
            {
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, zoomed.Width, zoomed.Height), cropRect, GraphicsUnit.Pixel);
            }

            return zoomed;
        }

        private async Task WaitForFirstFrameAsync(CancellationToken cancellationToken)
        {
            DateTime timeoutAt = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (_latestFrame != null)
                    {
                        return;
                    }
                }
                await Task.Delay(50, cancellationToken);
            }

            throw new TimeoutException("Camera preview did not deliver the first frame within 5 seconds.");
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("gdi32.dll")]
            public static extern bool DeleteObject(IntPtr hObject);
        }
    }
}
