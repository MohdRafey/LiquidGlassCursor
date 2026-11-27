# Liquid Glass Optical Studio

[![Download Latest Release](https://img.shields.io/badge/Download_Installer-v1.0.0-0284C7?style=for-the-badge&logo=windows)](../../releases/latest)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0F172A?style=for-the-badge&logo=windows)
![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=for-the-badge&logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-10B981?style=for-the-badge)

**Liquid Glass Optical Studio** is a real-time Windows desktop utility built in C# and .NET 8 WPF that replaces the static Windows pointer with a live optical glass lens featuring real-time magnification, Total Internal Reflection (TIR), chromatic dispersion, frosted diffusion, and a macOS-style **Shake to Enlarge** gesture locator.

---

## Features

- **Real-Time Optical Glass Rendering:** Live screen capture beneath the cursor processed through Signed Distance Fields (SDF) with convex magnification, rim refraction wrap, RGB chromatic aberration, and frosted blur diffusion.
- **5 Pointer Silhouettes:**
  - *Original Liquid Blob* (organic metaball cluster)
  - *Classic Glass Pointer* (arrow with liquid tail)
  - *Sleek Stealth Dart* (concave geometric dart)
  - *Liquid Teardrop*
  - *Minimal Lens Orb* (center-hotspot optical sphere)
- **5 Light-Screen Contrast Engines:** Automatically maintains crisp edge visibility over pure white documents and bright backgrounds using *Adaptive Luminance Rim*, *Soft Floating Contact Shadow*, *Double-Bevel Optical Edge*, or *Beer-Lambert Smoked Glass Absorption*.
- **macOS-Style Shake to Enlarge:** Rapidly shake your mouse to smoothly scale up the lens (`1.4x`–`3.0x`) for a configurable hold duration with adjustable trigger sensitivity.
- **Taskbar & Start Menu Z-Order Lock:** Uses `uiAccess="true"` alongside `WS_EX_NOACTIVATE` and a `WM_WINDOWPOSCHANGING` Win32 hook so the lens stays locked above the Windows Taskbar and Start Menu even when clicking shell icons.
- **System Tray Integration:** Minimize to the notification area to keep the optical cursor running unobtrusively in the background, with instant toggle and safe cursor restoration on exit.

---

## Installation (Recommended)

1. Go to the [**Releases**](../../releases/latest) page and download **`LiquidGlassCursor_Setup_v1.0.0.exe`**.
2. Run the installer (Administrator privileges are requested once during setup).
3. The installer automatically:
   - Deploys the signed application into `C:\Program Files\LiquidCursor\`.
   - Registers the public code-signing certificate (`LiquidGlassLocalCert.cer`) into `LocalMachine\Root` so Windows authorizes `uiAccess="true"` elevation.
   - Creates Start Menu and optional Desktop shortcuts.

---

## Building from Source

### Prerequisites
- **Windows 10 / 11 (x64)**
- **.NET 8.0 SDK** (`net8.0-windows`)
- **Visual Studio 2022** (or `dotnet` CLI)
- **Inno Setup 6 / 7** *(optional, for compiling the installer)*

### 1. Clone the Repository
```bash
git clone [https://github.com/YOUR_USERNAME/LiquidGlassCursor.git](https://github.com/YOUR_USERNAME/LiquidGlassCursor.git)
cd LiquidGlassCursor
