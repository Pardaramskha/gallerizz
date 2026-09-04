using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

// L'installeur de Gallerizz : deballe l'archive embarquee dans un dossier "Gallerizz"
// cree a cote de l'installeur, puis l'annonce dans un dialogue au theme sombre de l'app.
// Pas de registre, pas de droits admin, pas de magie.
internal static class Setup
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string target = null;
        string error = null;
        try
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            target = Path.Combine(baseDir, "Gallerizz");
            using (Stream s = Assembly.GetEntryAssembly().GetManifestResourceStream("app.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                Directory.CreateDirectory(target);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    string dest = Path.Combine(target, entry.FullName.Replace('/', '\\'));
                    string destDir = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(dest, true); // ecrase : reinstaller = mettre a jour
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error != null)
        {
            SetupDialog.Show("Installation impossible : " + error, "Gallerizz", null);
        }
        else
        {
            bool open = SetupDialog.Show(
                "Gallerizz est installé dans :\n" + target +
                "\n\nPour en faire votre visualiseur par défaut : clic droit sur une image → Ouvrir avec → Gallerizz.",
                "Gallerizz — installation terminée", "Ouvrir le dossier");
            if (open)
                System.Diagnostics.Process.Start("explorer.exe", "\"" + target + "\"");
        }
        app.Shutdown();
    }
}

// Dialogue maison au theme de Gallerizz : anthracite, boutons arrondis, pas d'icone systeme.
internal sealed class SetupDialog : Window
{
    private static readonly Brush Ink = Frozen(0xEC, 0xED, 0xEE);
    private static readonly Brush BtnBg = Frozen(0x42, 0x45, 0x4B);
    private static readonly Brush BtnHover = Frozen(0x4E, 0x52, 0x59);
    private static readonly Brush Accent = Frozen(0xE8, 0xB8, 0x4B);
    private static readonly Brush AccentHover = Frozen(0xF2, 0xC8, 0x66);
    private static readonly Brush AccentInk = Frozen(0x2B, 0x24, 0x12);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private bool _accepted;

    private SetupDialog(string text, string caption, string primaryLabel)
    {
        Title = caption;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x33, 0x35, 0x39));
        SourceInitialized += delegate
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int one = 1;
                DwmSetWindowAttribute(hwnd, 20, ref one, 4);
                int color = 0x302D2B;
                DwmSetWindowAttribute(hwnd, 35, ref color, 4);
            }
            catch { }
        };

        var panel = new StackPanel();
        panel.Margin = new Thickness(20, 18, 20, 16);
        panel.MaxWidth = 460;
        var msg = new TextBlock();
        msg.Text = text;
        msg.Foreground = Ink;
        msg.FontSize = 13;
        msg.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(msg);

        var row = new StackPanel();
        row.Orientation = Orientation.Horizontal;
        row.HorizontalAlignment = HorizontalAlignment.Right;
        row.Margin = new Thickness(0, 18, 0, 0);
        if (primaryLabel != null) row.Children.Add(MakeButton(primaryLabel, true));
        row.Children.Add(MakeButton("Fermer", false));
        panel.Children.Add(row);
        Content = panel;

        KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { _accepted = primaryLabel != null; Close(); }
        };
    }

    private Border MakeButton(string label, bool primary)
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
        btn.MouseLeftButtonUp += delegate { _accepted = primary; Close(); };
        return btn;
    }

    // Retourne vrai si le bouton principal a ete choisi.
    internal static bool Show(string text, string caption, string primaryLabel)
    {
        var dialog = new SetupDialog(text, caption, primaryLabel);
        dialog.ShowDialog();
        return dialog._accepted;
    }
}
