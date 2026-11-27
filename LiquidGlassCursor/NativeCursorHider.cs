using System.Runtime.InteropServices;

namespace LiquidGlassCursor;

public static class NativeCursorHider
{
  private const uint SPI_SETCURSORS = 0x0057;
  private const uint SPIF_SENDCHANGE = 0x02;

  private static readonly uint[] SystemCursorIds =
  [
      32512, // OCR_NORMAL
        32513, // OCR_IBEAM
        32514, // OCR_WAIT
        32515, // OCR_CROSS
        32516, // OCR_UP
        32642, // OCR_SIZENWSE
        32643, // OCR_SIZENESW
        32644, // OCR_SIZEWE
        32645, // OCR_SIZENS
        32646, // OCR_SIZEALL
        32648, // OCR_NO
        32649, // OCR_HAND
        32650  // OCR_APPSTARTING
  ];

  private static bool _isHidden;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr CreateCursor(
      IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight,
      byte[] pvANDPlane, byte[] pvXORPlane);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetSystemCursor(IntPtr hcur, uint id);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

  public static void HideSystemCursor()
  {
    if (_isHidden) return;

    // 32x32 1-bit masks: AND = 0xFF (preserve screen), XOR = 0x00 (do not invert) -> 100% invisible
    byte[] andMask = new byte[128];
    Array.Fill(andMask, (byte)0xFF);
    byte[] xorMask = new byte[128]; // all 0x00

    foreach (uint id in SystemCursorIds)
    {
      IntPtr hBlank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andMask, xorMask);
      if (hBlank != IntPtr.Zero)
      {
        SetSystemCursor(hBlank, id);
      }
    }

    _isHidden = true;
  }

  public static void RestoreSystemCursor()
  {
    // Always broadcast SPIF_SENDCHANGE so Windows reloads your active scheme (e.g., Black XL)
    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
    _isHidden = false;
  }
}