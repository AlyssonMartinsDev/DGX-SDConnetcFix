namespace Dgx.SdConnectFix;

internal static class DgxTheme
{
    public static readonly Color Background = Color.FromArgb(5, 16, 32);
    public static readonly Color Surface = Color.FromArgb(7, 24, 45);
    public static readonly Color Track = Color.FromArgb(22, 52, 92);
    public static readonly Color Primary = Color.FromArgb(37, 131, 255);
    public static readonly Color Text = Color.FromArgb(248, 250, 252);
    public static readonly Color Secondary = Color.FromArgb(169, 184, 208);

    public static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(113, 177, 255);
        button.BackColor = Primary;
        button.ForeColor = Background;
        button.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        button.Padding = new Padding(16, 7, 16, 7);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }
}

internal sealed class ProgressTrack : Control
{
    private int value;

    public ProgressTrack()
    {
        DoubleBuffered = true;
        AccessibleName = "Progresso";
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Value
    {
        get => value;
        set
        {
            this.value = Math.Clamp(value, 0, 100);
            AccessibleDescription = $"{this.value}%";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(DgxTheme.Track);
        using var fill = new SolidBrush(DgxTheme.Primary);
        e.Graphics.FillRectangle(fill, 0, 0, ClientSize.Width * value / 100, ClientSize.Height);
    }
}
