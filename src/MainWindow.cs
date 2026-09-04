using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallerizz
{
    internal sealed class MainWindow : Window
    {
        private static readonly Color[] Backgrounds =
        {
            Color.FromRgb(0x2B, 0x2D, 0x30), // anthracite (défaut)
            Color.FromRgb(0x80, 0x80, 0x80), // gris
            Color.FromRgb(0xFF, 0xFF, 0xFF)  // blanc
        };
        private static readonly string[] BackgroundNames = { "anthracite", "gris", "blanc" };

        private readonly FolderNav _nav = new FolderNav();
        private readonly ViewSurface _surface = new ViewSurface();
        private Border _infoPanel;
        private TextBlock _infoText;
        private Border _bgButton;
        private TextBlock _bgGlyph;

        private int _bg;
        private LoadedImage _current;
        private int _generation;
        private readonly Dictionary<string, LoadedImage> _cache = new Dictionary<string, LoadedImage>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _fullscreen;
        private WindowState _savedState;
        private WindowStyle _savedStyle;
        private ResizeMode _savedResize;

        internal MainWindow(string initialFile)
        {
            Title = "Gallerizz";
            Width = 1100;
            Height = 780;
            MinWidth = 420;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            AllowDrop = true;
            Native.DarkenChrome(this); // barre de titre anthracite + anti-flash blanc

            _bg = Math.Max(0, Math.Min(Backgrounds.Length - 1, Settings.GetInt("background", 0)));

            BuildLayout();
            ApplyBackground();

            PreviewKeyDown += OnKey;
            Drop += OnDrop;
            Closed += delegate { _surface.Shutdown(); };

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile)) OpenFile(initialFile);
            else ShowWelcome();
        }

        private void BuildLayout()
        {
            var root = new Grid();
            root.Children.Add(_surface);

            // Panneau d'informations (touche I), coin haut-gauche.
            _infoText = new TextBlock();
            _infoText.Foreground = Brushes.White;
            _infoText.FontSize = 13;
            _infoText.LineHeight = 20;
            _infoPanel = new Border();
            _infoPanel.Background = new SolidColorBrush(Color.FromArgb(0xD2, 0x1E, 0x1F, 0x22));
            _infoPanel.CornerRadius = new CornerRadius(8);
            _infoPanel.Padding = new Thickness(14, 10, 14, 10);
            _infoPanel.Margin = new Thickness(14);
            _infoPanel.HorizontalAlignment = HorizontalAlignment.Left;
            _infoPanel.VerticalAlignment = VerticalAlignment.Top;
            _infoPanel.Child = _infoText;
            _infoPanel.Visibility = Visibility.Collapsed;
            _infoPanel.IsHitTestVisible = false;
            root.Children.Add(_infoPanel);

            // Bouton discret de cycle du fond, coin bas-droit.
            _bgGlyph = new TextBlock();
            _bgGlyph.Text = "◐";
            _bgGlyph.FontSize = 15;
            _bgGlyph.Foreground = Brushes.White;
            _bgGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            _bgGlyph.VerticalAlignment = VerticalAlignment.Center;
            _bgButton = new Border();
            _bgButton.Width = 30;
            _bgButton.Height = 30;
            _bgButton.CornerRadius = new CornerRadius(15);
            _bgButton.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00));
            _bgButton.Margin = new Thickness(14);
            _bgButton.HorizontalAlignment = HorizontalAlignment.Right;
            _bgButton.VerticalAlignment = VerticalAlignment.Bottom;
            _bgButton.Cursor = Cursors.Hand;
            _bgButton.Child = _bgGlyph;
            _bgButton.ToolTip = "Couleur de fond : anthracite / gris / blanc (touche B)";
            _bgButton.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { CycleBackground(); e.Handled = true; };
            root.Children.Add(_bgButton);

            Content = root;
        }

        private void ShowWelcome()
        {
            _surface.ShowMessage("Glissez une image ici,\nou Ctrl+O pour en ouvrir une.\n\n← → naviguer   B fond   F plein écran   I infos");
        }

        // ---- Fond ----

        private void CycleBackground()
        {
            _bg = (_bg + 1) % Backgrounds.Length;
            Settings.SetInt("background", _bg);
            ApplyBackground();
        }

        private void ApplyBackground()
        {
            Background = new SolidColorBrush(Backgrounds[_bg]);
            _surface.SetMessageDark(_bg == 2);
            _bgGlyph.Foreground = _bg == 2 ? Brushes.White : Brushes.White;
        }

        internal int BackgroundIndex { get { return _bg; } }

        // ---- Ouverture et navigation ----

        internal void OpenFile(string path)
        {
            try { path = Path.GetFullPath(path); }
            catch { }
            _nav.Load(path);
            ShowPath(path);
        }

        // Chargement hors fil UI, retour explicite par le Dispatcher : ShowPath peut être appelé
        // avant même app.Run (ouverture par ligne de commande), où aucun SynchronizationContext n'existe.
        private void ShowPath(string path)
        {
            int gen = ++_generation;
            LoadedImage cached;
            if (_cache.TryGetValue(path, out cached))
            {
                ApplyLoaded(cached);
                return;
            }
            Task.Run(() => ImageLoader.Load(path)).ContinueWith(t =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (gen != _generation) return;
                    _cache[path] = t.Result;
                    ApplyLoaded(t.Result);
                })));
        }

        private void ApplyLoaded(LoadedImage img)
        {
            _current = img;
            _surface.SetContent(img);
            UpdateTitle();
            UpdateInfoPanel();
            PruneCache();
            Preload(_nav.Peek(1));
            Preload(_nav.Peek(-1));
        }

        private void Preload(string path)
        {
            if (path == null || _cache.ContainsKey(path) || _loading.Contains(path)) return;
            _loading.Add(path);
            Task.Run(() => ImageLoader.Load(path)).ContinueWith(t =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _loading.Remove(path);
                    _cache[path] = t.Result;
                    PruneCache();
                })));
        }

        private void PruneCache()
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_nav.Current != null) keep.Add(_nav.Current);
            string n = _nav.Peek(1); if (n != null) keep.Add(n);
            string p = _nav.Peek(-1); if (p != null) keep.Add(p);
            var stale = new List<string>();
            foreach (string key in _cache.Keys) if (!keep.Contains(key)) stale.Add(key);
            foreach (string key in stale) _cache.Remove(key);
        }

        private void Navigate(int dir)
        {
            string path = _nav.Move(dir);
            if (path == null)
            {
                ClearAll();
                return;
            }
            ShowPath(path);
        }

        private void ClearAll()
        {
            _current = null;
            _generation++;
            Title = "Gallerizz";
            _infoPanel.Visibility = Visibility.Collapsed;
            ShowWelcome();
        }

        private void UpdateTitle()
        {
            if (_current == null) { Title = "Gallerizz"; return; }
            var sb = new StringBuilder();
            sb.Append(_current.Info.FileName);
            if (_nav.Count > 1)
                sb.Append(string.Format(CultureInfo.InvariantCulture, " — {0}/{1}", _nav.Index + 1, _nav.Count));
            if (_current.Error == null)
                sb.Append(string.Format(CultureInfo.InvariantCulture, " — {0}×{1}", _current.Info.PixelWidth, _current.Info.PixelHeight));
            sb.Append(" — Gallerizz");
            Title = sb.ToString();
        }

        // ---- Panneau d'informations ----

        private void ToggleInfo()
        {
            if (_infoPanel.Visibility == Visibility.Visible) { _infoPanel.Visibility = Visibility.Collapsed; return; }
            if (_current == null) return;
            UpdateInfoPanel();
            _infoPanel.Visibility = Visibility.Visible;
        }

        private void UpdateInfoPanel()
        {
            if (_current == null) { _infoPanel.Visibility = Visibility.Collapsed; return; }
            ImageInfo i = _current.Info;
            var sb = new StringBuilder();
            sb.AppendLine(i.FileName);
            sb.AppendLine(i.Folder);
            sb.AppendLine();
            if (_current.Error == null)
            {
                sb.AppendLine("Format : " + i.FormatName + (i.FrameCount > 1 ? string.Format(" ({0} images)", i.FrameCount) : ""));
                sb.AppendLine(string.Format("Dimensions : {0} × {1} px", i.PixelWidth, i.PixelHeight));
            }
            else sb.AppendLine("Format : " + i.FormatName);
            sb.AppendLine("Poids : " + FormatSize(i.FileSize));
            sb.Append("Modifié : " + i.Modified.ToString("d MMMM yyyy HH:mm", CultureInfo.CurrentCulture));
            if (i.TakenDate != null) sb.Append("\nPrise de vue : " + i.TakenDate);
            if (i.Camera != null) sb.Append("\nAppareil : " + i.Camera);
            _infoText.Text = sb.ToString();
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0.##} Go", bytes / (1024.0 * 1024 * 1024));
            if (bytes >= 1024 * 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0.#} Mo", bytes / (1024.0 * 1024));
            if (bytes >= 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0.#} Ko", bytes / 1024.0);
            return bytes + " o";
        }

        // ---- Plein écran ----

        private void ToggleFullscreen()
        {
            if (!_fullscreen)
            {
                _savedState = WindowState;
                _savedStyle = WindowStyle;
                _savedResize = ResizeMode;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal; // force le recalcul si déjà maximisée
                WindowState = WindowState.Maximized;
                _fullscreen = true;
            }
            else
            {
                WindowStyle = _savedStyle;
                ResizeMode = _savedResize;
                WindowState = _savedState;
                _fullscreen = false;
            }
            // Quel que soit le sens de la bascule : recentrer et réajuster une fois la fenêtre reposée.
            Dispatcher.BeginInvoke(new Action(_surface.Refit), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ---- Presse-papiers, corbeille ----

        private void CopyToClipboard()
        {
            if (_current == null || _current.Error != null) return;
            try
            {
                BitmapSource bmp = _surface.SnapshotBitmap();
                var data = new DataObject();
                if (bmp != null) data.SetImage(bmp);
                if (File.Exists(_current.Path))
                {
                    var files = new System.Collections.Specialized.StringCollection();
                    files.Add(_current.Path);
                    data.SetFileDropList(files);
                }
                Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "Copie impossible : " + ex.Message, "Gallerizz", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteCurrent()
        {
            if (_current == null) return;
            string path = _nav.Current;
            if (path == null || !File.Exists(path)) return;
            MessageBoxResult res = MessageDialog.Show(this,
                string.Format("Envoyer « {0} » à la corbeille ?", Path.GetFileName(path)),
                "Gallerizz", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            _surface.Shutdown(); // libère le fichier avant la suppression
            if (!Native.SendToRecycleBin(path))
            {
                MessageDialog.Show(this, "La suppression a échoué.", "Gallerizz", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowPath(path);
                return;
            }
            _cache.Remove(path);
            string next = _nav.RemoveCurrent();
            if (next == null) ClearAll();
            else ShowPath(next);
        }

        // ---- Entrées ----

        private void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            switch (e.Key)
            {
                case Key.Left: Navigate(-1); e.Handled = true; break;
                case Key.Right: Navigate(1); e.Handled = true; break;
                case Key.B: if (!ctrl) { CycleBackground(); e.Handled = true; } break;
                case Key.F: if (!ctrl) { ToggleFullscreen(); e.Handled = true; } break;
                case Key.I: if (!ctrl) { ToggleInfo(); e.Handled = true; } break;
                case Key.C: if (ctrl) { CopyToClipboard(); e.Handled = true; } break;
                case Key.O: if (ctrl) { OpenDialog(); e.Handled = true; } break;
                case Key.Delete: DeleteCurrent(); e.Handled = true; break;
                case Key.Escape:
                    if (_fullscreen) ToggleFullscreen();
                    else Close();
                    e.Handled = true;
                    break;
            }
        }

        private void OpenDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = FolderNav.FileDialogFilter;
            dlg.Title = "Ouvrir une image";
            if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string f in files)
            {
                if (File.Exists(f)) { OpenFile(f); return; }
            }
        }
    }
}
