using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallerizz
{
    // La surface centrale : affiche le contenu (bitmap, GIF animé ou dessin vectoriel),
    // gère l'ajustement à la fenêtre, le zoom à la molette centré sur le curseur et le déplacement à la souris.
    internal sealed class ViewSurface : Border
    {
        private readonly Image _image = new Image();
        private readonly TextBlock _message = new TextBlock();

        private LoadedImage _content;
        private GifPlayer _player;
        private double _contentW, _contentH;
        private double _scale = 1.0, _offX, _offY;
        private bool _fitMode = true;

        private bool _dragging;
        private Point _dragStart;
        private double _dragOffX, _dragOffY;

        internal ViewSurface()
        {
            Background = Brushes.Transparent; // nécessaire au test de collision souris
            ClipToBounds = true;

            _image.Stretch = Stretch.Fill;
            _image.HorizontalAlignment = HorizontalAlignment.Left;
            _image.VerticalAlignment = VerticalAlignment.Top;
            _image.SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

            _message.HorizontalAlignment = HorizontalAlignment.Center;
            _message.VerticalAlignment = VerticalAlignment.Center;
            _message.TextAlignment = TextAlignment.Center;
            _message.FontSize = 15;
            _message.Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            _message.TextWrapping = TextWrapping.Wrap;
            _message.MaxWidth = 460;
            _message.Visibility = Visibility.Collapsed;

            // L'Image vit dans un Canvas : emplacement d'arrangement infini, donc pas de layout clip
            // quand l'image dépasse la fenêtre (sinon WPF rogne AVANT le RenderTransform de zoom).
            var canvas = new Canvas();
            canvas.Children.Add(_image);
            var grid = new Grid();
            grid.Children.Add(canvas);
            grid.Children.Add(_message);
            Child = grid;

            SizeChanged += OnSizeChanged;
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnMouseDown;
            MouseLeftButtonUp += OnMouseUp;
            MouseMove += OnMouseMove;
        }

        internal double Scale { get { return _scale; } }
        internal bool HasContent { get { return _content != null && _content.Error == null; } }

        // Couleur du message d'accueil/erreur selon le fond (lisible sur blanc comme sur anthracite).
        internal void SetMessageDark(bool darkText)
        {
            _message.Foreground = new SolidColorBrush(darkText
                ? Color.FromArgb(0xA0, 0x20, 0x20, 0x20)
                : Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        }

        internal void ShowMessage(string text)
        {
            StopPlayer();
            _content = null;
            _image.Source = null;
            _message.Text = text;
            _message.Visibility = Visibility.Visible;
        }

        internal void SetContent(LoadedImage content)
        {
            StopPlayer();
            _content = content;
            if (content == null || content.Error != null)
            {
                _image.Source = null;
                _message.Text = content != null ? content.Error : "";
                _message.Visibility = Visibility.Visible;
                return;
            }
            _message.Visibility = Visibility.Collapsed;
            _contentW = content.DisplayWidth;
            _contentH = content.DisplayHeight;
            _image.Width = _contentW;
            _image.Height = _contentH;
            switch (content.Kind)
            {
                case ImageKind.Static:
                    _image.Source = content.Bitmap;
                    break;
                case ImageKind.Animated:
                    _player = new GifPlayer(content.Gif);
                    _image.Source = _player.Canvas;
                    _player.Start();
                    break;
                case ImageKind.Vector:
                    _image.Source = content.Vector;
                    break;
            }
            _fitMode = true;
            Refit();
        }

        private void StopPlayer()
        {
            if (_player != null) { _player.Stop(); _player = null; }
        }

        internal void Shutdown() { StopPlayer(); }

        // Bitmap figé de ce qui est affiché, pour le presse-papiers.
        internal BitmapSource SnapshotBitmap()
        {
            if (_content == null || _content.Error != null) return null;
            switch (_content.Kind)
            {
                case ImageKind.Static:
                    return _content.Bitmap;
                case ImageKind.Animated:
                    return _content.Gif.ComposeFrame(0);
                case ImageKind.Vector:
                    int w = Math.Max(1, (int)Math.Round(_contentW));
                    int h = Math.Max(1, (int)Math.Round(_contentH));
                    var dv = new DrawingVisual();
                    using (DrawingContext dc = dv.RenderOpen())
                        dc.DrawImage(_content.Vector, new Rect(0, 0, w, h));
                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.Freeze();
                    return rtb;
            }
            return null;
        }

        // ---- Ajustement, zoom, pan ----

        private double FitScale()
        {
            if (_contentW <= 0 || _contentH <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return 1.0;
            double s = Math.Min(ActualWidth / _contentW, ActualHeight / _contentH);
            // Le raster n'est jamais agrandi au-delà de 100 % en mode ajusté ; le vectoriel, si (il reste net).
            bool vector = _content != null && _content.Kind == ImageKind.Vector;
            return vector ? s : Math.Min(1.0, s);
        }

        internal void Refit()
        {
            if (_content == null || _content.Error != null) return;
            _scale = FitScale();
            _offX = (ActualWidth - _contentW * _scale) / 2;
            _offY = (ActualHeight - _contentH * _scale) / 2;
            _fitMode = true;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _image.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _offX, _offY));
            bool raster = _content != null && _content.Kind != ImageKind.Vector;
            RenderOptions.SetBitmapScalingMode(_image, raster && _scale >= 3.0
                ? BitmapScalingMode.NearestNeighbor
                : BitmapScalingMode.HighQuality);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitMode) Refit();
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_content == null || _content.Error != null) return;
            double factor = e.Delta > 0 ? 1.25 : 1.0 / 1.25;
            Point p = e.GetPosition(this);
            double target = _scale * factor;
            double fit = FitScale();
            double min = Math.Min(fit, 1.0) * 0.1;
            double max = Math.Max(40.0, fit * 4);
            target = Math.Max(min, Math.Min(max, target));
            if (Math.Abs(target - _scale) < 1e-9) return;
            // Le point sous le curseur reste sous le curseur.
            _offX = p.X - (p.X - _offX) * (target / _scale);
            _offY = p.Y - (p.Y - _offY) * (target / _scale);
            _scale = target;
            _fitMode = false;
            ApplyTransform();
            e.Handled = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Refit();
                return;
            }
            if (_content == null || _content.Error != null) return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _dragOffX = _offX;
            _dragOffY = _offY;
            CaptureMouse();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point p = e.GetPosition(this);
            _offX = _dragOffX + (p.X - _dragStart.X);
            _offY = _dragOffY + (p.Y - _dragStart.Y);
            _fitMode = false;
            ApplyTransform();
        }
    }
}
