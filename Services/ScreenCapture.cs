using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FloatingOCRWidget.Services
{
    public class ScreenCapture
    {
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

        public Bitmap CaptureScreenArea(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            try
            {
                var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                    g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture area error: {ex.Message}");
                return null;
            }
        }
    }

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
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;
            this.Opacity = 0.35;
            this.Cursor = Cursors.Cross;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);

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
                    _startPoint = _endPoint = e.Location;
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
                    _isSelecting = false;
                    this.Capture = false;
                    if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
                    {
                        SelectedRectangle = _selectionRect;
                        this.DialogResult = DialogResult.OK;
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
                using (var pen = new Pen(Color.Red, 2))
                    e.Graphics.DrawRectangle(pen, _selectionRect);

                var info = $"{_selectionRect.Width} x {_selectionRect.Height}";
                using (var font = new Font("Arial", 11, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.White))
                using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    var sz = e.Graphics.MeasureString(info, font);
                    float tx = _selectionRect.X + 4;
                    float ty = _selectionRect.Y - sz.Height - 4;
                    if (ty < 0) ty = _selectionRect.Y + 4;
                    e.Graphics.FillRectangle(bg, tx - 2, ty - 2, sz.Width + 4, sz.Height + 4);
                    e.Graphics.DrawString(info, font, brush, tx, ty);
                }
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
