using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Basler.Pylon;
using System;
using System.Runtime.InteropServices;

namespace AvaloniaApplication1.Views
{
    public partial class MainWindow : Window
    {
        private Camera? _camera;
        private PixelDataConverter? _converter;

        public MainWindow()
        {
            InitializeComponent();

            OpenCameraButton.Click += OnOpenCameraClicked;
            Closed += OnClosed;
        }

        private void OnOpenCameraClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_camera is null)
            {
                InitializeCamera();
            }
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
                var bitmap = new Bitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul,
                    pointer,
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    stride);

                CameraImage.Source = bitmap;
            });

            handle.Free();
        }
    }
}
