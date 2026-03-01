using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FloatingOCRWidget.Services
{
    public class ScreenCapture
    {
        // ── P/Invoke：取得螢幕真實 DPI（不受應用程式 DPI 感知設定影響） ───────
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            IntPtr hdcDest, int xDest, int yDest, int w, int h,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        // CreateDC("DISPLAY") 建立的 DC 使用實體像素座標，不受 DPI 感知虛擬化影響
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(
            string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int  MDT_EFFECTIVE_DPI        = 0;       // 使用者可見的有效 DPI
        private const uint SRCCOPY                  = 0x00CC0020;

        // ── DPI 縮放倍率偵測 ──────────────────────────────────────────────────

        /// <summary>
        /// 回傳指定矩形中心點所在螢幕的真實 DPI 縮放倍率（如 1.0、1.25、1.5、2.0）。
        /// 使用 GetDpiForMonitor，永遠回傳實體 DPI，與應用程式 DPI 感知設定無關。
        /// </summary>
        private static float GetDpiScaleForRect(Rectangle rect)
        {
            var center = new POINT
            {
                X = rect.X + rect.Width  / 2,
                Y = rect.Y + rect.Height / 2
            };
            IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0) // S_OK = 0
                return dpiX / 96.0f;
            return 1.0f; // fallback：假設 100% 縮放
        }

        // ── 公開方法 ──────────────────────────────────────────────────────────

        public async Task<Bitmap> CaptureSelectedAreaAsync()
        {
            var tcs = new TaskCompletionSource<Bitmap>();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var form = new ScreenSelectionForm();
                    var result = form.ShowDialog();
                    if (result == DialogResult.OK && form.SelectedRectangle != Rectangle.Empty)
                        tcs.SetResult(CaptureScreenArea(form.SelectedRectangle));
                    else
                        tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Screen capture error: {ex.Message}");
                    tcs.SetResult(null);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            return await tcs.Task;
        }

        /// <summary>
        /// DPI 感知截圖：
        ///   1. 偵測選取區域所在螢幕的真實 DPI 縮放倍率
        ///   2. 將邏輯像素座標換算成實體像素
        ///   3. 透過 BitBlt + CreateDC("DISPLAY") 直接擷取實體解析度畫面
        ///
        /// 效果：150% 縮放的螢幕，同樣大小的選取區可得到 2.25× 的像素數量，
        /// 大幅提升 OCR 對小字/細節文字的辨識率。
        /// </summary>
        public Bitmap CaptureScreenArea(Rectangle logicalRect)
        {
            if (logicalRect.Width <= 0 || logicalRect.Height <= 0) return null;

            try
            {
                float scale = GetDpiScaleForRect(logicalRect);

                // 換算為實體像素
                int physX = (int)Math.Round(logicalRect.X      * scale);
                int physY = (int)Math.Round(logicalRect.Y      * scale);
                int physW = Math.Max(1, (int)Math.Round(logicalRect.Width  * scale));
                int physH = Math.Max(1, (int)Math.Round(logicalRect.Height * scale));

                System.Diagnostics.Debug.WriteLine(
                    $"DPI capture: logical={logicalRect.Width}x{logicalRect.Height}" +
                    $" → physical={physW}x{physH} (scale={scale:F2}x)");

                var bitmap = new Bitmap(physW, physH, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);
                IntPtr destDC = g.GetHdc();

                // CreateDC("DISPLAY") 的座標系永遠是實體像素，不受 DPI 虛擬化影響
                IntPtr srcDC = CreateDC("DISPLAY", null, null, IntPtr.Zero);
                bool ok = BitBlt(destDC, 0, 0, physW, physH, srcDC, physX, physY, SRCCOPY);
                DeleteDC(srcDC);
                g.ReleaseHdc(destDC);

                if (!ok)
                {
                    System.Diagnostics.Debug.WriteLine("BitBlt failed, falling back to CopyFromScreen");
                    bitmap.Dispose();
                    return CaptureScreenAreaFallback(logicalRect);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DPI capture error: {ex.Message}, falling back");
                return CaptureScreenAreaFallback(logicalRect);
            }
        }

        /// <summary>備用截圖（無 DPI 感知）</summary>
        private static Bitmap CaptureScreenAreaFallback(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            try
            {
                var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);
                g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fallback capture error: {ex.Message}");
                return null;
            }
        }
    }

    // ── 全螢幕框選 UI ─────────────────────────────────────────────────────────

    public class ScreenSelectionForm : Form
    {
        private bool _isSelecting;
        private Point _startPoint;
        private Point _endPoint;
        private Rectangle _selectionRect;

        public Rectangle SelectedRectangle { get; private set; } = Rectangle.Empty;

        public ScreenSelectionForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState     = FormWindowState.Maximized;
            this.TopMost         = true;
            this.ShowInTaskbar   = false;
            this.BackColor       = Color.Black;
            this.Opacity         = 0.35;
            this.Cursor          = Cursors.Cross;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.DoubleBuffer, true);

            // 涵蓋全部螢幕（多螢幕支援）
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (Screen s in Screen.AllScreens)
            {
                left   = Math.Min(left,   s.Bounds.Left);
                top    = Math.Min(top,    s.Bounds.Top);
                right  = Math.Max(right,  s.Bounds.Right);
                bottom = Math.Max(bottom, s.Bounds.Bottom);
            }
            this.Bounds = new Rectangle(left, top, right - left, bottom - top);

            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _isSelecting = true;
                    _startPoint  = _endPoint = e.Location;
                    this.Capture = true;
                }
            };
            this.MouseMove += (s, e) =>
            {
                if (_isSelecting) { _endPoint = e.Location; UpdateRect(); this.Invalidate(); }
            };
            this.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && _isSelecting)
                {
                    _isSelecting  = false;
                    this.Capture  = false;
                    if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
                    {
                        SelectedRectangle  = _selectionRect;
                        this.DialogResult  = DialogResult.OK;
                    }
                    else
                    {
                        this.DialogResult = DialogResult.Cancel;
                    }
                    this.Close();
                }
            };
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { this.DialogResult = DialogResult.Cancel; this.Close(); }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_selectionRect.Width > 0 && _selectionRect.Height > 0)
            {
                using var pen = new Pen(Color.Red, 2);
                e.Graphics.DrawRectangle(pen, _selectionRect);

                var info = $"{_selectionRect.Width} x {_selectionRect.Height}";
                using var font  = new Font("Arial", 11, FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                using var bg    = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                var sz = e.Graphics.MeasureString(info, font);
                float tx = _selectionRect.X + 4;
                float ty = _selectionRect.Y - sz.Height - 4;
                if (ty < 0) ty = _selectionRect.Y + 4;
                e.Graphics.FillRectangle(bg, tx - 2, ty - 2, sz.Width + 4, sz.Height + 4);
                e.Graphics.DrawString(info, font, brush, tx, ty);
            }
        }

        private void UpdateRect()
        {
            int x = Math.Min(_startPoint.X, _endPoint.X);
            int y = Math.Min(_startPoint.Y, _endPoint.Y);
            int w = Math.Abs(_endPoint.X - _startPoint.X);
            int h = Math.Abs(_endPoint.Y - _startPoint.Y);
            _selectionRect = new Rectangle(x, y, w, h);
        }
    }
}
