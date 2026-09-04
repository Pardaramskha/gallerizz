using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gallerizz
{
    // Sondes : une vraie Application WPF (une seule par AppDomain !), fenêtres hors écran, réflexion.
    internal static class Probes
    {
        private static int _failures;
        private static string _workDir;
        private static string _settingsPath;
        private static string _settingsBackup;

        [STAThread]
        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            _workDir = Path.Combine(Path.GetTempPath(), "gallerizz-probes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);
            BackupSettings();
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                Run("Signatures de formats", SniffProbe);
                Run("Tri naturel", NaturalSortProbe);
                Run("Delais GIF normalises", DelayProbe);
                Run("GIF anime : frames, delais, composition", GifProbe);
                Run("Orientation EXIF", ExifProbe);
                Run("WebP via WIC", WebpWicProbe);
                Run("WebP via dwebp (repli)", WebpFallbackProbe);
                Run("SVG : rendu et pixels", SvgProbe);
                Run("Fichier corrompu -> erreur propre", CorruptProbe);
                Run("Grande image : ajustement sans rognage", BigImageProbe);
                Run("Garde-fous securite (bombes, chemins)", SecurityProbe);
                Run("Navigation dossier + fenetre reelle", WindowProbe);
            }
            finally
            {
                RestoreSettings();
                try { Directory.Delete(_workDir, true); } catch { }
            }
            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "TOUTES LES SONDES SONT VERTES" : _failures + " SONDE(S) EN ECHEC");
            Environment.Exit(_failures);
        }

        private static void Run(string name, Action probe)
        {
            try
            {
                probe();
                Console.WriteLine("[OK] " + name);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("[KO] " + name + " : " + ex.Message);
            }
        }

        private static void Check(bool cond, string what)
        {
            if (!cond) throw new Exception(what);
        }

        // ---- Sauvegarde des réglages utilisateur (jamais écraser sans filet) ----

        private static void BackupSettings()
        {
            _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gallerizz", "settings.txt");
            if (File.Exists(_settingsPath))
            {
                _settingsBackup = _settingsPath + ".probes-bak";
                File.Copy(_settingsPath, _settingsBackup, true);
            }
        }

        private static void RestoreSettings()
        {
            try
            {
                if (_settingsBackup != null) File.Copy(_settingsBackup, _settingsPath, true);
                else if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
                if (_settingsBackup != null) File.Delete(_settingsBackup);
            }
            catch { }
        }

        // ---- Fabrique de fichiers témoins ----

        private static byte[] MakePng(int w, int h, Color color)
        {
            var pixels = new byte[w * h * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = color.A;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        private static byte[] MakeJpegWithOrientation(int w, int h, ushort orientation)
        {
            var pixels = new byte[w * h * 3];
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, pixels, w * 3);
            var enc = new JpegBitmapEncoder();
            var md = new BitmapMetadata("jpg");
            md.SetQuery("/app1/ifd/{ushort=274}", orientation);
            enc.Frames.Add(BitmapFrame.Create(bmp, null, md, null));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        // GIF animé minimal écrit à la main : 4×4, table de 4 couleurs, LZW « clear avant chaque code ».
        private static byte[] MakeAnimatedGif()
        {
            var o = new List<byte>();
            o.AddRange(Encoding.ASCII.GetBytes("GIF89a"));
            o.AddRange(U16(4)); o.AddRange(U16(4));      // écran logique 4×4
            o.Add(0xF1); o.Add(0); o.Add(0);             // GCT 4 couleurs
            o.AddRange(new byte[] { 255, 0, 0,  0, 0, 255,  0, 255, 0,  0, 0, 0 }); // rouge, bleu, vert, noir
            // Boucle infinie (NETSCAPE2.0)
            o.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
            o.AddRange(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            o.AddRange(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
            // Frame 1 : plein écran rouge, délai 10 cs, disposal 1
            AddFrame(o, 0, 0, 4, 4, 10, 1, FillIndices(16, 0));
            // Frame 2 : 2×2 bleu en (1,1), délai 25 cs, disposal 0
            AddFrame(o, 1, 1, 2, 2, 25, 0, FillIndices(4, 1));
            o.Add(0x3B); // fin
            return o.ToArray();
        }

        private static byte[] FillIndices(int count, byte index)
        {
            var a = new byte[count];
            for (int i = 0; i < count; i++) a[i] = index;
            return a;
        }

        private static IEnumerable<byte> U16(int v)
        {
            yield return (byte)(v & 0xFF);
            yield return (byte)((v >> 8) & 0xFF);
        }

        private static void AddFrame(List<byte> o, int x, int y, int w, int h, int delayCs, int disposal, byte[] indices)
        {
            o.AddRange(new byte[] { 0x21, 0xF9, 0x04, (byte)(disposal << 2) });
            o.AddRange(U16(delayCs));
            o.Add(0); o.Add(0); // pas de transparence, fin du bloc
            o.Add(0x2C);
            o.AddRange(U16(x)); o.AddRange(U16(y)); o.AddRange(U16(w)); o.AddRange(U16(h));
            o.Add(0); // pas de table locale
            o.Add(2); // taille minimale de code LZW
            // Codes de 3 bits : Clear=4 avant chaque pixel (le dictionnaire ne grossit jamais), EOI=5.
            var bits = new BitWriter();
            foreach (byte idx in indices)
            {
                bits.Write(4, 3);
                bits.Write(idx, 3);
            }
            bits.Write(5, 3);
            byte[] data = bits.ToArray();
            int pos = 0;
            while (pos < data.Length)
            {
                int chunk = Math.Min(255, data.Length - pos);
                o.Add((byte)chunk);
                for (int i = 0; i < chunk; i++) o.Add(data[pos + i]);
                pos += chunk;
            }
            o.Add(0);
        }

        private sealed class BitWriter
        {
            private readonly List<byte> _bytes = new List<byte>();
            private int _acc, _nbits;
            public void Write(int value, int width)
            {
                _acc |= value << _nbits;
                _nbits += width;
                while (_nbits >= 8)
                {
                    _bytes.Add((byte)(_acc & 0xFF));
                    _acc >>= 8;
                    _nbits -= 8;
                }
            }
            public byte[] ToArray()
            {
                var result = new List<byte>(_bytes);
                if (_nbits > 0) result.Add((byte)(_acc & 0xFF));
                return result.ToArray();
            }
        }

        private static byte[] GetPixel(BitmapSource bmp, int x, int y)
        {
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            var px = new byte[4];
            conv.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            return px; // B G R A
        }

        private static BitmapSource Rasterize(DrawingImage img, int w, int h)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
                dc.DrawImage(img, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        // ---- Les sondes ----

        private static void SniffProbe()
        {
            Check(ImageLoader.Sniff(MakePng(2, 2, Colors.Red)) == ImageFormatKind.Png, "PNG non reconnu");
            Check(ImageLoader.Sniff(MakeJpegWithOrientation(2, 2, 1)) == ImageFormatKind.Jpeg, "JPEG non reconnu");
            Check(ImageLoader.Sniff(MakeAnimatedGif()) == ImageFormatKind.Gif, "GIF non reconnu");
            byte[] webp = File.ReadAllBytes(FixturePath("test.webp"));
            Check(ImageLoader.Sniff(webp) == ImageFormatKind.Webp, "WebP non reconnu");
            byte[] svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
            Check(ImageLoader.Sniff(svg) == ImageFormatKind.Svg, "SVG non reconnu");
            var bmpBytes = new byte[16]; bmpBytes[0] = (byte)'B'; bmpBytes[1] = (byte)'M';
            Check(ImageLoader.Sniff(bmpBytes) == ImageFormatKind.Bmp, "BMP non reconnu");
            Check(ImageLoader.Sniff(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }) == ImageFormatKind.Unknown, "octets quelconques pris pour une image");
        }

        private static string FixturePath(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests", "fixtures", name);
        }

        private static void NaturalSortProbe()
        {
            Check(FolderNav.NaturalCompare(@"c:\x\img2.png", @"c:\x\img10.png") < 0, "img2 devrait venir avant img10");
            Check(FolderNav.NaturalCompare(@"c:\x\b.png", @"c:\x\a.png") > 0, "ordre alphabetique cassé");
        }

        private static void DelayProbe()
        {
            Check(GifData.NormalizeDelay(0) == 100, "delai 0 doit devenir 100 ms");
            Check(GifData.NormalizeDelay(1) == 100, "delai 1 cs doit devenir 100 ms");
            Check(GifData.NormalizeDelay(2) == 20, "delai 2 cs doit rester 20 ms");
            Check(GifData.NormalizeDelay(25) == 250, "delai 25 cs doit faire 250 ms");
        }

        private static void GifProbe()
        {
            GifData gif = GifData.Parse(MakeAnimatedGif());
            Check(gif.Width == 4 && gif.Height == 4, "ecran logique 4x4 attendu, obtenu " + gif.Width + "x" + gif.Height);
            Check(gif.Frames.Count == 2, "2 frames attendues, obtenu " + gif.Frames.Count);
            Check(gif.Frames[0].DelayMs == 100, "frame 1 : 100 ms attendu, obtenu " + gif.Frames[0].DelayMs);
            Check(gif.Frames[1].DelayMs == 250, "frame 2 : 250 ms attendu, obtenu " + gif.Frames[1].DelayMs);
            Check(gif.LoopCount == 0, "boucle infinie attendue, obtenu " + gif.LoopCount);
            Check(gif.Frames[1].X == 1 && gif.Frames[1].Y == 1, "frame 2 attendue en (1,1)");

            BitmapSource composed = gif.ComposeFrame(1);
            byte[] corner = GetPixel(composed, 0, 0);   // hors de la frame 2 : reste rouge
            byte[] center = GetPixel(composed, 1, 1);   // recouvert par la frame 2 : bleu
            Check(corner[2] > 200 && corner[0] < 50, "coin (0,0) devrait rester rouge");
            Check(center[0] > 200 && center[2] < 50, "centre (1,1) devrait etre bleu");
        }

        private static void ExifProbe()
        {
            string path = Path.Combine(_workDir, "oriented.jpg");
            File.WriteAllBytes(path, MakeJpegWithOrientation(4, 2, 6)); // 90° horaire
            LoadedImage img = ImageLoader.Load(path);
            Check(img.Error == null, "chargement JPEG : " + img.Error);
            Check(img.Info.PixelWidth == 2 && img.Info.PixelHeight == 4,
                "orientation 6 : dimensions 2x4 attendues, obtenu " + img.Info.PixelWidth + "x" + img.Info.PixelHeight);
        }

        private static void WebpWicProbe()
        {
            LoadedImage img = ImageLoader.Load(FixturePath("test.webp"));
            Check(img.Error == null, "chargement WebP : " + img.Error);
            Check(img.Info.PixelWidth == 128 && img.Info.PixelHeight == 128, "test.webp devrait faire 128x128");
        }

        private static void WebpFallbackProbe()
        {
            byte[] bytes = File.ReadAllBytes(FixturePath("test.webp"));
            BitmapSource bmp = WebPDecoder.Decode(bytes); // force la voie libwebp/dwebp, sans WIC
            Check(bmp != null, "le repli dwebp n'a rien decode (dwebp.exe present ?)");
            Check(bmp.PixelWidth == 128 && bmp.PixelHeight == 128, "repli : 128x128 attendu");
        }

        private static void SvgProbe()
        {
            string svg =
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\" viewBox=\"0 0 100 100\">" +
                "<style>.r{fill:#ff0000;}</style>" +
                "<rect class=\"r\" x=\"0\" y=\"0\" width=\"50\" height=\"100\"/>" +
                "<circle cx=\"75\" cy=\"25\" r=\"20\" fill=\"rgb(0,0,255)\"/>" +
                "<path d=\"M50 100 L100 100 L100 50 Z\" fill=\"#00ff00\"/>" +
                "<g transform=\"translate(60,60)\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" fill=\"#000\"/></g>" +
                "</svg>";
            SvgResult r = SvgRenderer.Render(svg, null);
            Check(Math.Abs(r.Width - 100) < 0.1 && Math.Abs(r.Height - 100) < 0.1, "taille 100x100 attendue");
            BitmapSource px = Rasterize(r.Image, 100, 100);
            byte[] pRect = GetPixel(px, 10, 50);
            byte[] pCircle = GetPixel(px, 75, 25);
            byte[] pPath = GetPixel(px, 90, 90);
            byte[] pEmpty = GetPixel(px, 55, 10);
            byte[] pMoved = GetPixel(px, 65, 65);
            Check(pRect[2] > 200 && pRect[1] < 50, "rect via classe CSS : rouge attendu");
            Check(pCircle[0] > 200 && pCircle[2] < 50, "cercle : bleu attendu");
            Check(pPath[1] > 200 && pPath[2] < 50, "chemin : vert attendu");
            Check(pEmpty[3] == 0, "zone vide : transparent attendu");
            Check(pMoved[3] > 200 && pMoved[2] < 50 && pMoved[1] < 50 && pMoved[0] < 50, "groupe translate : noir attendu en (65,65)");

            // SVG minifié : drapeaux d'arc collés, chemin relatif.
            string mini = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M12 2a10 10 0 1010 10A10 10 0 0012 2z\" fill=\"#123456\"/></svg>";
            SvgResult r2 = SvgRenderer.Render(mini, null);
            BitmapSource px2 = Rasterize(r2.Image, 24, 24);
            byte[] pDot = GetPixel(px2, 12, 12);
            Check(pDot[3] > 200, "cercle en arcs compacts : centre plein attendu");
        }

        private static void CorruptProbe()
        {
            string path = Path.Combine(_workDir, "corrompu.png");
            var junk = new byte[64];
            junk[0] = 0x89; junk[1] = 0x50; junk[2] = 0x4E; junk[3] = 0x47; // entete PNG, suite bidon
            File.WriteAllBytes(path, junk);
            LoadedImage img = ImageLoader.Load(path);
            Check(img.Error != null, "un PNG corrompu doit produire une erreur propre, pas un crash");
        }

        // Régression du 04/09 : une image plus grande que la fenêtre était rognée par le layout clip
        // (l'Image débordait de son emplacement AVANT le RenderTransform). Les 4 quadrants doivent être visibles.
        private static void BigImageProbe()
        {
            int w = 1414, h = 2000;
            var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool right = x >= w / 2, bottom = y >= h / 2;
                    // BGRA : rouge / vert / bleu / jaune selon le quadrant
                    if (!right && !bottom) { pixels[i + 2] = 255; }
                    else if (right && !bottom) { pixels[i + 1] = 255; }
                    else if (!right) { pixels[i] = 255; }
                    else { pixels[i + 1] = 255; pixels[i + 2] = 255; }
                    pixels[i + 3] = 255;
                }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            string path = Path.Combine(_workDir, "grande.png");
            using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);

            var surface = new ViewSurface();
            var win = new Window();
            win.Width = 520; win.Height = 420;
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = -32000; win.Top = -32000;
            win.ShowInTaskbar = false; win.ShowActivated = false;
            win.Content = surface;
            win.Show();
            try
            {
                DoEvents();
                LoadedImage img = ImageLoader.Load(path);
                Check(img.Error == null, "chargement : " + img.Error);
                surface.SetContent(img);
                DoEvents(); DoEvents();

                int sw = (int)surface.ActualWidth, sh = (int)surface.ActualHeight;
                Check(sw > 0 && sh > 0, "surface sans taille");
                var rtb = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(surface);

                double scale = Math.Min(1.0, Math.Min((double)sw / w, (double)sh / h));
                double bw = w * scale, bh = h * scale;
                double ox = (sw - bw) / 2, oy = (sh - bh) / 2;
                byte[] tl = GetPixel(rtb, (int)(ox + bw * 0.25), (int)(oy + bh * 0.25));
                byte[] tr = GetPixel(rtb, (int)(ox + bw * 0.75), (int)(oy + bh * 0.25));
                byte[] bl = GetPixel(rtb, (int)(ox + bw * 0.25), (int)(oy + bh * 0.75));
                byte[] br = GetPixel(rtb, (int)(ox + bw * 0.75), (int)(oy + bh * 0.75));
                Check(tl[2] > 200 && tl[1] < 60, "quadrant haut-gauche : rouge attendu (image rognee ?)");
                Check(tr[1] > 200 && tr[2] < 60, "quadrant haut-droit : vert attendu (image rognee ?)");
                Check(bl[0] > 200 && bl[2] < 60, "quadrant bas-gauche : bleu attendu (image rognee ?)");
                Check(br[1] > 200 && br[2] > 200 && br[0] < 60, "quadrant bas-droit : jaune attendu (image rognee ?)");
            }
            finally
            {
                win.Close();
                DoEvents();
            }
        }

        // Les garde-fous du 04/09 : bombes de décompression, bombes <use>, chemins hostiles dans les SVG.
        private static void SecurityProbe()
        {
            // 1. GIF déclarant un écran logique de 60000×60000 (canevas de 14 Go) → erreur propre.
            var bomb = new List<byte>();
            bomb.AddRange(Encoding.ASCII.GetBytes("GIF89a"));
            bomb.AddRange(U16(60000)); bomb.AddRange(U16(60000));
            bomb.Add(0xF1); bomb.Add(0); bomb.Add(0);
            bomb.AddRange(new byte[] { 255, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0 });
            AddFrame(bomb, 0, 0, 4, 4, 10, 0, FillIndices(16, 0));
            bomb.Add(0x3B);
            string bombPath = Path.Combine(_workDir, "bombe.gif");
            File.WriteAllBytes(bombPath, bomb.ToArray());
            LoadedImage img = ImageLoader.Load(bombPath);
            Check(img.Error != null, "un GIF a ecran logique demesure doit etre refuse proprement");

            // 2. Bombe <use> en largeur (15^5 expansions potentielles) → le budget coupe, rendu < 10 s.
            var sb = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">");
            sb.Append("<defs><g id=\"n0\"><rect width=\"1\" height=\"1\"/></g>");
            for (int level = 1; level <= 5; level++)
            {
                sb.Append("<g id=\"n").Append(level).Append("\">");
                for (int i = 0; i < 15; i++) sb.Append("<use href=\"#n").Append(level - 1).Append("\"/>");
                sb.Append("</g>");
            }
            sb.Append("</defs><use href=\"#n5\"/></svg>");
            var sw = Stopwatch.StartNew();
            SvgResult r = SvgRenderer.Render(sb.ToString(), null);
            sw.Stop();
            Check(r != null && sw.ElapsedMilliseconds < 10000,
                "la bombe <use> doit etre coupee par le budget (rendu en " + sw.ElapsedMilliseconds + " ms)");

            // 3. <image> vers un chemin UNC ou en remontee ..\ → jamais charge (zone transparente).
            string secret = Path.Combine(_workDir, "secret.png");
            File.WriteAllBytes(secret, MakePng(8, 8, Colors.Red));
            string svgDir = Path.Combine(_workDir, "svgdir");
            Directory.CreateDirectory(svgDir);
            string hostile =
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\">" +
                "<image href=\"..\\secret.png\" x=\"0\" y=\"0\" width=\"10\" height=\"10\"/>" +
                "<image href=\"\\\\127.0.0.1\\c$\\x.png\" x=\"10\" y=\"10\" width=\"10\" height=\"10\"/>" +
                "</svg>";
            SvgResult r2 = SvgRenderer.Render(hostile, svgDir);
            BitmapSource px = Rasterize(r2.Image, 20, 20);
            Check(GetPixel(px, 5, 5)[3] == 0, "la remontee ..\\ doit etre bloquee (pixel transparent attendu)");
            Check(GetPixel(px, 15, 15)[3] == 0, "le chemin UNC doit etre bloque (pixel transparent attendu)");
        }

        private static void WindowProbe()
        {
            string dir = Path.Combine(_workDir, "nav");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "img1.png"), MakePng(3, 3, Colors.Red));
            File.WriteAllBytes(Path.Combine(dir, "img2.png"), MakePng(3, 3, Colors.Lime));
            File.WriteAllBytes(Path.Combine(dir, "img10.png"), MakePng(3, 3, Colors.Blue));

            var win = new MainWindow(Path.Combine(dir, "img1.png"));
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = -32000;
            win.Top = -32000;
            win.ShowInTaskbar = false;
            win.ShowActivated = false;
            win.Show();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo navField = typeof(MainWindow).GetField("_nav", flags);
                FieldInfo curField = typeof(MainWindow).GetField("_current", flags);
                MethodInfo navigate = typeof(MainWindow).GetMethod("Navigate", flags);
                MethodInfo cycleBg = typeof(MainWindow).GetMethod("CycleBackground", flags);
                MethodInfo toggleInfo = typeof(MainWindow).GetMethod("ToggleInfo", flags);
                var nav = (FolderNav)navField.GetValue(win);

                Check(WaitFor(() => curField.GetValue(win) != null, 5000), "l'image initiale n'a pas charge");
                Check(nav.Count == 3, "3 images attendues dans le dossier, obtenu " + nav.Count);
                Check(Path.GetFileName(nav.Current) == "img1.png", "position initiale sur img1");

                navigate.Invoke(win, new object[] { 1 });
                Check(Path.GetFileName(nav.Current) == "img2.png", "tri naturel : img2 doit suivre img1, obtenu " + Path.GetFileName(nav.Current));
                navigate.Invoke(win, new object[] { 1 });
                Check(Path.GetFileName(nav.Current) == "img10.png", "img10 doit suivre img2");
                navigate.Invoke(win, new object[] { 1 });
                Check(Path.GetFileName(nav.Current) == "img1.png", "la navigation doit boucler sur img1");
                navigate.Invoke(win, new object[] { -1 });
                Check(Path.GetFileName(nav.Current) == "img10.png", "la navigation arriere doit boucler sur img10");

                Check(WaitFor(() => curField.GetValue(win) != null, 5000), "image non chargee apres navigation");
                Check(WaitFor(() => win.Title.StartsWith("img10.png"), 3000), "titre attendu commencant par img10.png, obtenu : " + win.Title);

                int bg0 = win.BackgroundIndex;
                cycleBg.Invoke(win, null);
                Check(win.BackgroundIndex == (bg0 + 1) % 3, "le fond doit passer au suivant");
                cycleBg.Invoke(win, null);
                cycleBg.Invoke(win, null);
                Check(win.BackgroundIndex == bg0, "trois cycles doivent revenir au fond initial");

                FieldInfo panelField = typeof(MainWindow).GetField("_infoPanel", flags);
                var panel = (Border)panelField.GetValue(win);
                Check(panel.Visibility == Visibility.Collapsed, "panneau infos cache au depart");
                toggleInfo.Invoke(win, null);
                Check(panel.Visibility == Visibility.Visible, "panneau infos visible apres I");
                toggleInfo.Invoke(win, null);
                Check(panel.Visibility == Visibility.Collapsed, "panneau infos cache apres second I");

                // Plein écran : la bascule doit recentrer et réajuster même après un zoom manuel.
                FieldInfo surfField = typeof(MainWindow).GetField("_surface", flags);
                var surf = (ViewSurface)surfField.GetValue(win);
                FieldInfo fitField = typeof(ViewSurface).GetField("_fitMode", flags);
                FieldInfo scaleField = typeof(ViewSurface).GetField("_scale", flags);
                fitField.SetValue(surf, false);
                scaleField.SetValue(surf, 7.0);

                MethodInfo fullscreen = typeof(MainWindow).GetMethod("ToggleFullscreen", flags);
                fullscreen.Invoke(win, null);
                Check(win.WindowStyle == WindowStyle.None && win.WindowState == WindowState.Maximized, "plein ecran attendu");
                Check(WaitFor(() => (bool)fitField.GetValue(surf), 3000), "le plein ecran doit reajuster l'image");
                Check((double)scaleField.GetValue(surf) <= 1.0, "le zoom doit revenir a l'ajustement, obtenu " + scaleField.GetValue(surf));
                fullscreen.Invoke(win, null);
                Check(win.WindowStyle != WindowStyle.None, "retour du plein ecran attendu");
            }
            finally
            {
                win.Close();
                DoEvents();
            }
        }

        // ---- Pompe d'evenements ----

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }

        private static bool WaitFor(Func<bool> cond, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                DoEvents();
                Thread.Sleep(15);
            }
            return cond();
        }
    }
}
