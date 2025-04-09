using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Basler.Pylon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace AvaloniaApplication1.Views
{
    public partial class MainWindow : Window
    {
        private Camera? _camera;
        private PixelDataConverter? _converter;
        private Bitmap? _latestFrame;
        private readonly List<Bitmap> _capturedImages = new();

        private const string CaptureDirectory = "CapturedImages";

        public MainWindow()
        {
            InitializeComponent();

            OpenCameraButton.Click += OnOpenCameraClicked;
            CaptureButton.Click += OnCaptureButtonClicked;
            SaveButton.Click += OnSaveButtonClicked;
            LoadButton.Click += OnLoadButtonClicked;
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

            Bitmap captured = CloneBitmap(_latestFrame);
            _capturedImages.Add(captured);
        }

        private void OnSaveButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_capturedImages.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(CaptureDirectory);

            for (int i = 0; i < _capturedImages.Count; i++)
            {
                string filename = Path.Combine(CaptureDirectory, $"Captured_{i}.png");

                using FileStream stream = File.Create(filename);
                _capturedImages[i].Save(stream);
            }
        }

        private void OnLoadButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            DisposeCapturedImages();
            _capturedImages.Clear();

            if (!Directory.Exists(CaptureDirectory))
            {
                return;
            }

            string[] files = Directory.GetFiles(CaptureDirectory, "Captured_*.png");
            foreach (string file in files)
            {
                try
                {
                    using FileStream stream = File.OpenRead(file);
                    Bitmap bitmap = new Bitmap(stream);
                    _capturedImages.Add(bitmap);
                }
                catch
                {
                    // Skip invalid or unreadable files
                }
            }

            if (_capturedImages.Count > 0)
            {
                CameraImage.Source = _capturedImages[0];
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            ShutdownCamera();
            DisposeCapturedImages();
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

        private void DisposeCapturedImages()
        {
            foreach (Bitmap bitmap in _capturedImages)
            {
                bitmap.Dispose();
            }

            _capturedImages.Clear();
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

        private static Bitmap CloneBitmap(Bitmap source)
        {
            using MemoryStream memoryStream = new MemoryStream();
            source.Save(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            return new Bitmap(memoryStream);
        }
    }
}
