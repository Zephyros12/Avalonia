using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Basler.Pylon;

namespace AvaloniaApplication1.Views
{
    public partial class MainWindow : Window
    {
        private Camera? _camera;
        private PixelDataConverter? _converter;
        private Bitmap? _latestFrame;
        private readonly List<Bitmap> _capturedImages = new();
        private double _imageScale = 1.0;

        public MainWindow()
        {
            InitializeComponent();

            OpenCameraButton.Click += OpenCameraButton_Click;
            CaptureButton.Click += CaptureButton_Click;
            SaveButton.Click += SaveButton_Click;
            LoadButton.Click += LoadButton_Click;
            CapturedListBox.SelectionChanged += CapturedListBox_SelectionChanged;
            CameraImage.PointerWheelChanged += CameraImage_PointerWheelChanged;
            Closed += MainWindow_Closed;
        }

        private void CameraImage_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            const double ZoomStep = 0.1;
            double delta = e.Delta.Y > 0 ? ZoomStep : -ZoomStep;
            _imageScale = Math.Clamp(_imageScale + delta, 0.1, 5.0);
            CameraImage.RenderTransform = new ScaleTransform(_imageScale, _imageScale);
        }

        private async void OpenCameraButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_camera is not null)
            {
                return;
            }

            var cameraInfos = CameraFinder.Enumerate();
            if (!cameraInfos.Any())
            {
                await ShowMessageBoxAsync("No Basler cameras found.");
                return;
            }

            var dialog = new CameraSelectionDialog(cameraInfos);
            await dialog.ShowDialog(this);

            if (dialog.SelectedSerialNumber is not null)
            {
                InitializeCamera(dialog.SelectedSerialNumber);
            }
        }

        private void InitializeCamera(string serialNumber)
        {
            _camera = new Camera(serialNumber);
            _camera.Open();

            _converter = new PixelDataConverter
            {
                OutputPixelFormat = PixelType.BGRA8packed
            };

            _camera.StreamGrabber.ImageGrabbed += Camera_StreamGrabber_ImageGrabbed;
            _camera.StreamGrabber.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByStreamGrabber);
        }

        private async Task ShowMessageBoxAsync(string message)
        {
            var msgBox = new Window
            {
                Title = "Notice",
                Width = 300,
                Height = 150
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };

            okButton.Click += (_, _) => msgBox.Close();

            msgBox.Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new TextBlock { Text = message },
                    okButton
                }
            };

            await msgBox.ShowDialog(this);
        }

        private void CaptureButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

        private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (CapturedListBox.SelectedItems is null || CapturedListBox.SelectedItems.Count == 0)
            {
                return;
            }

            var options = new FilePickerSaveOptions
            {
                Title = "Save Captured Image",
                SuggestedFileName = "Captured",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new("PNG Files") { Patterns = new[] { "*.png" } }
                },
                DefaultExtension = "png"
            };

            var file = await StorageProvider.SaveFilePickerAsync(options);

            if (file is null)
            {
                return;
            }

            int index = 0;
            foreach (var item in CapturedListBox.SelectedItems)
            {
                if (item is Bitmap bitmap)
                {
                    string filename = CapturedListBox.SelectedItems.Count == 1
                        ? file.Path.LocalPath
                        : Path.Combine(Path.GetDirectoryName(file.Path.LocalPath) ?? ".", $"Captured_{index++}.png");

                    using FileStream stream = File.Create(filename);
                    bitmap.Save(stream);
                }
            }
        }

        private async void LoadButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Load Captured Images",
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("PNG Files") { Patterns = new[] { "*.png" } }
                }
            };

            var files = await StorageProvider.OpenFilePickerAsync(options);

            if (files is null || files.Count == 0)
            {
                return;
            }

            DisposeCapturedImages();
            _capturedImages.Clear();

            foreach (var file in files)
            {
                try
                {
                    using FileStream stream = File.OpenRead(file.Path.LocalPath);
                    var bitmap = new Bitmap(stream);
                    _capturedImages.Add(bitmap);
                }
                catch
                {
                    // skip
                }
            }

            CapturedListBox.SelectedItems?.Clear();
            CapturedListBox.ItemsSource = null;
            CapturedListBox.ItemsSource = _capturedImages;

            if (_capturedImages.Count > 0)
            {
                CapturedListBox.SelectedIndex = 0;
                CameraImage.Source = _capturedImages[0];
            }
        }

        private void CapturedListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CapturedListBox.SelectedItem is Bitmap bitmap)
            {
                CameraImage.Source = bitmap;
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            ShutdownCamera();
            DisposeCapturedImages();
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

        private void Camera_StreamGrabber_ImageGrabbed(object? sender, ImageGrabbedEventArgs e)
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

    public class CameraSelectionDialog : Window
    {
        private readonly ListBox _cameraListBox = new ListBox();

        public string? SelectedSerialNumber { get; private set; }

        public CameraSelectionDialog(IEnumerable<ICameraInfo> cameras)
        {
            Title = "Select Camera";
            Width = 300;
            Height = 400;

            _cameraListBox.ItemsSource = cameras.Select(c => c[CameraInfoKey.SerialNumber]);
            _cameraListBox.Margin = new Thickness(10);
            _cameraListBox.SelectionMode = SelectionMode.Single;

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(10),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            okButton.Click += (_, _) =>
            {
                if (_cameraListBox.SelectedItem is string serial)
                {
                    SelectedSerialNumber = serial;
                    Close();
                }
            };

            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Select a camera to connect:", Margin = new Thickness(10) },
                    _cameraListBox,
                    okButton
                }
            };
        }
    }
}
