using System;
using System.Runtime.InteropServices;

namespace Gallerizz
{
    // P/Invoke : tri naturel de l'Explorateur et envoi à la corbeille.
    internal static class Native
    {
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
