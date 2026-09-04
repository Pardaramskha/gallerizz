using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Gallerizz
{
    // Le remplaçant maison de MessageBox, sur le modèle de celui de Marabook :
    // une Window ordinaire au thème sombre de l'app, pastille de couleur à la place
    // de l'icône système, boutons arrondis. Fermer par la croix ou Échap rend le
    // refus le plus sûr (Non ou Annuler selon les boutons, OK quand il n'y a que lui).
    internal sealed class MessageDialog : Window
    {
        // Palette du dialogue, alignée sur l'app (anthracite + or de l'icône).
        private static readonly Color BgColor = Color.FromRgb(0x33, 0x35, 0x39);
        private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0xEC, 0xED, 0xEE)));
        private static readonly Brush BtnBg = Freeze(new SolidColorBrush(Color.FromRgb(0x42, 0x45, 0x4B)));
        private static readonly Brush BtnHover = Freeze(new SolidColorBrush(Color.FromRgb(0x4E, 0x52, 0x59)));
        private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xB8, 0x4B)));
        private static readonly Brush AccentHover = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xC8, 0x66)));
        private static readonly Brush AccentInk = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x24, 0x12)));
        private static readonly Brush Danger = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F)));
        private static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x3C)));

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        private MessageBoxResult _result;
        private MessageBoxResult _defaultResult;

        private MessageDialog(Window owner, string text, string caption,
            MessageBoxButton buttons, MessageBoxImage image)
        {
            Title = caption ?? "";
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(BgColor);
            Native.DarkenChrome(this);
            _result = DismissResult(buttons);
            _defaultResult = buttons == MessageBoxButton.OK || buttons == MessageBoxButton.OKCancel
                ? MessageBoxResult.OK : MessageBoxResult.Yes;

            var panel = new StackPanel();
            panel.Margin = new Thickness(20, 18, 20, 16);
            panel.MaxWidth = 480;

            var body = new DockPanel();
            FrameworkElement glyph = Glyph(image);
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Top;
                glyph.Margin = new Thickness(0, 1, 12, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                body.Children.Add(glyph);
            }
            var msg = new TextBlock();
            msg.Text = text ?? "";
            msg.Foreground = Ink;
            msg.FontSize = 13;
            msg.TextWrapping = TextWrapping.Wrap;
            msg.VerticalAlignment = VerticalAlignment.Center;
            body.Children.Add(msg);
            panel.Children.Add(body);

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, 18, 0, 0);
            if (buttons == MessageBoxButton.OK || buttons == MessageBoxButton.OKCancel)
                row.Children.Add(Choice("OK", MessageBoxResult.OK, true));
            if (buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel)
            {
                row.Children.Add(Choice("Oui", MessageBoxResult.Yes, true));
                row.Children.Add(Choice("Non", MessageBoxResult.No, false));
            }
            if (buttons == MessageBoxButton.OKCancel || buttons == MessageBoxButton.YesNoCancel)
                row.Children.Add(Choice("Annuler", MessageBoxResult.Cancel, false));
            panel.Children.Add(row);
            Content = panel;

            KeyDown += OnKey;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { _result = _defaultResult; Close(); e.Handled = true; }
        }

        private static MessageBoxResult DismissResult(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK: return MessageBoxResult.OK;
                case MessageBoxButton.YesNo: return MessageBoxResult.No;
                default: return MessageBoxResult.Cancel;
            }
        }

        // Bouton arrondi maison : Border + états visuels à la souris.
        private Border Choice(string label, MessageBoxResult result, bool primary)
        {
            var txt = new TextBlock();
            txt.Text = label;
            txt.FontSize = 13;
            txt.Foreground = primary ? AccentInk : Ink;
            if (primary) txt.FontWeight = FontWeights.SemiBold;
            txt.HorizontalAlignment = HorizontalAlignment.Center;

            var btn = new Border();
            btn.CornerRadius = new CornerRadius(6);
            btn.Background = primary ? Accent : BtnBg;
            btn.Padding = new Thickness(18, 7, 18, 7);
            btn.Margin = new Thickness(8, 0, 0, 0);
            btn.MinWidth = 88;
            btn.Cursor = Cursors.Hand;
            btn.Child = txt;
            btn.MouseEnter += delegate { btn.Background = primary ? AccentHover : BtnHover; };
            btn.MouseLeave += delegate { btn.Background = primary ? Accent : BtnBg; };
            btn.MouseLeftButtonUp += delegate { _result = result; Close(); };
            return btn;
        }

        // La pastille de couleur de sens, à la place de l'icône système.
        private static FrameworkElement Glyph(MessageBoxImage image)
        {
            string sign;
            Brush brush;
            switch (image)
            {
                case MessageBoxImage.Error: sign = "✕"; brush = Danger; break;
                case MessageBoxImage.Warning: sign = "!"; brush = Warn; break;
                case MessageBoxImage.Question: sign = "?"; brush = Accent; break;
                case MessageBoxImage.Information: sign = "i"; brush = Accent; break;
                default: return null;
            }
            var pill = new Border();
            pill.Width = 26;
            pill.Height = 26;
            pill.CornerRadius = new CornerRadius(13);
            pill.Background = brush;
            var glyph = new TextBlock();
            glyph.Text = sign;
            glyph.Foreground = Freeze(new SolidColorBrush(Color.FromRgb(0x24, 0x20, 0x16)));
            glyph.FontSize = 14;
            glyph.FontWeight = FontWeights.Bold;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            pill.Child = glyph;
            return pill;
        }

        internal static MessageBoxResult Show(Window owner, string text, string caption,
            MessageBoxButton buttons, MessageBoxImage image)
        {
            var dialog = new MessageDialog(owner, text, caption, buttons, image);
            dialog.ShowDialog();
            return dialog._result;
        }
    }
}
