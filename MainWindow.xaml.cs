using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace CyberpunkSlideshowWidget
{
    public partial class MainWindow : Window
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
        };

        private static readonly Random _random = new Random();

        // Cached pre-frozen transforms for EXIF orientation tags (indices 0..8)
        private static readonly Transform?[] ExifTransforms = CreateExifTransforms();

        // Cached pre-frozen solid color brushes for CRT glitches to eliminate frame allocation churn
        private static readonly Brush[] GlitchBrushes = CreateGlitchBrushes();

        // Shared static retro CRT static noise frames (256x256 frozen bitmaps, created lazily once)
        private static readonly List<WriteableBitmap> SharedSnowBitmaps = new List<WriteableBitmap>();
        private static readonly object SnowLock = new object();

        // Pre-frozen rainbow rotation animation
        private static readonly DoubleAnimation RainbowAnimation = CreateRainbowAnimation();

        // Instance state
        private List<string> _imageFiles = new List<string>();
        private List<int> _displayOrder = new List<int>();
        private int _displayIndex = -1;
        private readonly DispatcherTimer _timer;
        private bool _isAdjustingOrientation = false;
        private bool _recurseSubdirectories = false;
        private bool _isRandomized = false;
        private string? _currentFolderPath = null;
        private bool _isInitialized = false;
        private bool _isClosed = false;

        // Effects state
        private bool _rainbowBorderEnabled = false;
        private double _borderWidth = 4.0;

        private bool _crtScanlinesEnabled = false;
        private double _scanlineThickness = 3.0;

        private bool _crtGlitchEnabled = false;
        private double _glitchChance = 15.0;
        private readonly DispatcherTimer _glitchTimer;
        private int _glitchCooldown = 0;

        private bool _crtSnowEnabled = false;
        private double _snowAmount = 25.0;
        private readonly DispatcherTimer _snowTimer;
        private int _snowFrameIndex = 0;

        private readonly DispatcherTimer _effectsCloseTimer;
        private FrameworkElement? _subscribedPopupChild = null;

        public MainWindow()
        {
            // Set up slideshow interval timer (default: 5 seconds)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += Timer_Tick;

            // Set up CRT glitch timer (default: 120ms tick rate)
            _glitchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _glitchTimer.Tick += GlitchTimer_Tick;

            // Set up CRT snow static animation timer (default: 65ms tick rate, ~15 fps)
            _snowTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(65)
            };
            _snowTimer.Tick += SnowTimer_Tick;

            // Set up Effects submenu close delay timer (700ms) to ensure smooth traversal across menu
            _effectsCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _effectsCloseTimer.Tick += EffectsCloseTimer_Tick;

            InitializeComponent();

            _isInitialized = true;

            // Initialize scanlines brush
            UpdateScanlinesBrush(_scanlineThickness);

            // Handle display / monitor changes (e.g. resolution changes, sleep/wake, multi-monitor shifts)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private static DoubleAnimation CreateRainbowAnimation()
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(5)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            anim.Freeze();
            return anim;
        }

        private static Brush[] CreateGlitchBrushes()
        {
            Color[] colors =
            {
                Color.FromArgb(220, 0, 240, 255),   // Neon cyan
                Color.FromArgb(220, 255, 0, 127),   // Neon magenta
                Color.FromArgb(240, 255, 255, 255), // CRT phosphor white
                Color.FromArgb(235, 0, 0, 0),       // Deep horizontal scan dropout
                Color.FromArgb(210, 0, 255, 102),   // Neon phosphor lime
                Color.FromArgb(200, 255, 230, 0)    // Cyber yellow
            };

            var brushes = new Brush[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                var brush = new SolidColorBrush(colors[i]);
                brush.Freeze();
                brushes[i] = brush;
            }
            return brushes;
        }

        private static Transform?[] CreateExifTransforms()
        {
            var transforms = new Transform?[9];

            // 1: Normal (null)
            transforms[1] = null;

            // 2: Flip Horizontal
            var t2 = new ScaleTransform(-1, 1);
            t2.Freeze();
            transforms[2] = t2;

            // 3: Rotate 180
            var t3 = new RotateTransform(180);
            t3.Freeze();
            transforms[3] = t3;

            // 4: Flip Vertical
            var t4 = new ScaleTransform(1, -1);
            t4.Freeze();
            transforms[4] = t4;

            // 5: Transpose (Rotate 270 CW + Flip Horizontal)
            var g5 = new TransformGroup();
            g5.Children.Add(new RotateTransform(270));
            g5.Children.Add(new ScaleTransform(-1, 1));
            g5.Freeze();
            transforms[5] = g5;

            // 6: Rotate 90 CW (Standard smartphone portrait photo)
            var t6 = new RotateTransform(90);
            t6.Freeze();
            transforms[6] = t6;

            // 7: Transverse (Rotate 90 CW + Flip Horizontal)
            var g7 = new TransformGroup();
            g7.Children.Add(new RotateTransform(90));
            g7.Children.Add(new ScaleTransform(-1, 1));
            g7.Freeze();
            transforms[7] = g7;

            // 8: Rotate 270 CW (or 90 CCW)
            var t8 = new RotateTransform(270);
            t8.Freeze();
            transforms[8] = t8;

            return transforms;
        }

        private static List<WriteableBitmap> GetOrCreateSnowBitmaps()
        {
            if (SharedSnowBitmaps.Count == 8)
            {
                return SharedSnowBitmaps;
            }

            lock (SnowLock)
            {
                if (SharedSnowBitmaps.Count == 8)
                {
                    return SharedSnowBitmaps;
                }

                int w = 256, h = 256;
                for (int f = 0; f < 8; f++)
                {
                    var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                    int stride = w * 4;
                    byte[] pixels = new byte[stride * h];
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte val = (byte)_random.Next(256);
                        bool colorSpeck = _random.Next(30) == 0;
                        pixels[i] = colorSpeck ? (byte)_random.Next(256) : val;     // B
                        pixels[i + 1] = colorSpeck ? (byte)_random.Next(256) : val; // G
                        pixels[i + 2] = colorSpeck ? (byte)_random.Next(256) : val; // R
                        pixels[i + 3] = 255;
                    }
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    wb.Freeze();
                    SharedSnowBitmaps.Add(wb);
                }
                return SharedSnowBitmaps;
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_isClosed) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (!_isClosed)
                {
                    this.InvalidateVisual();
                }
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

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = _recurseSubdirectories,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                var foundFiles = Directory.EnumerateFiles(folderPath, "*.*", options)
                    .Where(file => SupportedExtensions.Contains(Path.GetExtension(file)))
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
            if (_isClosed || _imageFiles.Count == 0 || _displayOrder.Count == 0) return;

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
            if (_isClosed || _imageFiles.Count == 0 || _displayOrder.Count == 0) return;

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

                // Open with non-locking file sharing so files on disk are never locked
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                // Fast JPEG EXIF byte scan on first 64KB without loading entire file into LOH memory
                int orientation = 1;
                string ext = Path.GetExtension(filePath);
                if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] headerBytes = new byte[Math.Min(65536, (int)fs.Length)];
                    int bytesRead = fs.Read(headerBytes, 0, headerBytes.Length);
                    orientation = GetExifOrientationFromBytes(headerBytes, bytesRead);
                    fs.Position = 0;
                }

                // Header inspection with DelayCreation to inspect dimensions and metadata without rasterizing pixels
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0) return false;

                var frame = decoder.Frames[0];
                if (orientation <= 1 && frame.Metadata is BitmapMetadata metadata)
                {
                    orientation = GetOrientationFromMetadata(metadata);
                }

                int origWidth = frame.PixelWidth;
                int origHeight = frame.PixelHeight;

                // Max allowable dimension bounded by screen resolution (supports 1080p, 1440p, 4K)
                int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
                int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
                int maxScreenDim = Math.Max(screenWidth, screenHeight);
                if (maxScreenDim <= 0) maxScreenDim = 1920;
                int maxAllowedDim = Math.Min(Math.Max(maxScreenDim, 1920), 3840);

                // Load image into frozen BitmapImage
                fs.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = fs;

                // Downscale ONLY if original exceeds screen bound (never upscale small images/icons)
                if (origWidth > maxAllowedDim || origHeight > maxAllowedDim)
                {
                    if (origWidth >= origHeight)
                    {
                        bitmap.DecodePixelWidth = maxAllowedDim;
                    }
                    else
                    {
                        bitmap.DecodePixelHeight = maxAllowedDim;
                    }
                }

                bitmap.EndInit();
                bitmap.Freeze(); // Detaches from UI thread and drastically cuts memory overhead

                // Apply EXIF orientation transform if needed so vertical phone photos display upright
                BitmapSource finalImage = bitmap;
                Transform? transform = (orientation >= 1 && orientation <= 8) ? ExifTransforms[orientation] : null;
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
                double currentWidth = double.IsNaN(this.Width) ? this.ActualWidth : this.Width;
                double currentHeight = double.IsNaN(this.Height) ? this.ActualHeight : this.Height;

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

        // Fast zero-allocation byte scanner for EXIF Orientation tag in JPEG files
        private static int GetExifOrientationFromBytes(byte[] bytes, int validLength)
        {
            try
            {
                if (bytes == null || validLength < 14) return 1;

                // Check JPEG SOI (0xFF, 0xD8)
                if (bytes[0] != 0xFF || bytes[1] != 0xD8) return 1;

                int index = 2;
                while (index + 4 < validLength)
                {
                    if (bytes[index] != 0xFF) return 1;

                    byte marker = bytes[index + 1];
                    // SOS (Start of Scan) or EOI (End of Image)
                    if (marker == 0xDA || marker == 0xD9) break;

                    int length = (bytes[index + 2] << 8) | bytes[index + 3];
                    if (length < 2 || index + 2 + length > validLength) break;

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
                            if (ifd0Pos + 2 > validLength) return 1;

                            ushort entriesCount = ReadU16(ifd0Pos);
                            int entryPos = ifd0Pos + 2;

                            for (int i = 0; i < entriesCount && entryPos + 12 <= validLength; i++, entryPos += 12)
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
        private static int GetOrientationFromMetadata(BitmapMetadata metadata)
        {
            try
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

        #region Effects Methods & Handlers

        // -------------------------------------------------------------
        // Cyberpunk Rainbow Border
        // -------------------------------------------------------------
        private void RainbowBorder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                SetRainbowBorderEnabled(menuItem.IsChecked);
            }
        }

        private void BorderWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _borderWidth = e.NewValue;
            if (BorderWidthValueText != null)
            {
                BorderWidthValueText.Text = $"{(int)Math.Round(_borderWidth)}px";
            }

            if (CyberpunkRainbowBorder != null && _rainbowBorderEnabled)
            {
                CyberpunkRainbowBorder.BorderThickness = new Thickness(_borderWidth);
                if (RainbowGlowEffect != null)
                {
                    RainbowGlowEffect.BlurRadius = Math.Max(8, _borderWidth * 2.5);
                }
            }
        }

        public void SetRainbowBorderEnabled(bool enabled, double? width = null)
        {
            _rainbowBorderEnabled = enabled;
            if (width.HasValue)
            {
                _borderWidth = width.Value;
                if (BorderWidthSlider != null)
                {
                    BorderWidthSlider.Value = _borderWidth;
                }
            }

            if (RainbowBorderMenuItem != null)
            {
                RainbowBorderMenuItem.IsChecked = enabled;
            }

            if (CyberpunkRainbowBorder != null)
            {
                if (enabled)
                {
                    CyberpunkRainbowBorder.Visibility = Visibility.Visible;
                    CyberpunkRainbowBorder.BorderThickness = new Thickness(_borderWidth);
                    if (RainbowGlowEffect != null)
                    {
                        RainbowGlowEffect.BlurRadius = Math.Max(8, _borderWidth * 2.5);
                    }
                    StartRainbowAnimation();
                }
                else
                {
                    CyberpunkRainbowBorder.Visibility = Visibility.Collapsed;
                    StopRainbowAnimation();
                }
            }
        }

        private void StartRainbowAnimation()
        {
            if (RainbowRotateTransform == null) return;
            RainbowRotateTransform.BeginAnimation(RotateTransform.AngleProperty, RainbowAnimation);
        }

        private void StopRainbowAnimation()
        {
            RainbowRotateTransform?.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        // -------------------------------------------------------------
        // Vintage CRT Scanlines
        // -------------------------------------------------------------
        private void CrtScanlines_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                SetCrtScanlinesEnabled(menuItem.IsChecked);
            }
        }

        private void ScanlineThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _scanlineThickness = e.NewValue;
            if (ScanlineThicknessValueText != null)
            {
                ScanlineThicknessValueText.Text = $"{(int)Math.Round(_scanlineThickness)}px";
            }

            if (_crtScanlinesEnabled)
            {
                UpdateScanlinesBrush(_scanlineThickness);
            }
        }

        public void SetCrtScanlinesEnabled(bool enabled, double? thickness = null)
        {
            _crtScanlinesEnabled = enabled;
            if (thickness.HasValue)
            {
                _scanlineThickness = thickness.Value;
                if (ScanlineThicknessSlider != null)
                {
                    ScanlineThicknessSlider.Value = _scanlineThickness;
                }
            }

            if (CrtScanlinesMenuItem != null)
            {
                CrtScanlinesMenuItem.IsChecked = enabled;
            }

            if (CrtScanlinesOverlay != null)
            {
                CrtScanlinesOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (enabled)
                {
                    UpdateScanlinesBrush(_scanlineThickness);
                }
            }
        }

        private void UpdateScanlinesBrush(double thickness)
        {
            if (CrtDrawingBrush == null) return;
            double lineThickness = Math.Max(1.0, Math.Round(thickness));
            double period = lineThickness * 2.0;

            var darkBrush = new SolidColorBrush(Color.FromArgb(245, 0, 0, 0));
            darkBrush.Freeze();

            var transparentRect = new RectangleGeometry(new Rect(0, 0, 1, period));
            transparentRect.Freeze();

            var darkRect = new RectangleGeometry(new Rect(0, 0, 1, lineThickness));
            darkRect.Freeze();

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, transparentRect));
            group.Children.Add(new GeometryDrawing(darkBrush, null, darkRect));
            group.Freeze();

            CrtDrawingBrush.Viewport = new Rect(0, 0, 1, period);
            CrtDrawingBrush.Drawing = group;
        }

        // -------------------------------------------------------------
        // CRT Glitch
        // -------------------------------------------------------------
        private void CrtGlitch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                if (menuItem.IsChecked && _glitchChance <= 0)
                {
                    _glitchChance = 15.0;
                    if (GlitchChanceSlider != null)
                    {
                        GlitchChanceSlider.Value = 15.0;
                    }
                }
                SetCrtGlitchEnabled(menuItem.IsChecked);
            }
        }

        private void GlitchChanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _glitchChance = e.NewValue;
            if (GlitchChanceValueText != null)
            {
                GlitchChanceValueText.Text = $"{(int)Math.Round(_glitchChance)}%";
            }

            if (!_isInitialized) return;

            // Adjusting slider enables glitch if > 0%, or disables if 0%
            if (_glitchChance > 0 && !_crtGlitchEnabled)
            {
                SetCrtGlitchEnabled(true);
            }
            else if (_glitchChance <= 0 && _crtGlitchEnabled)
            {
                SetCrtGlitchEnabled(false);
            }
        }

        public void SetCrtGlitchEnabled(bool enabled, double? chance = null)
        {
            _crtGlitchEnabled = enabled;
            if (chance.HasValue)
            {
                _glitchChance = chance.Value;
                if (GlitchChanceSlider != null)
                {
                    GlitchChanceSlider.Value = _glitchChance;
                }
            }

            if (CrtGlitchMenuItem != null)
            {
                CrtGlitchMenuItem.IsChecked = enabled;
            }

            if (enabled)
            {
                if (_glitchTimer != null && !_glitchTimer.IsEnabled)
                {
                    _glitchTimer.Start();
                }
            }
            else
            {
                _glitchTimer?.Stop();
                ResetGlitchState();
            }
        }

        private void GlitchTimer_Tick(object? sender, EventArgs e)
        {
            if (_isClosed || !_crtGlitchEnabled || _glitchChance <= 0)
            {
                ResetGlitchState();
                return;
            }

            if (_glitchCooldown > 0)
            {
                _glitchCooldown--;
                if (_glitchCooldown == 0)
                {
                    ResetGlitchState();
                }
                return;
            }

            double roll = _random.NextDouble() * 100.0;
            if (roll < _glitchChance)
            {
                TriggerGlitch();
                _glitchCooldown = _random.Next(1, 3); // Active for 1-2 ticks (~120ms to 240ms)
            }
            else
            {
                ResetGlitchState();
            }
        }

        private void TriggerGlitch()
        {
            // Glitch slices and chromatic tear bars scaled for high-resolution displays
            if (CrtGlitchCanvas != null)
            {
                CrtGlitchCanvas.Children.Clear();
                CrtGlitchCanvas.Visibility = Visibility.Visible;

                double canvasWidth = CrtGlitchCanvas.ActualWidth > 0 ? CrtGlitchCanvas.ActualWidth : this.Width;
                double canvasHeight = CrtGlitchCanvas.ActualHeight > 0 ? CrtGlitchCanvas.ActualHeight : this.Height;

                // Scale bar heights proportionally to canvas height so glitches look substantial on 1440p/4K
                double minBarHeight = Math.Max(6, canvasHeight * 0.02);
                double maxBarHeight = Math.Max(28, canvasHeight * 0.12);

                int barCount = _random.Next(2, 6);

                for (int i = 0; i < barCount; i++)
                {
                    double barHeight = minBarHeight + (_random.NextDouble() * (maxBarHeight - minBarHeight));
                    double barY = _random.NextDouble() * Math.Max(10, canvasHeight - barHeight);
                    double barX = (_random.NextDouble() * 60.0) - 30.0;
                    var rect = new Rectangle
                    {
                        Width = canvasWidth + 80,
                        Height = barHeight,
                        Fill = GlitchBrushes[_random.Next(GlitchBrushes.Length)],
                        Opacity = 0.70 + (_random.NextDouble() * 0.30)
                    };
                    Canvas.SetLeft(rect, barX);
                    Canvas.SetTop(rect, barY);
                    CrtGlitchCanvas.Children.Add(rect);
                }
            }

            // Jitter image horizontally (tracking sync slip), scaled for high-res screens
            if (ImageJitterTransform != null)
            {
                double canvasWidth = CrtGlitchCanvas?.ActualWidth > 0 ? CrtGlitchCanvas.ActualWidth : this.Width;
                double maxJitter = Math.Max(8.0, Math.Min(30.0, canvasWidth * 0.04));
                ImageJitterTransform.X = (_random.NextDouble() * (maxJitter * 2.0)) - maxJitter;
            }
        }

        private void ResetGlitchState()
        {
            if (CrtGlitchCanvas != null)
            {
                CrtGlitchCanvas.Visibility = Visibility.Collapsed;
                CrtGlitchCanvas.Children.Clear();
            }
            if (ImageJitterTransform != null)
            {
                ImageJitterTransform.X = 0;
            }
        }

        // -------------------------------------------------------------
        // CRT Snow
        // -------------------------------------------------------------
        private void SnowTimer_Tick(object? sender, EventArgs e)
        {
            if (_isClosed || !_crtSnowEnabled || CrtSnowOverlay == null)
            {
                return;
            }

            var bitmaps = GetOrCreateSnowBitmaps();
            if (bitmaps.Count == 0) return;

            _snowFrameIndex = (_snowFrameIndex + 1) % bitmaps.Count;
            CrtSnowOverlay.Source = bitmaps[_snowFrameIndex];
        }

        private void CrtSnow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                if (menuItem.IsChecked && _snowAmount <= 0)
                {
                    _snowAmount = 25.0;
                    if (SnowAmountSlider != null)
                    {
                        SnowAmountSlider.Value = 25.0;
                    }
                }
                SetCrtSnowEnabled(menuItem.IsChecked);
            }
        }

        private void SnowAmountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _snowAmount = e.NewValue;
            if (SnowAmountValueText != null)
            {
                SnowAmountValueText.Text = $"{(int)Math.Round(_snowAmount)}%";
            }

            if (!_isInitialized) return;

            // Adjusting slider enables snow if > 0%, or disables if 0%
            if (_snowAmount > 0 && !_crtSnowEnabled)
            {
                SetCrtSnowEnabled(true);
            }
            else if (_snowAmount <= 0 && _crtSnowEnabled)
            {
                SetCrtSnowEnabled(false);
            }
            else if (_crtSnowEnabled && CrtSnowOverlay != null)
            {
                UpdateSnowOpacity();
            }
        }

        private void UpdateSnowOpacity()
        {
            if (CrtSnowOverlay == null) return;
            // Map 0% - 100% strength to 0.0 - 0.95 opacity for deep CRT noise density
            double opacity = Math.Clamp((_snowAmount / 100.0) * 0.95, 0.0, 0.95);
            CrtSnowOverlay.Opacity = opacity;
        }

        public void SetCrtSnowEnabled(bool enabled, double? amount = null)
        {
            _crtSnowEnabled = enabled;
            if (amount.HasValue)
            {
                _snowAmount = amount.Value;
                if (SnowAmountSlider != null)
                {
                    SnowAmountSlider.Value = _snowAmount;
                }
            }

            if (CrtSnowMenuItem != null)
            {
                CrtSnowMenuItem.IsChecked = enabled;
            }

            if (CrtSnowOverlay != null)
            {
                if (enabled)
                {
                    UpdateSnowOpacity();
                    var bitmaps = GetOrCreateSnowBitmaps();
                    if (bitmaps.Count > 0 && CrtSnowOverlay.Source == null)
                    {
                        CrtSnowOverlay.Source = bitmaps[0];
                    }
                    CrtSnowOverlay.Visibility = Visibility.Visible;
                    if (_snowTimer != null && !_snowTimer.IsEnabled)
                    {
                        _snowTimer.Start();
                    }
                }
                else
                {
                    _snowTimer?.Stop();
                    CrtSnowOverlay.Visibility = Visibility.Collapsed;
                    CrtSnowOverlay.Opacity = 0.0;
                    CrtSnowOverlay.Source = null;
                }
            }
        }

        #endregion

        #region Effects Submenu Hover Delay & Smart Placement

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // Smart Context Menu Placement:
        // Guarantees menu fits on screen and Effects submenu opens predictably
        private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (MainContextMenu == null) return;

            if (GetCursorPos(out POINT pt))
            {
                IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                MONITORINFO info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    double screenRightDip = info.rcWork.Right / dpi.DpiScaleX;
                    double screenLeftDip = info.rcWork.Left / dpi.DpiScaleX;
                    double screenBottomDip = info.rcWork.Bottom / dpi.DpiScaleY;
                    double screenTopDip = info.rcWork.Top / dpi.DpiScaleY;
                    double cursorXDip = pt.X / dpi.DpiScaleX;
                    double cursorYDip = pt.Y / dpi.DpiScaleY;

                    const double requiredSpaceRight = 500.0;

                    if (cursorXDip + requiredSpaceRight > screenRightDip)
                    {
                        // Near the right monitor edge: position MainContextMenu to leave room on right
                        double targetLeft = screenRightDip - requiredSpaceRight;
                        MainContextMenu.Placement = PlacementMode.AbsolutePoint;
                        MainContextMenu.HorizontalOffset = Math.Max(screenLeftDip + 10, targetLeft);

                        // Safe vertical clamp that prevents ArgumentException when min > max on small/scaled displays
                        double minTop = screenTopDip + 10;
                        double maxTop = Math.Max(minTop, screenBottomDip - 500);
                        MainContextMenu.VerticalOffset = Math.Clamp(cursorYDip - 20, minTop, maxTop);
                    }
                    else
                    {
                        MainContextMenu.Placement = PlacementMode.MousePoint;
                        MainContextMenu.HorizontalOffset = 0;
                        MainContextMenu.VerticalOffset = 0;
                    }
                }
            }
        }

        private Popup? GetEffectsPopup()
        {
            if (EffectsMenuItem == null) return null;

            var popup = EffectsMenuItem.Template?.FindName("PART_Popup", EffectsMenuItem) as Popup;
            if (popup != null) return popup;

            EffectsMenuItem.ApplyTemplate();
            popup = EffectsMenuItem.Template?.FindName("PART_Popup", EffectsMenuItem) as Popup;
            if (popup != null) return popup;

            return FindVisualChild<Popup>(EffectsMenuItem);
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        private FrameworkElement? GetEffectsPopupChild()
        {
            var popup = GetEffectsPopup();
            return popup?.Child as FrameworkElement;
        }

        private bool IsElementInEffects(DependencyObject? element)
        {
            if (element == null) return false;
            var popupChild = GetEffectsPopupChild();

            DependencyObject? curr = element;
            while (curr != null)
            {
                if (curr == EffectsMenuItem || (popupChild != null && curr == popupChild))
                {
                    return true;
                }

                if (EffectsMenuItem != null && curr is MenuItem item && EffectsMenuItem.Items.Contains(item))
                {
                    return true;
                }

                DependencyObject? parent = null;
                if (curr is Visual visual)
                {
                    parent = VisualTreeHelper.GetParent(visual);
                }

                if (parent == null && curr is FrameworkContentElement fce)
                {
                    parent = fce.Parent;
                }

                if (parent == null)
                {
                    parent = LogicalTreeHelper.GetParent(curr);
                }

                curr = parent;
            }

            return false;
        }

        private void SetSiblingHitTestVisible(bool visible)
        {
            if (MainContextMenu == null) return;
            foreach (var item in MainContextMenu.Items)
            {
                if (item != EffectsMenuItem && item is UIElement uie)
                {
                    uie.IsHitTestVisible = visible;
                }
            }
        }

        private void EffectsCloseTimer_Tick(object? sender, EventArgs e)
        {
            _effectsCloseTimer.Stop();
            if (EffectsMenuItem != null && EffectsMenuItem.IsSubmenuOpen)
            {
                var popupChild = GetEffectsPopupChild();
                bool inPopup = popupChild != null && (popupChild.IsMouseOver || popupChild.IsMouseCaptureWithin);
                if (!EffectsMenuItem.IsMouseOver && !inPopup)
                {
                    EffectsMenuItem.IsSubmenuOpen = false;
                    SetSiblingHitTestVisible(true);
                }
            }
        }

        private void MainContextMenu_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (EffectsMenuItem != null && EffectsMenuItem.IsSubmenuOpen)
            {
                var popupChild = GetEffectsPopupChild();
                bool inPopup = popupChild != null && (popupChild.IsMouseOver || popupChild.IsMouseCaptureWithin);
                if (inPopup)
                {
                    _effectsCloseTimer.Stop();
                    return;
                }

                if (EffectsMenuItem.IsMouseOver)
                {
                    _effectsCloseTimer.Stop();
                    return;
                }

                // Mouse is moving inside MainContextMenu while Effects submenu is open
                if (!_effectsCloseTimer.IsEnabled)
                {
                    _effectsCloseTimer.Start();
                }
            }
        }

        private void MainContextMenu_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (EffectsMenuItem != null && EffectsMenuItem.IsSubmenuOpen)
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;
                if (!IsElementInEffects(source))
                {
                    _effectsCloseTimer.Stop();
                    EffectsMenuItem.IsSubmenuOpen = false;
                    SetSiblingHitTestVisible(true);
                }
            }
        }

        private void MainContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            if (EffectsMenuItem != null)
            {
                EffectsMenuItem.IsSubmenuOpen = false;
            }
            SetSiblingHitTestVisible(true);
            DetachPopupChildHandlers();
        }

        private void EffectsMenuItem_MouseEnter(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
            if (EffectsMenuItem != null)
            {
                EffectsMenuItem.IsSubmenuOpen = true;
            }
        }

        private void EffectsMenuItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (EffectsMenuItem != null && EffectsMenuItem.IsSubmenuOpen)
            {
                var popupChild = GetEffectsPopupChild();
                bool inPopup = popupChild != null && (popupChild.IsMouseOver || popupChild.IsMouseCaptureWithin);
                if (!inPopup)
                {
                    _effectsCloseTimer.Stop();
                    _effectsCloseTimer.Start();
                }
            }
        }

        private void EffectsMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            SetSiblingHitTestVisible(false);
            AttachPopupChildHandlers();
        }

        private void EffectsMenuItem_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            _effectsCloseTimer.Stop();
            SetSiblingHitTestVisible(true);
            DetachPopupChildHandlers();
        }

        private void AttachPopupChildHandlers()
        {
            var popupChild = GetEffectsPopupChild();
            if (popupChild != null && popupChild != _subscribedPopupChild)
            {
                DetachPopupChildHandlers();
                _subscribedPopupChild = popupChild;
                _subscribedPopupChild.PreviewMouseMove += PopupChild_PreviewMouseMove;
                _subscribedPopupChild.MouseLeave += PopupChild_MouseLeave;
            }
        }

        private void DetachPopupChildHandlers()
        {
            if (_subscribedPopupChild != null)
            {
                _subscribedPopupChild.PreviewMouseMove -= PopupChild_PreviewMouseMove;
                _subscribedPopupChild.MouseLeave -= PopupChild_MouseLeave;
                _subscribedPopupChild = null;
            }
        }

        private void PopupChild_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _effectsCloseTimer.Stop();
        }

        private void PopupChild_MouseLeave(object sender, MouseEventArgs e)
        {
            if (EffectsMenuItem != null && EffectsMenuItem.IsSubmenuOpen)
            {
                if (!EffectsMenuItem.IsMouseOver)
                {
                    _effectsCloseTimer.Stop();
                    _effectsCloseTimer.Start();
                }
            }
        }

        #endregion

        // Spawn another instance / window
        private void NewWindow_Click(object sender, RoutedEventArgs e)
        {
            double newLeft = this.Left + 30;
            double newTop = this.Top + 30;

            // Ensure the newly spawned window remains completely on visible desktop bounds
            double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120;
            double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120;
            if (newLeft > maxLeft) newLeft = SystemParameters.VirtualScreenLeft + 40;
            if (newTop > maxTop) newTop = SystemParameters.VirtualScreenTop + 40;

            var newWindow = new MainWindow
            {
                Left = newLeft,
                Top = newTop,
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
            newWindow.SetRainbowBorderEnabled(this._rainbowBorderEnabled, this._borderWidth);
            newWindow.SetCrtScanlinesEnabled(this._crtScanlinesEnabled, this._scanlineThickness);
            newWindow.SetCrtGlitchEnabled(this._crtGlitchEnabled, this._glitchChance);
            newWindow.SetCrtSnowEnabled(this._crtSnowEnabled, this._snowAmount);

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

        // Exit all widgets cleanly
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
            App.CleanProcessExit(0);
        }

        // Clean up all resources when window is closed
        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;

            // Unsubscribe static system display events
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            // Stop and unhook all DispatcherTimers
            _timer.Stop();
            _timer.Tick -= Timer_Tick;

            _glitchTimer.Stop();
            _glitchTimer.Tick -= GlitchTimer_Tick;

            _snowTimer.Stop();
            _snowTimer.Tick -= SnowTimer_Tick;

            _effectsCloseTimer.Stop();
            _effectsCloseTimer.Tick -= EffectsCloseTimer_Tick;

            // Close context menu and detach handlers
            if (MainContextMenu != null)
            {
                MainContextMenu.IsOpen = false;
                MainContextMenu.PreviewMouseMove -= MainContextMenu_PreviewMouseMove;
                MainContextMenu.PreviewMouseLeftButtonDown -= MainContextMenu_PreviewMouseLeftButtonDown;
                MainContextMenu.Closed -= MainContextMenu_Closed;
            }

            SetSiblingHitTestVisible(true);
            DetachPopupChildHandlers();

            // Stop animations and clear visual effects
            StopRainbowAnimation();
            ResetGlitchState();

            if (CrtSnowOverlay != null)
            {
                CrtSnowOverlay.Source = null;
            }

            if (CrtDrawingBrush != null)
            {
                CrtDrawingBrush.Drawing = null;
            }

            if (CyberpunkRainbowBorder != null)
            {
                CyberpunkRainbowBorder.BorderBrush = null;
            }

            // Release image sources and collection references
            SlideshowImage.Source = null;
            _imageFiles.Clear();
            _displayOrder.Clear();

            base.OnClosed(e);

            // Reclaim unmanaged texture memory promptly
            GC.Collect(1, GCCollectionMode.Optimized, false);
        }
    }
}

