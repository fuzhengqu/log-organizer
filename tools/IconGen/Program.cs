using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IconGen;

/// <summary>
/// Draws the Log Organizer mark at every icon size and writes the multi-frame .ico plus the
/// 256 px badge. Geometry is normalised to a unit square and re-rendered per size, so the
/// 16 px frame keeps a heavier stroke instead of being a shrunken 256 px picture.
/// </summary>
internal static class Program
{
    /// <summary>Sizes Windows asks for: taskbar, tray at 100–200% DPI, Alt-Tab and Explorer.</summary>
    private static readonly int[] FrameSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static int Main(string[] args)
    {
        var repoRoot = Path.GetFullPath(args.Length > 0 ? args[0] : FindRepoRoot());
        var assets = Path.Combine(repoRoot, "src", "LogOrganizer.WinUI", "Assets");
        Directory.CreateDirectory(assets);

        var frames = FrameSizes.Select(size => (Size: size, Bitmap: Draw(size))).ToArray();
        try
        {
            var ico = Path.Combine(assets, "LogOrganizer.ico");
            WriteIco(ico, frames);
            Console.WriteLine($"ico  -> {ico} ({new FileInfo(ico).Length / 1024} KB, {frames.Length} frames)");

            var badge = Path.Combine(assets, "LogOrganizer-256.png");
            frames[^1].Bitmap.Save(badge, ImageFormat.Png);
            Console.WriteLine($"png  -> {badge}");

            if (args.Length > 1)
            {
                var preview = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(preview);
                WritePreviewSheet(Path.Combine(preview, "icon-preview.png"), frames);
                WriteZoomSheet(
                    Path.Combine(preview, "icon-zoom.png"),
                    frames.Where(frame => frame.Size <= 32).ToArray());
                foreach (var (size, bitmap) in frames)
                {
                    bitmap.Save(Path.Combine(preview, $"icon-{size}.png"), ImageFormat.Png);
                }

                Console.WriteLine($"preview -> {preview}");
            }
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Bitmap.Dispose();
            }
        }

        return 0;
    }

    private static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        var tile = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
        using (var shape = RoundedRectangle(tile, size * 0.225f))
        using (var fill = TileBrush(tile, size))
        {
            g.FillPath(fill, shape);
        }

        DrawMark(g, size);
        return bitmap;
    }

    /// <summary>Warm accent gradient; flat fill below 32 px where a gradient turns to mud.</summary>
    private static Brush TileBrush(RectangleF tile, int size)
    {
        if (size < 32)
        {
            return new SolidBrush(Color.FromArgb(0xD2, 0x61, 0x28));
        }

        var brush = new LinearGradientBrush(
            tile,
            Color.FromArgb(0xE4, 0x8B, 0x41),
            Color.FromArgb(0xA3, 0x47, 0x18),
            LinearGradientMode.ForwardDiagonal);
        brush.SetSigmaBellShape(0.45f);
        return brush;
    }

    private static void DrawMark(Graphics g, int size)
    {
        var bold = size <= 24;
        var s = (float)size;
        var ink = Color.FromArgb(0xFF, 0xFF, 0xF7, 0xEF);

        // Arrow head and shaft scale together; small frames get a thicker shaft and wider head.
        var halfShaft = bold ? 0.082f : 0.050f;
        var headHalf = bold ? 0.196f : 0.152f;
        var headBase = bold ? 0.382f : 0.372f;
        var tip = bold ? 0.512f : 0.524f;
        var shaftTop = bold ? 0.150f : 0.158f;

        var arrow = new[]
        {
            At(0.5f - halfShaft, shaftTop),
            At(0.5f + halfShaft, shaftTop),
            At(0.5f + halfShaft, headBase),
            At(0.5f + headHalf, headBase),
            At(0.5f, tip),
            At(0.5f - headHalf, headBase),
            At(0.5f - halfShaft, headBase),
        };
        using (var path = new GraphicsPath())
        using (var brush = new SolidBrush(ink))
        {
            path.AddPolygon(arrow);
            g.FillPath(brush, path);
        }

        // Open tray: short outward lips at the top, a slight taper and a flat bottom.
        var tray = new[]
        {
            At(0.166f, 0.622f),
            At(0.238f, 0.622f),
            At(0.300f, 0.752f),
            At(0.700f, 0.752f),
            At(0.762f, 0.622f),
            At(0.834f, 0.622f),
        };
        var thickness = MathF.Max(s * (bold ? 0.104f : 0.072f), bold ? 1.6f : 2.2f);
        using (var pen = new Pen(ink, thickness)
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round,
                   LineJoin = LineJoin.Round,
               })
        {
            g.DrawLines(pen, tray);
        }

        PointF At(float x, float y) => new(x * s, y * s);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void WriteIco(string path, IReadOnlyList<(int Size, Bitmap Bitmap)> frames)
    {
        // 128 px and up are PNG compressed; smaller frames stay 32 bpp BMP, which every shell
        // version renders identically, including the tray's 16–32 px lookups.
        var payloads = frames
            .Select(frame => frame.Size >= 128 ? EncodePng(frame.Bitmap) : EncodeDib(frame.Bitmap))
            .ToArray();

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var size = frames[i].Size;
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var payload in payloads)
        {
            writer.Write(payload);
        }
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        return buffer.ToArray();
    }

    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var size = bitmap.Width;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        writer.Write(40);                  // biSize
        writer.Write(size);                // biWidth
        writer.Write(size * 2);            // biHeight: XOR bitmap followed by the AND mask
        writer.Write((ushort)1);           // biPlanes
        writer.Write((ushort)32);          // biBitCount
        writer.Write(0);                   // BI_RGB
        writer.Write(size * size * 4);     // biSizeImage
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var data = bitmap.LockBits(
            new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[size * 4];
            for (var y = size - 1; y >= 0; y--)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                writer.Write(row);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // 1 bpp mask, unused because 32 bpp frames carry alpha, but the format requires it.
        var maskStride = ((size + 31) / 32) * 4;
        writer.Write(new byte[maskStride * size]);
        return buffer.ToArray();
    }

    /// <summary>Contact sheet on the app's light and dark surfaces, for eyeballing 16 px legibility.</summary>
    private static void WritePreviewSheet(string path, IReadOnlyList<(int Size, Bitmap Bitmap)> frames)
    {
        const int padding = 24;
        const int labelHeight = 18;
        var widest = frames.Max(frame => frame.Size);
        var rowHeight = widest + padding + labelHeight;
        var width = padding + frames.Sum(frame => frame.Size + padding);
        var height = (rowHeight * 2) + padding;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(0xF8, 0xF5, 0xEF));

        using (var dark = new SolidBrush(Color.FromArgb(0x20, 0x1D, 0x1A)))
        {
            g.FillRectangle(dark, 0, rowHeight + padding, width, height);
        }

        using var label = new Font("Segoe UI", 8f);
        using var labelBrush = new SolidBrush(Color.FromArgb(0x6B, 0x63, 0x5C));
        using var labelBrushDark = new SolidBrush(Color.FromArgb(0xB9, 0xAF, 0xA5));
        var x = padding;
        foreach (var (size, bitmap) in frames)
        {
            // Bottom-align every frame so the sizes read as a row of icons, not a staircase.
            g.DrawImage(bitmap, x, padding + widest - size, size, size);
            g.DrawString($"{size}", label, labelBrush, x, padding + widest + 4);

            var darkTop = rowHeight + padding + padding + widest - size;
            g.DrawImage(bitmap, x, darkTop, size, size);
            g.DrawString($"{size}", label, labelBrushDark, x, rowHeight + padding + padding + widest + 4);
            x += size + padding;
        }

        sheet.Save(path, ImageFormat.Png);
    }

    /// <summary>Small frames blown up with nearest-neighbour, the only honest way to judge 16 px.</summary>
    private static void WriteZoomSheet(string path, IReadOnlyList<(int Size, Bitmap Bitmap)> frames)
    {
        const int zoom = 6;
        const int padding = 20;
        const int labelHeight = 20;

        var rowWidth = frames.Sum(frame => (frame.Size * zoom) + padding) + padding;
        var rowHeight = frames.Max(frame => frame.Size * zoom) + labelHeight + padding;
        var width = rowWidth;
        var height = (rowHeight * 2) + padding;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(0xF8, 0xF5, 0xEF));
        using (var dark = new SolidBrush(Color.FromArgb(0x20, 0x1D, 0x1A)))
        {
            g.FillRectangle(dark, 0, rowHeight + padding, width, height);
        }

        using var label = new Font("Segoe UI", 8f);
        using var mutedBrush = new SolidBrush(Color.FromArgb(0x6B, 0x63, 0x5C));
        using var lightBrush = new SolidBrush(Color.FromArgb(0xB9, 0xAF, 0xA5));
        var x = padding;
        foreach (var (size, bitmap) in frames)
        {
            var side = size * zoom;
            g.DrawImage(bitmap, new Rectangle(x, padding + (rowHeight - labelHeight - padding - side), side, side));
            g.DrawString($"{size}px", label, mutedBrush, x, padding + rowHeight - labelHeight);

            var y = rowHeight + padding + padding + (rowHeight - labelHeight - padding - side);
            g.DrawImage(bitmap, new Rectangle(x, y, side, side));
            g.DrawString($"{size}px", label, lightBrush, x, rowHeight + padding + rowHeight - labelHeight);
            x += side + padding;
        }

        sheet.Save(path, ImageFormat.Png);
    }

    /// <summary>Walks up from the tool output folder until the repository root is found.</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}