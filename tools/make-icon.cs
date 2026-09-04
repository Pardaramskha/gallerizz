using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Fabrique assets\gallerizz.ico (16 a 256 px, entrees PNG) + un apercu PNG.
// Design : cadre de galerie dore sur fond anthracite, montagnes au soleil couchant.
internal static class MakeIcon
{
    [STAThread]
    private static void Main()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);

        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int size in sizes) pngs.Add(RenderPng(size));

        WriteIco(Path.Combine(assets, "gallerizz.ico"), sizes, pngs);
        File.WriteAllBytes(Path.Combine(assets, "icon-preview.png"), pngs[pngs.Count - 1]);
        Console.WriteLine("OK : assets\\gallerizz.ico + assets\\icon-preview.png");
    }

    private static byte[] RenderPng(int size)
    {
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            double s = size / 256.0;
            dc.PushTransform(new ScaleTransform(s, s));
            Draw(dc);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var ms = new MemoryStream())
        {
            enc.Save(ms);
            return ms.ToArray();
        }
    }

    private static void Draw(DrawingContext dc)
    {
        // Fond : carre arrondi anthracite avec un tres leger degrade.
        var bg = new LinearGradientBrush(
            Color.FromRgb(0x3A, 0x3C, 0x40), Color.FromRgb(0x23, 0x25, 0x28), 90);
        dc.DrawRoundedRectangle(bg, null, new Rect(8, 8, 240, 240), 52, 52);

        // La photo : ciel de couchant.
        var photoRect = new Rect(52, 52, 152, 152);
        var photoGeo = new RectangleGeometry(photoRect, 14, 14);
        var sky = new LinearGradientBrush(
            Color.FromRgb(0xF5, 0xC6, 0x5D), Color.FromRgb(0xDD, 0x6B, 0x35), 90);
        dc.DrawGeometry(sky, null, photoGeo);

        dc.PushClip(photoGeo);

        // Soleil pale.
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF2, 0xC9)), null, new Point(150, 104), 26, 26);

        // Montagnes : silhouette lointaine puis premier plan.
        var far = new PathGeometry();
        var f1 = new PathFigure { StartPoint = new Point(52, 204) };
        f1.Segments.Add(new LineSegment(new Point(112, 118), true));
        f1.Segments.Add(new LineSegment(new Point(160, 204), true));
        f1.IsClosed = true;
        far.Figures.Add(f1);
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x3B)), null, far);

        var near = new PathGeometry();
        var f2 = new PathFigure { StartPoint = new Point(118, 204) };
        f2.Segments.Add(new LineSegment(new Point(172, 132), true));
        f2.Segments.Add(new LineSegment(new Point(226, 204), true));
        f2.IsClosed = true;
        near.Figures.Add(f2);
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x53, 0x2F, 0x33)), null, near);

        dc.Pop();

        // Cadre dore de galerie autour de la photo.
        var gold = new LinearGradientBrush(
            Color.FromRgb(0xF0, 0xC9, 0x60), Color.FromRgb(0xC8, 0x8F, 0x2E), 90);
        var pen = new Pen(gold, 14) { LineJoin = PenLineJoin.Round };
        dc.DrawRoundedRectangle(null, pen, new Rect(52, 52, 152, 152), 14, 14);
    }

    private static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] png in pngs) w.Write(png);
        }
    }
}
