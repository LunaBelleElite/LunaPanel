using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LunaPanel.Tray;

/// <summary>
/// A flat button with a rounded-rectangle fill/outline, matching the
/// pill-style buttons the web-served panels already use (e.g. "Build a new
/// macro..." in the macro builder) - added so every native WinForms window
/// in this app (Status, Ports, Paired devices) reads as the same modern
/// visual language as the browser-rendered panels next to it, instead of
/// square Win32 buttons. Native <see cref="Button"/> has no corner-radius
/// property, so this owner-draws the fill, border and centered text itself
/// (<see cref="ControlStyles.UserPaint"/>) rather than clipping a plain
/// <see cref="Button"/> to a <see cref="Region"/> - a clipped region has no
/// anti-aliasing and the corners come out visibly jagged at this size.
/// </summary>
internal sealed class RoundedButton : Button
{
    private const int CornerRadius = 8;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>
    /// The button's actual fill colour.
    /// </summary>
    // Runtime-only, never meant to appear in the Designer - these three
    // attributes are what WFO1000 (WinForms' designer-serialization
    // analyzer) requires on a public property of a Control subclass.
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = SystemColors.Control;

    /// <summary>
    /// What to paint outside the rounded shape, in the four corners this
    /// control's own fill/border never touches - the parent container's own
    /// background colour, e.g. the same dark theme colour
    /// <c>buttonPanel</c>/<c>_contentPanel</c> already use in every current
    /// caller. A real <c>Color.Transparent</c> BackColor was tried first and
    /// produced visible fringing at the corners: WinForms' owner-draw
    /// transparency support paints whatever the double-buffer happened to
    /// hold underneath rather than reliably compositing against the parent,
    /// so those four corner pixels came out as a stray light-coloured halo
    /// instead of the dark background they should blend into. Painting them
    /// explicitly with a known solid colour removes the ambiguity entirely.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ContainerBackColor { get; set; } = SystemColors.Control;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.Transparent;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderThickness { get; set; } = 1;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Paint the whole control with the container's background first -
        // see ContainerBackColor's own remarks on why this replaced a
        // Color.Transparent BackColor.
        e.Graphics.Clear(ContainerBackColor);

        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;

        using var path = RoundedRectPath(bounds, CornerRadius);
        using (var fill = new SolidBrush(FillColor))
        {
            e.Graphics.FillPath(fill, path);
        }

        if (BorderThickness > 0 && BorderColor != Color.Transparent)
        {
            using var pen = new Pen(BorderColor, BorderThickness);
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
