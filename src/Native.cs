using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Gallerizz
{
    // P/Invoke : tri naturel de l'Explorateur, envoi à la corbeille, chrome sombre.
    internal static class Native
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint color);

        private const int GCLP_HBRBACKGROUND = -10;
        private const uint AnthraciteRef = 0x302D2B; // COLORREF 0x00BBGGRR de #2B2D30

        // Barre de titre et bordure anthracite (Windows 10 1809+/11), et brosse de fond
        // sombre sur la classe de fenêtre : c'est elle qui cause le flash blanc à l'ouverture.
        internal static void DarkenChrome(Window window)
        {
            window.SourceInitialized += delegate
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) return;
                    int one = 1;
                    DwmSetWindowAttribute(hwnd, 20, ref one, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(hwnd, 19, ref one, 4); // variante des builds plus anciens
                    int color = unchecked((int)AnthraciteRef);
                    DwmSetWindowAttribute(hwnd, 35, ref color, 4); // DWMWA_CAPTION_COLOR (Win11)
                    DwmSetWindowAttribute(hwnd, 34, ref color, 4); // DWMWA_BORDER_COLOR (Win11)
                    SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, CreateSolidBrush(AnthraciteRef));
                }
                catch { } // esthétique seulement : jamais bloquant
            };
        }
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        internal static extern int StrCmpLogicalW(string a, string b);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

        private const uint FO_DELETE = 3;
        private const ushort FOF_ALLOWUNDO = 0x40;
        private const ushort FOF_NOCONFIRMATION = 0x10;
        private const ushort FOF_SILENT = 0x4;
        private const ushort FOF_NOERRORUI = 0x400;

        // Envoie le fichier à la corbeille (sans dialogue système : la confirmation est la nôtre).
        internal static bool SendToRecycleBin(string path)
        {
            var op = new SHFILEOPSTRUCT();
            op.wFunc = FO_DELETE;
            op.pFrom = path + "\0"; // le marshalling ajoute le second terminateur
            op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);
            int rc = SHFileOperationW(ref op);
            return rc == 0 && !op.fAnyOperationsAborted;
        }
    }
}
