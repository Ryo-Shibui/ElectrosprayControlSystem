using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class MockCameraService : ICameraService
    {
        private const int Width = 960;
        private const int Height = 540;
        private DateTime _startUtc;
        private BitmapSource _lastFrame;

        public string ActiveDeviceName => "Mock camera";
        public string BackendName => "Mock camera";
        public bool SupportsLightControl => false;

        public IReadOnlyList<CameraDeviceInfo> GetAvailableDevices()
        {
            return new[] { new CameraDeviceInfo { Name = "Mock camera", MonikerString = "mock://camera" } };
        }

        public Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            _startUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task<BitmapSource> GetPreviewFrameAsync(CancellationToken cancellationToken)
        {
            double t = (DateTime.UtcNow - _startUtc).TotalSeconds;
            _lastFrame = RenderFrame(t);
            return Task.FromResult(_lastFrame);
        }

        public async Task SaveStillAsync(string filePath, CancellationToken cancellationToken)
        {
            if (_lastFrame == null)
            {
                _lastFrame = await GetPreviewFrameAsync(cancellationToken);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_lastFrame));
            using (var stream = System.IO.File.Create(filePath))
            {
                encoder.Save(stream);
            }
        }

        public Task SetLightEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private BitmapSource RenderFrame(double seconds)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Width, Height));

                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 255, 180)), 2);
                double centerX = Width * 0.50;
                double centerY = Height * 0.35;
                double coneWidth = 110 + 25 * Math.Sin(seconds * 2.1);
                double coneHeight = 140 + 10 * Math.Cos(seconds * 1.4);
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(centerX, centerY), false, false);
                    ctx.LineTo(new Point(centerX - coneWidth * 0.5, centerY + coneHeight), true, false);
                    ctx.LineTo(new Point(centerX + coneWidth * 0.5, centerY + coneHeight), true, false);
                }
                dc.DrawGeometry(null, pen, geometry);

                dc.DrawLine(new Pen(Brushes.White, 2), new Point(centerX, 20), new Point(centerX, centerY));
                dc.DrawLine(new Pen(Brushes.White, 1), new Point(centerX - 80, centerY + coneHeight + 50), new Point(centerX + 80, centerY + coneHeight + 50));

                for (int i = 0; i < 120; i++)
                {
                    double phase = i / 20.0;
                    double x = centerX + 70 * Math.Sin(seconds * 2.6 + phase) * Math.Exp(-phase * 0.18);
                    double y = centerY + coneHeight + phase * 18 + (seconds * 70 % 18);
                    double radius = Math.Max(1.0, 3.2 - 0.16 * phase);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(120, 220, 255)), null, new Point(x, y), radius, radius);
                }

                DrawLabel(dc, $"Mock Preview  {DateTime.Now:HH:mm:ss.fff}", 20, 18, 22, Brushes.White);
                DrawLabel(dc, "AForge camera initialization failed, so mock mode is active.", 20, Height - 34, 16, Brushes.LightGray);
            }

            var bmp = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private static void DrawLabel(DrawingContext dc, string text, double x, double y, double fontSize, Brush brush)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                brush,
                1.25);
            dc.DrawText(formatted, new Point(x, y));
        }
    }
}
