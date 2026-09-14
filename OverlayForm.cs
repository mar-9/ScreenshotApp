using System.Drawing.Drawing2D;

namespace ScreenshotApp;

/// <summary>
/// 画面全体(マルチモニター含む仮想スクリーン全体)を覆う半透明の選択用フォーム。
/// マウスドラッグで矩形を選択し、離すと SelectionCompleted イベントで
/// 選択範囲(スクリーン座標)を通知する。Esc でキャンセル。
/// </summary>
public sealed class OverlayForm : Form
{
    private Point _startPoint;
    private Point _currentPoint;
    private bool _isSelecting;

    /// <summary>選択確定時に発火。引数は仮想スクリーン座標系の矩形。null ならキャンセル。</summary>
    public event Action<Rectangle?>? SelectionCompleted;

    public OverlayForm()
    {
        // 仮想スクリーン全体(マルチモニター全体)をカバー
        var bounds = SystemInformation.VirtualScreen;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        // 半透明の黒でうっすら暗くする（選択領域は後で明るく描画）
        Opacity = 0.35;

        KeyPreview = true;
        KeyDown += OverlayForm_KeyDown;
        MouseDown += OverlayForm_MouseDown;
        MouseMove += OverlayForm_MouseMove;
        MouseUp += OverlayForm_MouseUp;
        Paint += OverlayForm_Paint;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // レイヤードウィンドウ相当の見た目にするためTopMostスタイルを付与
            const int WS_EX_TOPMOST = 0x00000008;
            cp.ExStyle |= WS_EX_TOPMOST;
            return cp;
        }
    }

    private void OverlayForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            SelectionCompleted?.Invoke(null);
            Close();
        }
    }

    private void OverlayForm_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _isSelecting = true;
        _startPoint = e.Location;
        _currentPoint = e.Location;
        Invalidate();
    }

    private void OverlayForm_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;
        _currentPoint = e.Location;
        Invalidate();
    }

    private void OverlayForm_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting || e.Button != MouseButtons.Left) return;
        _isSelecting = false;

        Rectangle selRect = GetSelectionRectangle();

        // 極端に小さい矩形(誤クリック)はキャンセル扱い
        if (selRect.Width < 3 || selRect.Height < 3)
        {
            SelectionCompleted?.Invoke(null);
            Close();
            return;
        }

        // フォームはVirtualScreenの原点(負座標の場合あり)に配置されているため、
        // クライアント座標をスクリーン座標に変換する
        var screenRect = new Rectangle(
            selRect.X + Bounds.X,
            selRect.Y + Bounds.Y,
            selRect.Width,
            selRect.Height);

        SelectionCompleted?.Invoke(screenRect);
        Close();
    }

    private Rectangle GetSelectionRectangle()
    {
        int x = Math.Min(_startPoint.X, _currentPoint.X);
        int y = Math.Min(_startPoint.Y, _currentPoint.Y);
        int w = Math.Abs(_startPoint.X - _currentPoint.X);
        int h = Math.Abs(_startPoint.Y - _currentPoint.Y);
        return new Rectangle(x, y, w, h);
    }

    private void OverlayForm_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;

        if (_isSelecting)
        {
            var rect = GetSelectionRectangle();

            // 選択領域だけくり抜いたように明るく見せる
            using var brightBrush = new SolidBrush(Color.FromArgb(60, Color.White));
            g.FillRectangle(brightBrush, rect);

            using var pen = new Pen(Color.DeepSkyBlue, 2);
            g.DrawRectangle(pen, rect);

            // サイズ表示
            string sizeText = $"{rect.Width} x {rect.Height}";
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            var textSize = g.MeasureString(sizeText, font);
            var textPos = new PointF(rect.X, Math.Max(0, rect.Y - textSize.Height - 4));
            using var textBg = new SolidBrush(Color.FromArgb(200, Color.Black));
            g.FillRectangle(textBg, textPos.X, textPos.Y, textSize.Width + 6, textSize.Height + 2);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(sizeText, font, textBrush, textPos.X + 3, textPos.Y + 1);
        }

        // 操作ガイド(左上に軽く表示)
        string guide = "ドラッグで範囲選択  /  Esc でキャンセル";
        using var guideFont = new Font("Segoe UI", 9);
        using var guideBrush = new SolidBrush(Color.White);
        g.DrawString(guide, guideFont, guideBrush, 12, 12);
    }
}
