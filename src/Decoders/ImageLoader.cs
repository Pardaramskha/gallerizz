using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallerizz
{
    internal enum ImageFormatKind { Unknown, Jpeg, Png, Gif, Bmp, Tiff, Webp, Ico, Svg, Avif, Heic }

    internal enum ImageKind { Static, Animated, Vector }

    // Fiche d'informations affichée par le panneau I.
    internal sealed class ImageInfo
    {
        public string FileName;
        public string Folder;
        public string FormatName;
        public int PixelWidth;
        public int PixelHeight;
        public long FileSize;
        public DateTime Modified;
        public string TakenDate;    // EXIF, peut être null
        public string Camera;       // EXIF, peut être null
        public int FrameCount;      // > 1 pour un GIF animé
    }

    // Résultat d'un chargement : bitmap figé, animation GIF ou dessin vectoriel — ou une erreur propre.
    internal sealed class LoadedImage
    {
        public ImageKind Kind;
        public BitmapSource Bitmap;   // Static
        public GifData Gif;           // Animated
        public DrawingImage Vector;   // Vector (SVG)
        public double DisplayWidth;   // taille d'affichage à 100 % (px)
        public double DisplayHeight;
        public ImageInfo Info;
        public string Error;
        public string Path;
    }

    internal static class ImageLoader
    {
        // Identifie le format par la signature des octets, pas par l'extension.
        internal static ImageFormatKind Sniff(byte[] b)
        {
            if (b == null || b.Length < 12) return SniffText(b);
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ImageFormatKind.Jpeg;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ImageFormatKind.Png;
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return ImageFormatKind.Gif;
            if (b[0] == 'B' && b[1] == 'M') return ImageFormatKind.Bmp;
            if ((b[0] == 'I' && b[1] == 'I' && b[2] == 0x2A && b[3] == 0) ||
                (b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 0x2A)) return ImageFormatKind.Tiff;
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' &&
                b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ImageFormatKind.Webp;
            if (b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return ImageFormatKind.Ico;
            if (b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p')
            {
                string brand = Encoding.ASCII.GetString(b, 8, 4);
                if (brand == "avif" || brand == "avis") return ImageFormatKind.Avif;
                return ImageFormatKind.Heic; // heic, heix, mif1, msf1...
            }
            return SniffText(b);
        }

        private static ImageFormatKind SniffText(byte[] b)
        {
            if (b == null || b.Length < 4) return ImageFormatKind.Unknown;
            try
            {
                int len = Math.Min(b.Length, 1024);
                string head = Encoding.UTF8.GetString(b, 0, len).TrimStart('﻿', ' ', '\t', '\r', '\n');
                if (head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (head.StartsWith("<?xml") && head.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0))
                    return ImageFormatKind.Svg;
            }
            catch { }
            return ImageFormatKind.Unknown;
        }

        internal static string FormatLabel(ImageFormatKind k)
        {
            switch (k)
            {
                case ImageFormatKind.Jpeg: return "JPEG";
                case ImageFormatKind.Png: return "PNG";
                case ImageFormatKind.Gif: return "GIF";
                case ImageFormatKind.Bmp: return "BMP";
                case ImageFormatKind.Tiff: return "TIFF";
                case ImageFormatKind.Webp: return "WebP";
                case ImageFormatKind.Ico: return "ICO";
                case ImageFormatKind.Svg: return "SVG";
                case ImageFormatKind.Avif: return "AVIF";
                case ImageFormatKind.Heic: return "HEIC";
                default: return "Inconnu";
            }
        }

        // Chargement complet. Peut tourner hors du fil UI : tout ce qui en sort est figé (Freeze).
        internal static LoadedImage Load(string path)
        {
            var result = new LoadedImage();
            result.Path = path;
            byte[] bytes;
            var info = new ImageInfo();
            try
            {
                var fi = new FileInfo(path);
                info.FileName = fi.Name;
                info.Folder = fi.DirectoryName;
                info.FileSize = fi.Length;
                info.Modified = fi.LastWriteTime;
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                result.Error = "Impossible de lire le fichier : " + ex.Message;
                return result;
            }

            ImageFormatKind kind = Sniff(bytes);
            if (kind == ImageFormatKind.Unknown &&
                string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
                kind = ImageFormatKind.Svg;
            info.FormatName = FormatLabel(kind);
            result.Info = info;

            try
            {
                switch (kind)
                {
                    case ImageFormatKind.Svg:
                        LoadSvg(bytes, path, result);
                        break;
                    case ImageFormatKind.Gif:
                        LoadGif(bytes, result);
                        break;
                    case ImageFormatKind.Webp:
                        LoadWebp(bytes, result);
                        break;
                    case ImageFormatKind.Unknown:
                        result.Error = "Format d'image non reconnu.";
                        break;
                    default:
                        LoadWic(bytes, kind, result);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (kind == ImageFormatKind.Avif)
                    result.Error = "Codec AVIF absent. Installez « Extensions vidéo AV1 » depuis le Microsoft Store.";
                else if (kind == ImageFormatKind.Heic)
                    result.Error = "Codec HEIC absent. Installez « Extensions d'image HEIF » depuis le Microsoft Store.";
                else
                    result.Error = "Image illisible ou corrompue (" + ex.Message + ")";
            }
            return result;
        }

        // Au-delà d'un demi-gigapixel (2 Go de BGRA), on refuse proprement plutôt que d'engloutir la RAM.
        internal const long MaxRasterPixels = 536870912;

        private static void LoadWic(byte[] bytes, ImageFormatKind kind, LoadedImage result)
        {
            // Premier passage sans décodage des pixels : les dimensions seules, pour jauger la bombe.
            var probe = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            BitmapFrame probeFrame = probe.Frames[0];
            if ((long)probeFrame.PixelWidth * probeFrame.PixelHeight > MaxRasterPixels)
            {
                result.Error = string.Format("Image démesurée ({0}×{1}) : au-delà d'un demi-gigapixel.",
                    probeFrame.PixelWidth, probeFrame.PixelHeight);
                return;
            }
            var ms = new MemoryStream(bytes);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapFrame frame = dec.Frames[0];
            if (kind == ImageFormatKind.Ico)
            {
                // Un .ico contient plusieurs tailles : on prend la plus grande.
                foreach (BitmapFrame f in dec.Frames)
                    if (f.PixelWidth > frame.PixelWidth) frame = f;
            }
            BitmapSource src = frame;
            ReadExif(frame, kind, result.Info);
            int orientation = ReadOrientation(frame, kind);
            src = ApplyOrientation(src, orientation);
            src.Freeze();
            result.Kind = ImageKind.Static;
            result.Bitmap = src;
            result.DisplayWidth = src.PixelWidth;
            result.DisplayHeight = src.PixelHeight;
            result.Info.PixelWidth = src.PixelWidth;
            result.Info.PixelHeight = src.PixelHeight;
            result.Info.FrameCount = 1;
        }

        private static void LoadGif(byte[] bytes, LoadedImage result)
        {
            GifData gif = GifData.Parse(bytes);
            result.Info.PixelWidth = gif.Width;
            result.Info.PixelHeight = gif.Height;
            result.Info.FrameCount = gif.Frames.Count;
            result.DisplayWidth = gif.Width;
            result.DisplayHeight = gif.Height;
            if (gif.Frames.Count <= 1)
            {
                result.Kind = ImageKind.Static;
                result.Bitmap = gif.ComposeFrame(0);
            }
            else
            {
                result.Kind = ImageKind.Animated;
                result.Gif = gif;
            }
        }

        private static void LoadWebp(byte[] bytes, LoadedImage result)
        {
            BitmapSource src = null;
            try
            {
                var ms = new MemoryStream(bytes);
                var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                src = dec.Frames[0];
            }
            catch
            {
                src = WebPDecoder.Decode(bytes); // repli : libwebp embarquée
            }
            if (src == null)
            {
                result.Error = "WebP illisible : ni le codec Windows ni libwebp.dll ne sont disponibles.";
                return;
            }
            if (src.CanFreeze) src.Freeze();
            result.Kind = ImageKind.Static;
            result.Bitmap = src;
            result.DisplayWidth = src.PixelWidth;
            result.DisplayHeight = src.PixelHeight;
            result.Info.PixelWidth = src.PixelWidth;
            result.Info.PixelHeight = src.PixelHeight;
            result.Info.FrameCount = 1;
        }

        private static void LoadSvg(byte[] bytes, string path, LoadedImage result)
        {
            string text = DecodeText(bytes);
            SvgResult svg = SvgRenderer.Render(text, Path.GetDirectoryName(path));
            result.Kind = ImageKind.Vector;
            result.Vector = svg.Image;
            result.DisplayWidth = svg.Width;
            result.DisplayHeight = svg.Height;
            result.Info.PixelWidth = (int)Math.Round(svg.Width);
            result.Info.PixelHeight = (int)Math.Round(svg.Height);
            result.Info.FrameCount = 1;
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            return Encoding.UTF8.GetString(bytes);
        }

        // ---- EXIF ----

        private static object Query(BitmapMetadata md, string q)
        {
            try { return md.ContainsQuery(q) ? md.GetQuery(q) : null; }
            catch { return null; }
        }

        internal static int ReadOrientation(BitmapFrame frame, ImageFormatKind kind)
        {
            var md = frame.Metadata as BitmapMetadata;
            if (md == null) return 1;
            string[] queries = kind == ImageFormatKind.Tiff
                ? new[] { "/ifd/{ushort=274}" }
                : new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" };
            foreach (string q in queries)
            {
                object v = Query(md, q);
                if (v == null) continue;
                try
                {
                    int o = Convert.ToInt32(v, CultureInfo.InvariantCulture);
                    if (o >= 1 && o <= 8) return o;
                }
                catch { }
            }
            return 1;
        }

        internal static BitmapSource ApplyOrientation(BitmapSource src, int orientation)
        {
            if (orientation <= 1 || orientation > 8) return src;
            var tg = new TransformGroup();
            switch (orientation)
            {
                case 2: tg.Children.Add(new ScaleTransform(-1, 1)); break;
                case 3: tg.Children.Add(new RotateTransform(180)); break;
                case 4: tg.Children.Add(new ScaleTransform(1, -1)); break;
                case 5: tg.Children.Add(new RotateTransform(90)); tg.Children.Add(new ScaleTransform(-1, 1)); break;
                case 6: tg.Children.Add(new RotateTransform(90)); break;
                case 7: tg.Children.Add(new RotateTransform(270)); tg.Children.Add(new ScaleTransform(-1, 1)); break;
                case 8: tg.Children.Add(new RotateTransform(270)); break;
            }
            var tb = new TransformedBitmap(src, tg);
            return tb;
        }

        private static void ReadExif(BitmapFrame frame, ImageFormatKind kind, ImageInfo info)
        {
            var md = frame.Metadata as BitmapMetadata;
            if (md == null) return;
            string prefix = kind == ImageFormatKind.Tiff ? "/ifd" : "/app1/ifd";
            object taken = Query(md, prefix + "/exif/{ushort=36867}"); // DateTimeOriginal
            if (taken == null) taken = Query(md, prefix + "/{ushort=306}"); // DateTime
            if (taken != null)
            {
                string s = taken.ToString();
                DateTime dt;
                if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    info.TakenDate = dt.ToString("d MMMM yyyy HH:mm", CultureInfo.CurrentCulture);
                else
                    info.TakenDate = s;
            }
            object make = Query(md, prefix + "/{ushort=271}");
            object model = Query(md, prefix + "/{ushort=272}");
            string camera = ((make != null ? make.ToString().Trim() : "") + " " + (model != null ? model.ToString().Trim() : "")).Trim();
            if (camera.Length > 0) info.Camera = camera;
        }
    }
}
