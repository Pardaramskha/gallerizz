using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gallerizz
{
    // Liste des images du dossier courant, tri naturel (celui de l'Explorateur), index, navigation bouclée.
    internal sealed class FolderNav
    {
        private static readonly HashSet<string> Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".webp", ".svg",
            ".tif", ".tiff", ".ico", ".avif", ".heic", ".heif"
        };

        private List<string> _files = new List<string>();
        private int _index = -1;

        internal static bool IsSupported(string path)
        {
            return Exts.Contains(Path.GetExtension(path) ?? "");
        }

        internal static string FileDialogFilter
        {
            get
            {
                return "Images|*.jpg;*.jpeg;*.jfif;*.png;*.gif;*.bmp;*.webp;*.svg;*.tif;*.tiff;*.ico;*.avif;*.heic;*.heif|Tous les fichiers|*.*";
            }
        }

        internal int Count { get { return _files.Count; } }
        internal int Index { get { return _index; } }
        internal string Current { get { return (_index >= 0 && _index < _files.Count) ? _files[_index] : null; } }

        // Charge le dossier du fichier donné et se positionne dessus.
        internal void Load(string filePath)
        {
            _files.Clear();
            _index = -1;
            try
            {
                string full = Path.GetFullPath(filePath);
                string dir = Path.GetDirectoryName(full);
                if (dir == null || !Directory.Exists(dir)) { _files.Add(full); _index = 0; return; }
                _files = Directory.GetFiles(dir).Where(IsSupported).ToList();
                _files.Sort(NaturalCompare);
                _index = _files.FindIndex(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase));
                if (_index < 0) { _files.Insert(0, full); _index = 0; }
            }
            catch
            {
                _files.Clear();
                _files.Add(filePath);
                _index = 0;
            }
        }

        internal static int NaturalCompare(string a, string b)
        {
            return Native.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
        }

        // Avance de dir (+1/-1) en bouclant ; écarte au passage les fichiers disparus.
        internal string Move(int dir)
        {
            while (_files.Count > 0)
            {
                _index = ((_index + dir) % _files.Count + _files.Count) % _files.Count;
                if (File.Exists(_files[_index])) return _files[_index];
                _files.RemoveAt(_index);
                if (dir > 0) _index--; // l'élément suivant a glissé sur _index : le prochain +1 doit tomber dessus
            }
            _index = -1;
            return null;
        }

        // Chemin voisin sans bouger l'index (pour le préchargement).
        internal string Peek(int dir)
        {
            if (_files.Count == 0 || _index < 0) return null;
            int i = ((_index + dir) % _files.Count + _files.Count) % _files.Count;
            return i == _index ? null : _files[i];
        }

        // Retire l'image courante (supprimée) et se place sur la suivante.
        internal string RemoveCurrent()
        {
            if (_index < 0 || _index >= _files.Count) return null;
            _files.RemoveAt(_index);
            if (_files.Count == 0) { _index = -1; return null; }
            if (_index >= _files.Count) _index = 0;
            return _files[_index];
        }
    }
}
