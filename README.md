<img width="1589" height="301" alt="image" src="https://github.com/user-attachments/assets/1470d558-51ec-45ea-964a-de4f161a4734" />
# 🖼️ CyberpunkSlideshowWidget

[![.NET](https://img.shields.io/badge/.NET-8.0--windows-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-00ADEF?logo=windows11&logoColor=white)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A modern, lightweight, frameless desktop slideshow widget for Windows. Floats seamlessly over your desktop wallpaper or open applications with interactive transparency, subtle 3D drop shadows, multi-instance support, and zero-file-lock memory optimization.

---

## ✨ Features

- **Borderless & Transparent:** A clean, chrome-free photo frame that blends into your desktop without window title bars or clutter.
- **Subtle 3-D Desktop Shadow:** Soft, realistic drop shadow (`Blur: 16px`, `Depth: 4px`, `Opacity: 35%`) giving images a floating 3D photograph aesthetic on the desktop. Can be toggled on/off at any time.
- **Precise Opacity Slider:** Drag the live opacity slider in the right-click menu from **10% to 100%** (or scroll with your mouse wheel) to achieve the perfect transparency level.
- **Recursive Subdirectory Scanning:** Checkbox option in the native Windows folder picker and context menu to recurse and include all images in nested subdirectories.
- **Continuous Slideshow Looping:** The slideshow loops endlessly (both sequentially and randomized) without stopping until the widget is closed or program exits.
- **Randomized Image Order:** Checkbox option in the right-click menu to shuffle image display order, completing a full pass through the image library before reshuffling.
- **Custom Slideshow Interval:**
  - **1s – 60s Slider:** Fine-tune intervals down to the second with live feedback.
  - **Extended Presets:** 10s, 30s, 1m, 2m, 5m, 10m, and 30m presets for longer ambient photo rotations.
  - **Mouse Wheel Tuning:** Hover over the slider and scroll to bump up or down by 1 second.
- **Multiple Instances & Multi-Window:**
  - Run multiple widgets concurrently on the same or multiple monitors.
  - Spawn new widget instances directly via the right-click **"New Window"** menu option, or by launching the `.exe` multiple times.
  - Each widget operates autonomously with its own selected folder, timer, interval, opacity, position, and orientation.
- **Visual Effects Suite:**
  - **Cyberpunk Rainbow Border:** An animated rotating RGB/neon multi-stop gradient border around the application window with adjustable border width (1px – 20px) and glowing edge halo.
  - **Vintage CRT Scanlines:** Hardware-accelerated alternating horizontal scanlines with deep contrast and real-time thickness tuning (1px – 20px) optimized for high-resolution 1440p and 4K displays.
  - **CRT Glitch Effect:** Retro television sync tears, horizontal scan drops, tracking jitter, and chromatic aberration slices with an adjustable random percentage chance slider (0% – 100%).
  - **CRT Snow Effect:** Continuous analog TV static noise with authentic retro phosphor grain and an adjustable amount-based strength slider (1% – 100%).
- **Always on Top:** Pin your widget above all other windows (great for work desks, monitoring photos, or keeping references visible).
- **Auto EXIF Orientation:** Reads camera metadata from smartphone photos (Samsung Galaxy, iPhone, Pixel, DSLR) so vertical portrait shots display right-side up automatically instead of sideways.
- **Playback Controls:** Pause/Resume slideshow (`⏸`/`▶`), jump to the **Previous Image**, or advance to the **Next Image** manually.
- **Leak-Free Memory & Zero File Locks:**
  - Decodes images via in-memory streams so files on disk are never locked. You can rename, edit, or delete photos while the slideshow is running.
  - Downscales massive camera photos (e.g. 48MP+ DSLR RAW/JPEGs) to display resolution on decode, preventing gigabyte heap bloat.
  - Freezes bitmap sources to detach from UI threads and maximize performance.
- **Display Change & Wake Recovery:** Listens for display resolution and session switches (sleep/wake, lock/unlock) to automatically trigger invalidation repaints, preventing black/frozen window states common in transparent WPF apps.
- **Clean Shutdown:** Unregisters all system hooks, timers, and image handles on close to ensure zero lingering background processes in Windows Task Manager.

---

## 🎛️ Context Menu & Controls

Right-click anywhere on the widget to access the control panel:

| Menu Item | Description |
| :--- | :--- |
| **Select Folder...** | Opens the folder picker (with native **Recurse subdirectories** checkbox) to choose your photo directory. |
| **Recurse Subdirectories** | Checkbox to toggle inclusion of nested subfolders for the current image library. |
| **Randomize Order** | Checkbox to toggle shuffled/randomized playback vs sequential order. |
| **Pause / Resume Slideshow** | Freezes playback on the current image or resumes automatic rotation. |
| **Previous Image** | Steps backward to the previous compatible image in the folder. |
| **Next Image** | Advances forward to the next image immediately. |
| **Interval (1s - 60s)** | Live slider to set the delay between image transitions. |
| **More Intervals...** | Quick presets for longer delays (10s, 30s, 1m, 2m, 5m, 10m, 30m). |
| **Opacity (10% - 100%)** | Real-time slider for precise transparency adjustment. |
| **Effects > Rainbow Border** | Toggles the animated cyberpunk RGB border, with a 1px–20px width slider. |
| **Effects > Vintage CRT Scanlines** | Toggles vintage CRT horizontal scanlines, with a 1px–20px thickness slider. |
| **Effects > CRT Glitch** | Toggles retro CRT sync glitching and chromatic tears, with a 0%–100% chance slider. |
| **Effects > CRT Snow** | Toggles continuous analog TV static noise, with a 1%–100% amount slider. |
| **Always on Top** | Checkbox to pin the widget above all other windows. |
| **Window Shadow** | Checkbox to toggle the soft 3D desktop drop shadow. |
| **New Window** | Spawns an additional independent slideshow widget on your desktop. |
| **Close Widget** | Closes only the active widget window (leaves other open widgets running). |
| **Exit All** | Cleanly terminates all open widgets and shuts down the application. |

---

## 🖱️ Mouse Gestures

- **Left-Click & Drag:** Click anywhere on the image/widget to move it around your desktop.
- **Corner Resize:** Grab the bottom-right grip to resize the widget to any dimension.
- **Mouse Wheel on Sliders:** Hover over the Interval or Opacity slider in the context menu and scroll the wheel for incremental adjustment.

---

## 📂 Supported Image Formats

- `.jpg`, `.jpeg` (JPEG Images)
- `.png` (Portable Network Graphics with alpha transparency)
- `.gif` (GIF Images)
- `.bmp` (Bitmap Images)
- `.webp` (WebP Images)
- `.tiff`, `.tif` (TIFF Images)
- `.ico` (Windows Icons)

---

---

## 📱 Smartphone Photo Orientation (EXIF Handling)

Photos captured on mobile devices (such as Samsung Galaxy S23 Ultra, Apple iPhones, Google Pixel, etc.) are physically recorded by horizontally-aligned camera sensors. When holding a phone vertically in portrait mode, smartphones write the image pixels horizontally in landscape orientation and store an **EXIF Orientation Tag** (`Tag 0x0112 = 6` for 90° clockwise) in the file header rather than re-encoding millions of pixels.

Standard WPF decoders ignore this tag by default, causing phone photos to display sideways (horizontal). **CyberpunkSlideshowWidget solves this automatically:**

1. **Dual-Stage EXIF Scanner:** Directly inspects the JPEG `APP1` / `Exif` header in memory within microseconds without third-party dependencies, falling back to WIC `BitmapMetadata` for other formats.
2. **Lossless Hardware Transform:** Automatically applies the required `RotateTransform` (90°, 180°, 270°) or `ScaleTransform` (mirroring) via WPF's `TransformedBitmap`.
3. **Adaptive Aspect Ratio:** The widget recalculates window orientation using the *post-transformed* dimensions, ensuring portrait photos trigger a vertical widget orientation and display upright.

---

## 🏗️ Architecture & Engineering Highlights

```
CyberpunkSlideshowWidget/
├── App.xaml                  # Application entry point (ShutdownMode: OnLastWindowClose)
├── App.xaml.cs               # Clean exit dispatcher lifecycle
├── MainWindow.xaml           # Borderless transparent window with context menu & 3D shadow
├── MainWindow.xaml.cs        # Slideshow logic, memory safeguards, stream loader, multi-window
├── AssemblyInfo.cs           # WPF ThemeInfo attributes
├── CyberpunkSlideshowWidget.csproj # .NET 8 WPF project file, ApplicationIcon & embedded resources
├── icon.png                  # Original 1024x1024 application asset
└── icon.ico                  # Multi-resolution icon (256, 128, 64, 48, 32, 16) for Windows
```

### Key Technical Safeguards:
1. **Zero File Locking:** Uses `File.ReadAllBytes` and `MemoryStream` with `BitmapCacheOption.OnLoad` so the OS file handle is closed in milliseconds.
2. **Preventing High-Resolution Memory Bloat:** Applies `DecodePixelWidth` capped at screen resolution for ultra-high megapixel photos.
3. **Automatic EXIF Orientation Correction:** Employs a zero-allocation byte scanner on the JPEG APP1 stream to detect orientation tags (1–8) and applies GPU-backed lossless rotation so mobile camera portrait shots always display right-side up.
4. **No Event Leakage:** `_timer.Tick` and `SystemEvents.DisplaySettingsChanged` are explicitly unsubscribed in `OnClosed()`, guaranteeing the Garbage Collector can immediately reclaim closed window instances.
5. **DWM Context Loss Protection:** Hooks `DisplaySettingsChanged` and dispatches `InvalidateVisual()` to ensure transparent windows do not lose their rendering buffer across monitor reconfiguration or display sleep.
6. **Thread-Safe Rendering:** Uses `bitmap.Freeze()` on all decoded bitmaps to enable hardware-accelerated rendering and multi-threaded accessibility.

---

## 🚀 Building & Running

### Prerequisites
- Windows 10 or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Debug:
```bash
dotnet build
```

### Build Optimized Release:
```bash
dotnet build -c Release
```

### Run Directly:
```bash
dotnet run
```
or run the compiled binary located at:
`bin/Release/net8.0-windows/CyberpunkSlideshowWidget.exe`

---

## 📄 License
This project is open source and available under the [MIT License](LICENSE).

