using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallerizz
{
    // Une frame décodée : ses pixels BGRA, sa position dans l'écran logique, son délai et sa méthode d'effacement.
    internal sealed class GifFrame
    {
        public int X, Y, Width, Height;
        public int DelayMs;
        public int Disposal;    // 0/1 : laisser, 2 : restaurer le fond, 3 : restaurer la frame précédente
        public byte[] Pixels;   // BGRA, Width*Height*4
    }

    // Données d'un GIF décodé. Constructible hors fil UI (tout est en byte[]).
    internal sealed class GifData
    {
        public int Width;
        public int Height;
        public int LoopCount;   // 0 = infini
        public readonly List<GifFrame> Frames = new List<GifFrame>();

        // Normalisation des délais, convention des navigateurs : < 20 ms => 100 ms.
        internal static int NormalizeDelay(int centiseconds)
        {
            int ms = centiseconds * 10;
            return ms < 20 ? 100 : ms;
        }

        internal static GifData Parse(byte[] bytes)
        {
            var data = new GifData();
            var ms = new MemoryStream(bytes);
            var dec = new GifBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            var gmd = dec.Metadata as BitmapMetadata;
            data.Width = QueryInt(gmd, "/logscrdesc/Width", 0);
            data.Height = QueryInt(gmd, "/logscrdesc/Height", 0);
            data.LoopCount = ReadLoopCount(gmd);

            foreach (BitmapFrame frame in dec.Frames)
            {
                var fmd = frame.Metadata as BitmapMetadata;
                var gf = new GifFrame();
                gf.X = QueryInt(fmd, "/imgdesc/Left", 0);
                gf.Y = QueryInt(fmd, "/imgdesc/Top", 0);
                gf.Width = QueryInt(fmd, "/imgdesc/Width", frame.PixelWidth);
                gf.Height = QueryInt(fmd, "/imgdesc/Height", frame.PixelHeight);
                gf.DelayMs = NormalizeDelay(QueryInt(fmd, "/grctlext/Delay", 10));
                gf.Disposal = QueryInt(fmd, "/grctlext/Disposal", 0);

                var conv = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                if (gf.Width != w) gf.Width = w;
                if (gf.Height != h) gf.Height = h;
                gf.Pixels = new byte[w * h * 4];
                conv.CopyPixels(gf.Pixels, w * 4, 0);
                data.Frames.Add(gf);
            }

            if (data.Width <= 0 || data.Height <= 0)
            {
                // Écran logique absent ou farfelu : on prend l'étendue réelle des frames.
                foreach (GifFrame f in data.Frames)
                {
                    if (f.X + f.Width > data.Width) data.Width = f.X + f.Width;
                    if (f.Y + f.Height > data.Height) data.Height = f.Y + f.Height;
                }
            }
            return data;
        }

        private static int QueryInt(BitmapMetadata md, string q, int fallback)
        {
            if (md == null) return fallback;
            try
            {
                if (!md.ContainsQuery(q)) return fallback;
                object v = md.GetQuery(q);
                if (v == null) return fallback;
                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }

        private static int ReadLoopCount(BitmapMetadata md)
        {
            try
            {
                if (md == null || !md.ContainsQuery("/appext/application")) return 0;
                var app = md.GetQuery("/appext/application") as byte[];
                if (app == null) return 0;
                string name = System.Text.Encoding.ASCII.GetString(app);
                if (!name.StartsWith("NETSCAPE") && !name.StartsWith("ANIMEXTS")) return 1; // pas de boucle déclarée
                var raw = md.GetQuery("/appext/data") as byte[];
                if (raw == null || raw.Length < 4) return 0;
                return raw[2] | (raw[3] << 8); // [taille, 1, lo, hi] — 0 = infini
            }
            catch { return 0; }
        }

        // Compose la frame d'index donné (avec tout l'historique de disposal) — pour l'aperçu et le presse-papiers.
        internal BitmapSource ComposeFrame(int index)
        {
            var canvas = new byte[Width * Height * 4];
            byte[] saved = null;
            for (int i = 0; i <= index && i < Frames.Count; i++)
                ComposeStep(canvas, ref saved, i);
            var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, canvas, Width * 4);
            bmp.Freeze();
            return bmp;
        }

        // Applique la frame i sur le canevas : disposal de la frame précédente, puis fusion alpha.
        internal void ComposeStep(byte[] canvas, ref byte[] saved, int i)
        {
            if (i > 0)
            {
                GifFrame prev = Frames[i - 1];
                if (prev.Disposal == 2) ClearRect(canvas, prev.X, prev.Y, prev.Width, prev.Height);
                else if (prev.Disposal == 3 && saved != null) Array.Copy(saved, canvas, canvas.Length);
            }
            GifFrame cur = Frames[i];
            if (cur.Disposal == 3)
            {
                if (saved == null) saved = new byte[canvas.Length];
                Array.Copy(canvas, saved, canvas.Length);
            }
            BlitAlpha(canvas, cur);
        }

        private void ClearRect(byte[] canvas, int x, int y, int w, int h)
        {
            for (int row = 0; row < h; row++)
            {
                int cy = y + row;
                if (cy < 0 || cy >= Height) continue;
                int start = Math.Max(0, x);
                int end = Math.Min(Width, x + w);
                if (end <= start) continue;
                Array.Clear(canvas, (cy * Width + start) * 4, (end - start) * 4);
            }
        }

        private void BlitAlpha(byte[] canvas, GifFrame f)
        {
            for (int row = 0; row < f.Height; row++)
            {
                int cy = f.Y + row;
                if (cy < 0 || cy >= Height) continue;
                for (int col = 0; col < f.Width; col++)
                {
                    int cx = f.X + col;
                    if (cx < 0 || cx >= Width) continue;
                    int si = (row * f.Width + col) * 4;
                    if (f.Pixels[si + 3] == 0) continue; // pixel transparent : le canevas garde sa valeur
                    int di = (cy * Width + cx) * 4;
                    canvas[di] = f.Pixels[si];
                    canvas[di + 1] = f.Pixels[si + 1];
                    canvas[di + 2] = f.Pixels[si + 2];
                    canvas[di + 3] = f.Pixels[si + 3];
                }
            }
        }
    }

    // Lecture à vitesse réelle : horloge Stopwatch, frames sautées si le rendu prend du retard
    // (elles sont quand même composées, la surface reste juste — c'est ça, 100 % de la vitesse).
    internal sealed class GifPlayer
    {
        private readonly GifData _data;
        private readonly WriteableBitmap _canvasBmp;
        private readonly byte[] _canvas;
        private byte[] _saved;
        private readonly Stopwatch _watch = new Stopwatch();
        private long _dueMs;
        private int _frame = -1;
        private int _loopsDone;
        private bool _running;
        private bool _finished;

        internal WriteableBitmap Canvas { get { return _canvasBmp; } }

        internal GifPlayer(GifData data)
        {
            _data = data;
            _canvas = new byte[data.Width * data.Height * 4];
            _canvasBmp = new WriteableBitmap(data.Width, data.Height, 96, 96, PixelFormats.Bgra32, null);
            Advance(); // première frame visible immédiatement
            Push();
        }

        internal void Start()
        {
            if (_running || _finished || _data.Frames.Count <= 1) return;
            _running = true;
            _watch.Start();
            CompositionTarget.Rendering += OnRendering;
        }

        internal void Stop()
        {
            if (!_running) return;
            _running = false;
            _watch.Stop();
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            long now = _watch.ElapsedMilliseconds;
            bool changed = false;
            while (now >= _dueMs && !_finished)
            {
                Advance();
                changed = true;
            }
            if (changed) Push();
            if (_finished) Stop();
        }

        private void Advance()
        {
            int next = _frame + 1;
            if (next >= _data.Frames.Count)
            {
                _loopsDone++;
                if (_data.LoopCount != 0 && _loopsDone >= _data.LoopCount) { _finished = true; return; }
                next = 0;
                // Retour au début : le canevas repart de zéro (disposal de la dernière frame inclus).
                Array.Clear(_canvas, 0, _canvas.Length);
                _saved = null;
            }
            if (next == 0)
            {
                Array.Clear(_canvas, 0, _canvas.Length);
                _saved = null;
            }
            _frame = next;
            _data.ComposeStep(_canvas, ref _saved, _frame);
            _dueMs += _data.Frames[_frame].DelayMs;
        }

        private void Push()
        {
            _canvasBmp.WritePixels(new Int32Rect(0, 0, _data.Width, _data.Height), _canvas, _data.Width * 4, 0);
        }
    }
}
