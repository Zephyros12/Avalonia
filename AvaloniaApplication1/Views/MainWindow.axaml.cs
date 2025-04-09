using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Basler.Pylon;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AvaloniaApplication1.Views
{
    public partial class MainWindow : Window
    {
        private Camera? _camera;
        private PixelDataConverter? _converter;
        private Bitmap? _latestFrame;

        public MainWindow()
        {
            InitializeComponent();

            OpenCameraButton.Click += OnOpenCameraClicked;
            CaptureButton.Click += OnCaptureButtonClicked;
            Closed += OnClosed;
        }

        private void OnOpenCameraClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_camera is null)
            {
                InitializeCamera();
            }
        }

        private void OnCaptureButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_latestFrame is null)
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"Captured_{timestamp}.png";
            var path = Path.Combine(AppContext.BaseDirectory, filename);

            using FileStream stream = File.Create(path);
            _latestFrame.Save(stream);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            ShutdownCamera();
        }

        private void InitializeCamera()
        {
            _camera = new Camera();
            _camera.Open();

            _converter = new PixelDataConverter
            {
                OutputPixelFormat = PixelType.BGRA8packed
            };

            _camera.StreamGrabber.ImageGrabbed += OnImageGrabbed;
            _camera.StreamGrabber.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByStreamGrabber);
        }

        private void ShutdownCamera()
        {
            if (_camera is not null)
            {
                _camera.StreamGrabber.Stop();
                _camera.Close();
                _camera.Dispose();
                _camera = null;
            }

            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        private void OnImageGrabbed(object? sender, ImageGrabbedEventArgs e)
        {
            if (!e.GrabResult.GrabSucceeded || _converter is null)
            {
                return;
            }

            IGrabResult grabResult = e.GrabResult;
            int width = grabResult.Width;
            int height = grabResult.Height;
            int stride = width * 4;

            byte[] buffer = new byte[_converter.GetBufferSizeForConversion(grabResult)];
            _converter.Convert(buffer, grabResult);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            IntPtr pointer = handle.AddrOfPinnedObject();

            Dispatcher.UIThread.Post(() =>
            {
                _latestFrame?.Dispose();

                _latestFrame = new Bitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul,
                    pointer,
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    stride);

                CameraImage.Source = _latestFrame;
            });

            handle.Free();
        }
    }
}
