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
            CapturedListBox.SelectionChanged += OnCapturedImageSelected;
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

            Bitmap clone = CloneBitmap(_latestFrame);
            _capturedImages.Add(clone);
            CapturedListBox.ItemsSource = null;
            CapturedListBox.ItemsSource = _capturedImages;
        }

        private void OnSaveButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (CapturedListBox.SelectedItems is null || CapturedListBox.SelectedItems.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(CaptureDirectory);
            int index = 0;

            foreach (var item in CapturedListBox.SelectedItems)
            {
                if (item is Bitmap bitmap)
                {
                    string path = Path.Combine(CaptureDirectory, $"Captured_{index++}.png");
                    using FileStream stream = File.Create(path);
                    bitmap.Save(stream);
                }
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

            var files = Directory.GetFiles(CaptureDirectory, "Captured_*.png");
            foreach (var file in files)
            {
                try
                {
                    using FileStream stream = File.OpenRead(file);
                    var bitmap = new Bitmap(stream);
                    _capturedImages.Add(bitmap);
                }
                catch
                {
                    // ¹«½Ã
                }
            }

            CapturedListBox.ItemsSource = null;
            CapturedListBox.ItemsSource = _capturedImages;

            if (_capturedImages.Count > 0)
            {
                CameraImage.Source = _capturedImages[0];
                CapturedListBox.SelectedIndex = 0;
            }
        }

        private void OnCapturedImageSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (CapturedListBox.SelectedItem is Bitmap bitmap)
            {
                CameraImage.Source = bitmap;
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
            CapturedListBox.SelectedItems?.Clear();
            foreach (var bitmap in _capturedImages)
            {
                bitmap.Dispose();
            }

            _capturedImages.Clear();
            CapturedListBox.ItemsSource = null;
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
            using MemoryStream ms = new MemoryStream();
            source.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return new Bitmap(ms);
        }
    }
}
