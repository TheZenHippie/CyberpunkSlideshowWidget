using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace SlideshowWidget
{
    public partial class MainWindow : Window
    {
        private List<string> _imageFiles = new List<string>();
        private List<int> _displayOrder = new List<int>();
        private int _displayIndex = -1;
        private readonly DispatcherTimer _timer;
        private bool _isAdjustingOrientation = false;
        private bool _recurseSubdirectories = false;
        private bool _isRandomized = false;
        private string? _currentFolderPath = null;
        private static readonly Random _random = new Random();

        public MainWindow()
        {
            InitializeComponent();

            // Set up slideshow interval timer (default: 5 seconds)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += Timer_Tick;

            // Handle display / monitor changes (e.g. resolution changes, sleep/wake, multi-monitor shifts)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                this.InvalidateVisual();
            });
        }

        // Left-click and hold to drag window safely
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Ignore if mouse state changed during call
                }
            }
        }

        // Folder selection menu logic
        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var result = FolderPicker.Show(this, _currentFolderPath, _recurseSubdirectories);
            if (result.Success && !string.IsNullOrWhiteSpace(result.SelectedPath))
            {
                _recurseSubdirectories = result.RecurseSubdirectories;
                if (RecurseSubdirectoriesMenuItem != null)
                {
                    RecurseSubdirectoriesMenuItem.IsChecked = _recurseSubdirectories;
                }

                LoadFolder(result.SelectedPath, keepCurrentImage: false);
            }
        }

        private void RecurseSubdirectories_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                _recurseSubdirectories = menuItem.IsChecked;
                if (!string.IsNullOrWhiteSpace(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                {
                    LoadFolder(_currentFolderPath, keepCurrentImage: true);
                }
            }
        }

        private void RandomizeOrder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                _isRandomized = menuItem.IsChecked;
                if (_imageFiles.Count > 0)
                {
                    int currentImageIndex = (_displayIndex >= 0 && _displayIndex < _displayOrder.Count)
                        ? _displayOrder[_displayIndex]
                        : 0;

                    if (_isRandomized)
                    {
                        var remaining = Enumerable.Range(0, _imageFiles.Count)
                            .Where(idx => idx != currentImageIndex)
                            .OrderBy(_ => _random.Next())
                            .ToList();
                        _displayOrder = new List<int> { currentImageIndex };
                        _displayOrder.AddRange(remaining);
                        _displayIndex = 0;
                    }
                    else
                    {
                        _displayOrder = Enumerable.Range(0, _imageFiles.Count).ToList();
                        _displayIndex = currentImageIndex;
                    }
                }
            }
        }

        private void LoadFolder(string folderPath, bool keepCurrentImage)
        {
            try
            {
                string? currentImagePath = (keepCurrentImage && _imageFiles.Count > 0 && _displayIndex >= 0 && _displayIndex < _displayOrder.Count)
                    ? _imageFiles[_displayOrder[_displayIndex]]
                    : null;

                string[] extensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico" };
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = _recurseSubdirectories,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                var foundFiles = Directory.EnumerateFiles(folderPath, "*.*", options)
                    .Where(file => extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (foundFiles.Count > 0)
                {
                    _currentFolderPath = folderPath;
                    _imageFiles = foundFiles;

                    if (_isRandomized)
                    {
                        var list = Enumerable.Range(0, _imageFiles.Count).ToList();
                        for (int i = list.Count - 1; i > 0; i--)
                        {
                            int j = _random.Next(i + 1);
                            (list[i], list[j]) = (list[j], list[i]);
                        }
                        _displayOrder = list;

                        if (keepCurrentImage && currentImagePath != null)
                        {
                            int newIdx = _imageFiles.IndexOf(currentImagePath);
                            if (newIdx >= 0)
                            {
                                int orderPos = _displayOrder.IndexOf(newIdx);
                                _displayIndex = orderPos >= 0 ? orderPos : 0;
                            }
                            else
                            {
                                _displayIndex = -1;
                                NextImage();
                            }
                        }
                        else
                        {
                            _displayIndex = -1;
                            NextImage();
                        }
                    }
                    else
                    {
                        _displayOrder = Enumerable.Range(0, _imageFiles.Count).ToList();

                        if (keepCurrentImage && currentImagePath != null)
                        {
                            int newIdx = _imageFiles.IndexOf(currentImagePath);
                            _displayIndex = newIdx >= 0 ? newIdx : 0;
                            if (newIdx < 0)
                            {
                                _displayIndex = -1;
                                NextImage();
                            }
                        }
                        else
                        {
                            _displayIndex = -1;
                            NextImage();
                        }
                    }

                    _timer.Start();
                    if (PlayPauseMenuItem != null)
                    {
                        PlayPauseMenuItem.Header = "Pause Slideshow";
                    }
                }
                else
                {
                    _timer.Stop();
                    _imageFiles.Clear();
                    _displayOrder.Clear();
                    _displayIndex = -1;
                    SlideshowImage.Source = null;
                    PlaceholderBorder.Visibility = Visibility.Visible;
                    if (PlayPauseMenuItem != null)
                    {
                        PlayPauseMenuItem.Header = "Resume Slideshow";
                    }
                    MessageBox.Show("No compatible images found in the selected folder.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
                if (PlayPauseMenuItem != null)
                {
                    PlayPauseMenuItem.Header = "Resume Slideshow";
                }
            }
            else
            {
                if (_imageFiles.Count > 0)
                {
                    _timer.Start();
                    if (PlayPauseMenuItem != null)
                    {
                        PlayPauseMenuItem.Header = "Pause Slideshow";
                    }
                }
            }
        }

        private void PrevImage_Click(object sender, RoutedEventArgs e)
        {
            PrevImage();
        }

        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            NextImage();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            NextImage();
        }

        private void ReshuffleDisplayOrder()
        {
            if (_imageFiles.Count <= 1) return;

            int lastShownImageIndex = (_displayIndex >= 0 && _displayIndex < _displayOrder.Count)
                ? _displayOrder[_displayIndex]
                : -1;

            var list = Enumerable.Range(0, _imageFiles.Count).ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            // Prevent repeating the same image back-to-back across the loop boundary
            if (list.Count > 1 && list[0] == lastShownImageIndex)
            {
                int swapIdx = _random.Next(1, list.Count);
                (list[0], list[swapIdx]) = (list[swapIdx], list[0]);
            }

            _displayOrder = list;
        }

        private void NextImage()
        {
            if (_imageFiles.Count == 0 || _displayOrder.Count == 0) return;

            int attempts = 0;
            while (attempts < _displayOrder.Count)
            {
                _displayIndex++;
                if (_displayIndex >= _displayOrder.Count)
                {
                    // Slideshow loops continuously until program is exited
                    if (_isRandomized)
                    {
                        ReshuffleDisplayOrder();
                    }
                    _displayIndex = 0;
                }

                int imageIndex = _displayOrder[_displayIndex];
                if (imageIndex >= 0 && imageIndex < _imageFiles.Count)
                {
                    if (TryDisplayImage(_imageFiles[imageIndex]))
                    {
                        return;
                    }
                }
                attempts++;
            }

            // All images in the list failed to load
            _timer.Stop();
            SlideshowImage.Source = null;
            PlaceholderBorder.Visibility = Visibility.Visible;
            if (PlayPauseMenuItem != null)
            {
                PlayPauseMenuItem.Header = "Resume Slideshow";
            }
            MessageBox.Show("Unable to load any images from the selected folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void PrevImage()
        {
            if (_imageFiles.Count == 0 || _displayOrder.Count == 0) return;

            int attempts = 0;
            while (attempts < _displayOrder.Count)
            {
                _displayIndex--;
                if (_displayIndex < 0)
                {
                    _displayIndex = _displayOrder.Count - 1;
                }

                int imageIndex = _displayOrder[_displayIndex];
                if (imageIndex >= 0 && imageIndex < _imageFiles.Count)
                {
                    if (TryDisplayImage(_imageFiles[imageIndex]))
                    {
                        return;
                    }
                }
                attempts++;
            }

            // All images in the list failed to load
            _timer.Stop();
            SlideshowImage.Source = null;
            PlaceholderBorder.Visibility = Visibility.Visible;
            if (PlayPauseMenuItem != null)
            {
                PlayPauseMenuItem.Header = "Resume Slideshow";
            }
        }

        private bool TryDisplayImage(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                // Load image into a MemoryStream to release file lock immediately
                byte[] imageBytes = File.ReadAllBytes(filePath);
                using var ms = new MemoryStream(imageBytes);

                // Detect EXIF orientation (crucial for phone photos like Samsung Galaxy S23, iPhone, Pixel)
                int orientation = GetExifOrientationFromBytes(imageBytes);
                if (orientation <= 1)
                {
                    orientation = GetOrientationFromMetadata(ms);
                    ms.Position = 0;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;

                // Downscale excessively large photos (e.g. 48MP/200MP camera images) to prevent multi-gigabyte memory leaks
                int maxScreenDim = Math.Max((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
                if (maxScreenDim > 0 && maxScreenDim < 3840)
                {
                    bitmap.DecodePixelWidth = Math.Min(maxScreenDim, 2560);
                }

                bitmap.EndInit();
                bitmap.Freeze(); // Detaches from UI thread and drastically cuts memory overhead

                // Apply EXIF orientation transform if needed so vertical phone photos display upright
                BitmapSource finalImage = bitmap;
                Transform? transform = GetExifTransform(orientation);
                if (transform != null)
                {
                    var transformed = new TransformedBitmap();
                    transformed.BeginInit();
                    transformed.Source = bitmap;
                    transformed.Transform = transform;
                    transformed.EndInit();
                    transformed.Freeze();
                    finalImage = transformed;
                }

                _isAdjustingOrientation = true;

                // Adjust window aspect based on image orientation (using post-transformed dimensions!)
                double currentWidth = this.Width;
                double currentHeight = this.Height;

                if (finalImage.PixelWidth >= finalImage.PixelHeight)
                {
                    // Image is Landscape: Width should be the larger dimension
                    if (currentHeight > currentWidth)
                    {
                        this.Width = Math.Max(currentHeight, this.MinWidth);
                        this.Height = Math.Max(currentWidth, this.MinHeight);
                    }
                }
                else
                {
                    // Image is Portrait: Height should be the larger dimension
                    if (currentWidth > currentHeight)
                    {
                        this.Width = Math.Max(currentHeight, this.MinWidth);
                        this.Height = Math.Max(currentWidth, this.MinHeight);
                    }
                }

                // Explicitly clear previous image source to release bitmap handles
                SlideshowImage.Source = null;
                SlideshowImage.Source = finalImage;

                PlaceholderBorder.Visibility = Visibility.Collapsed;
                _isAdjustingOrientation = false;
                return true;
            }
            catch
            {
                _isAdjustingOrientation = false;
                return false;
            }
        }

        // Map EXIF orientation tag (1-8) to WPF Transform
        private static Transform? GetExifTransform(int orientation)
        {
            switch (orientation)
            {
                case 2: // Flip Horizontal
                    return new ScaleTransform(-1, 1);
                case 3: // Rotate 180
                    return new RotateTransform(180);
                case 4: // Flip Vertical
                    return new ScaleTransform(1, -1);
                case 5: // Transpose (Rotate 270 CW + Flip Horizontal)
                    var g5 = new TransformGroup();
                    g5.Children.Add(new RotateTransform(270));
                    g5.Children.Add(new ScaleTransform(-1, 1));
                    return g5;
                case 6: // Rotate 90 CW (Standard smartphone portrait photo)
                    return new RotateTransform(90);
                case 7: // Transverse (Rotate 90 CW + Flip Horizontal)
                    var g7 = new TransformGroup();
                    g7.Children.Add(new RotateTransform(90));
                    g7.Children.Add(new ScaleTransform(-1, 1));
                    return g7;
                case 8: // Rotate 270 CW (or 90 CCW)
                    return new RotateTransform(270);
                default:
                    return null;
            }
        }

        // Fast zero-allocation byte scanner for EXIF Orientation tag in JPEG files
        private static int GetExifOrientationFromBytes(byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length < 14) return 1;

                // Check JPEG SOI (0xFF, 0xD8)
                if (bytes[0] != 0xFF || bytes[1] != 0xD8) return 1;

                int index = 2;
                while (index + 4 < bytes.Length)
                {
                    if (bytes[index] != 0xFF) return 1;

                    byte marker = bytes[index + 1];
                    // SOS (Start of Scan) or EOI (End of Image)
                    if (marker == 0xDA || marker == 0xD9) break;

                    int length = (bytes[index + 2] << 8) | bytes[index + 3];
                    if (length < 2 || index + 2 + length > bytes.Length) break;

                    // 0xE1 is APP1 (EXIF marker)
                    if (marker == 0xE1 && length >= 14)
                    {
                        // Check for "Exif\0\0" header
                        if (bytes[index + 4] == 'E' &&
                            bytes[index + 5] == 'x' &&
                            bytes[index + 6] == 'i' &&
                            bytes[index + 7] == 'f' &&
                            bytes[index + 8] == 0 &&
                            bytes[index + 9] == 0)
                        {
                            int tiffStart = index + 10;
                            bool isLittleEndian = bytes[tiffStart] == 'I' && bytes[tiffStart + 1] == 'I';
                            bool isBigEndian = bytes[tiffStart] == 'M' && bytes[tiffStart + 1] == 'M';
                            if (!isLittleEndian && !isBigEndian) return 1;

                            ushort ReadU16(int offset)
                            {
                                return isLittleEndian
                                    ? (ushort)(bytes[offset] | (bytes[offset + 1] << 8))
                                    : (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
                            }

                            uint ReadU32(int offset)
                            {
                                return isLittleEndian
                                    ? (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24))
                                    : (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
                            }

                            ushort magic = ReadU16(tiffStart + 2);
                            if (magic != 42) return 1;

                            uint ifd0Offset = ReadU32(tiffStart + 4);
                            int ifd0Pos = tiffStart + (int)ifd0Offset;
                            if (ifd0Pos + 2 > bytes.Length) return 1;

                            ushort entriesCount = ReadU16(ifd0Pos);
                            int entryPos = ifd0Pos + 2;

                            for (int i = 0; i < entriesCount && entryPos + 12 <= bytes.Length; i++, entryPos += 12)
                            {
                                ushort tag = ReadU16(entryPos);
                                if (tag == 0x0112) // Orientation tag
                                {
                                    ushort val = ReadU16(entryPos + 8);
                                    if (val >= 1 && val <= 8)
                                    {
                                        return val;
                                    }
                                }
                            }
                        }
                    }

                    index += 2 + length;
                }
            }
            catch { }

            return 1;
        }

        // Secondary fallback to query WIC BitmapMetadata for EXIF orientation
        private static int GetOrientationFromMetadata(MemoryStream stream)
        {
            try
            {
                stream.Position = 0;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0 && decoder.Frames[0].Metadata is BitmapMetadata metadata)
                {
                    string[] queries = {
                        "/app1/ifd/{ushort=274}",
                        "/app1/ifd/exif/{ushort=274}",
                        "/ifd/{ushort=274}",
                        "System.Photo.Orientation"
                    };
                    foreach (var q in queries)
                    {
                        try
                        {
                            if (metadata.ContainsQuery(q))
                            {
                                object? val = metadata.GetQuery(q);
                                if (val != null)
                                {
                                    int orientation = Convert.ToInt32(val);
                                    if (orientation >= 1 && orientation <= 8)
                                    {
                                        return orientation;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return 1;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isAdjustingOrientation || SlideshowImage.Source == null) return;
        }

        // Interval slider adjustment
        private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int seconds = (int)Math.Round(e.NewValue);
            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromSeconds(seconds);
            }
            if (IntervalValueText != null)
            {
                IntervalValueText.Text = $"{seconds}s";
            }
        }

        // Quick presets for interval
        private void IntervalPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && int.TryParse(menuItem.Tag?.ToString(), out int seconds))
            {
                if (seconds < 1) seconds = 1;

                if (_timer != null)
                {
                    _timer.Interval = TimeSpan.FromSeconds(seconds);
                }

                if (IntervalSlider != null)
                {
                    if (seconds <= IntervalSlider.Maximum)
                    {
                        IntervalSlider.Value = seconds;
                    }
                }

                if (IntervalValueText != null)
                {
                    if (seconds < 60)
                    {
                        IntervalValueText.Text = $"{seconds}s";
                    }
                    else
                    {
                        IntervalValueText.Text = $"{seconds / 60}m";
                    }
                }
            }
        }

        // Opacity slider adjustment
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.Opacity = e.NewValue;
            if (OpacityValueText != null)
            {
                OpacityValueText.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
            }
        }

        // Allow mouse wheel adjustments on sliders
        private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                double delta = e.Delta > 0 ? slider.SmallChange : -slider.SmallChange;
                slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
                e.Handled = true;
            }
        }

        // Always on Top toggle
        private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                this.Topmost = menuItem.IsChecked;
            }
        }

        // Window Shadow toggle
        private void Shadow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                SetShadowEnabled(menuItem.IsChecked);
            }
        }

        public void SetShadowEnabled(bool enabled)
        {
            if (ImageDropShadow != null)
            {
                ImageDropShadow.Opacity = enabled ? 0.35 : 0.0;
            }
            if (PlaceholderBorder?.Effect is DropShadowEffect placeholderShadow)
            {
                placeholderShadow.Opacity = enabled ? 0.30 : 0.0;
            }
            if (ShadowMenuItem != null)
            {
                ShadowMenuItem.IsChecked = enabled;
            }
        }

        // Spawn another instance / window
        private void NewWindow_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new MainWindow
            {
                Left = this.Left + 30,
                Top = this.Top + 30,
                Topmost = this.Topmost
            };
            if (newWindow.AlwaysOnTopMenuItem != null)
            {
                newWindow.AlwaysOnTopMenuItem.IsChecked = this.Topmost;
            }
            if (ShadowMenuItem != null)
            {
                newWindow.SetShadowEnabled(ShadowMenuItem.IsChecked);
            }
            newWindow._recurseSubdirectories = this._recurseSubdirectories;
            if (newWindow.RecurseSubdirectoriesMenuItem != null)
            {
                newWindow.RecurseSubdirectoriesMenuItem.IsChecked = this._recurseSubdirectories;
            }
            newWindow._isRandomized = this._isRandomized;
            if (newWindow.RandomizeOrderMenuItem != null)
            {
                newWindow.RandomizeOrderMenuItem.IsChecked = this._isRandomized;
            }
            newWindow.Show();
        }

        // Close only this widget
        private void CloseWidget_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Exit all widgets
        private void ExitAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToList())
            {
                try
                {
                    window.Close();
                }
                catch { }
            }
            Application.Current.Shutdown();
        }

        // Clean up all resources when window is closed
        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            SlideshowImage.Source = null;
            _imageFiles.Clear();
            _displayOrder.Clear();
            base.OnClosed(e);
        }
    }
}
