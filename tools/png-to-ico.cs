using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Convertit assets\gallerizz.png en assets\gallerizz.ico (16 a 256 px, entrees PNG, reduction haute qualite).
internal static class PngToIco
{
    [STAThread]
    private static void Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string source = args.Length > 0 ? args[0] : Path.Combine(root, "assets", "gallerizz.png");
        string target = Path.Combine(root, "assets", "gallerizz.ico");

        var src = new BitmapImage();
        src.BeginInit();
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.UriSource = new Uri(Path.GetFullPath(source));
        src.EndInit();
        src.Freeze();

        // Regle du format ICO : PNG uniquement pour la 256 ; les autres tailles en DIB classique,
        // sinon l'extracteur d'icones du shell (Explorateur, navigateurs) retombe sur l'icone generique.
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int size in sizes)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = dv.RenderOpen())
                dc.DrawImage(src, new Rect(0, 0, size, size));
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (size >= 256)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var ms = new MemoryStream()) { enc.Save(ms); pngs.Add(ms.ToArray()); }
            }
            else pngs.Add(MakeDib(rtb, size));
        }

        using (var fs = new FileStream(target, FileMode.Create))
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
        Console.WriteLine("OK : " + target + " (source : " + source + ")");
    }

    // Entree DIB 32 bits : BITMAPINFOHEADER (hauteur doublee), pixels BGRA de bas en haut,
    // puis masque AND 1 bpp a zero (l'alpha fait foi).
    private static byte[] MakeDib(BitmapSource src, int size)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0); // depremultiplie
        int stride = size * 4;
        var px = new byte[stride * size];
        conv.CopyPixels(px, stride, 0);
        int maskStride = ((size + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40);                // biSize
            w.Write(size);              // biWidth
            w.Write(size * 2);          // biHeight (XOR + AND)
            w.Write((ushort)1);         // biPlanes
            w.Write((ushort)32);        // biBitCount
            w.Write(0);                 // biCompression
            w.Write(stride * size + maskStride * size); // biSizeImage
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = size - 1; y >= 0; y--)
                w.Write(px, y * stride, stride);
            w.Write(new byte[maskStride * size]);
            w.Flush();
            return ms.ToArray();
        }
    }
}
