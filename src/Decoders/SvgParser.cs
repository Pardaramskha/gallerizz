using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Gallerizz
{
    // Une règle CSS interne (<style>) réduite aux sélecteurs simples : tag, .classe, #id et leurs combinaisons.
    internal sealed class CssRule
    {
        public string Tag;          // null = tout élément
        public List<string> Classes = new List<string>();
        public string Id;           // null = indifférent
        public int Specificity;
        public int Order;
        public Dictionary<string, string> Props = new Dictionary<string, string>();

        public bool Matches(string tag, HashSet<string> classes, string id)
        {
            if (Tag != null && !string.Equals(Tag, tag, StringComparison.Ordinal)) return false;
            if (Id != null && !string.Equals(Id, id, StringComparison.Ordinal)) return false;
            foreach (string c in Classes)
                if (classes == null || !classes.Contains(c)) return false;
            return true;
        }
    }

    // Boîte à outils de parsing SVG : nombres, longueurs, transformations, couleurs, données de chemin, mini-CSS.
    internal static class SvgUtil
    {
        internal static bool TryParseDouble(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        internal static List<double> ParseNumberList(string s)
        {
            var result = new List<double>();
            if (s == null) return result;
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                int start = i;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                if (i == start) { i++; continue; }
                double v;
                if (TryParseDouble(s.Substring(start, i - start), out v)) result.Add(v);
            }
            return result;
        }

        // Longueur CSS → pixels (96 dpi). reference sert aux pourcentages ; NaN si % sans référence.
        internal static double ParseLength(string s, double reference, double fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim();
            double mult = 1.0;
            if (s.EndsWith("%"))
            {
                if (double.IsNaN(reference)) return fallback;
                s = s.Substring(0, s.Length - 1);
                double p;
                return TryParseDouble(s, out p) ? p / 100.0 * reference : fallback;
            }
            if (s.EndsWith("px")) { s = s.Substring(0, s.Length - 2); }
            else if (s.EndsWith("pt")) { s = s.Substring(0, s.Length - 2); mult = 96.0 / 72.0; }
            else if (s.EndsWith("pc")) { s = s.Substring(0, s.Length - 2); mult = 16.0; }
            else if (s.EndsWith("mm")) { s = s.Substring(0, s.Length - 2); mult = 96.0 / 25.4; }
            else if (s.EndsWith("cm")) { s = s.Substring(0, s.Length - 2); mult = 96.0 / 2.54; }
            else if (s.EndsWith("in")) { s = s.Substring(0, s.Length - 2); mult = 96.0; }
            else if (s.EndsWith("em")) { s = s.Substring(0, s.Length - 2); mult = 16.0; }
            else if (s.EndsWith("ex")) { s = s.Substring(0, s.Length - 2); mult = 8.0; }
            double v2;
            return TryParseDouble(s, out v2) ? v2 * mult : fallback;
        }

        // Liste de transformations SVG → matrice WPF (convention vecteur-ligne : la plus à droite s'applique d'abord).
        internal static Matrix ParseTransform(string s)
        {
            var m = Matrix.Identity;
            if (string.IsNullOrEmpty(s)) return m;
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                int ns = i;
                while (i < s.Length && (char.IsLetter(s[i]))) i++;
                if (i == ns) { i++; continue; }
                string name = s.Substring(ns, i - ns);
                while (i < s.Length && s[i] != '(') i++;
                if (i >= s.Length) break;
                int close = s.IndexOf(')', i);
                if (close < 0) break;
                List<double> a = ParseNumberList(s.Substring(i + 1, close - i - 1));
                i = close + 1;

                Matrix item = Matrix.Identity;
                switch (name)
                {
                    case "matrix":
                        if (a.Count >= 6) item = new Matrix(a[0], a[1], a[2], a[3], a[4], a[5]);
                        break;
                    case "translate":
                        if (a.Count >= 1) item = new Matrix(1, 0, 0, 1, a[0], a.Count >= 2 ? a[1] : 0);
                        break;
                    case "scale":
                        if (a.Count >= 1) item = new Matrix(a[0], 0, 0, a.Count >= 2 ? a[1] : a[0], 0, 0);
                        break;
                    case "rotate":
                        if (a.Count >= 1)
                        {
                            item = Matrix.Identity;
                            if (a.Count >= 3) item.RotateAt(a[0], a[1], a[2]);
                            else item.Rotate(a[0]);
                        }
                        break;
                    case "skewX":
                        if (a.Count >= 1) item = new Matrix(1, 0, Math.Tan(a[0] * Math.PI / 180.0), 1, 0, 0);
                        break;
                    case "skewY":
                        if (a.Count >= 1) item = new Matrix(1, Math.Tan(a[0] * Math.PI / 180.0), 0, 1, 0, 0);
                        break;
                }
                m = Matrix.Multiply(item, m);
            }
            return m;
        }

        internal static bool TryParseColor(string s, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            try
            {
                if (s.StartsWith("#"))
                {
                    string hex = s.Substring(1);
                    if (hex.Length == 3 || hex.Length == 4)
                    {
                        var sb = new StringBuilder();
                        foreach (char c in hex) { sb.Append(c); sb.Append(c); }
                        hex = sb.ToString();
                    }
                    if (hex.Length == 6)
                    {
                        color = Color.FromRgb(H2(hex, 0), H2(hex, 2), H2(hex, 4));
                        return true;
                    }
                    if (hex.Length == 8) // CSS : RRGGBBAA
                    {
                        color = Color.FromArgb(H2(hex, 6), H2(hex, 0), H2(hex, 2), H2(hex, 4));
                        return true;
                    }
                    return false;
                }
                if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    int open = s.IndexOf('(');
                    int close = s.IndexOf(')');
                    if (open < 0 || close < open) return false;
                    string[] parts = s.Substring(open + 1, close - open - 1).Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) return false;
                    byte r = ChannelByte(parts[0]);
                    byte g = ChannelByte(parts[1]);
                    byte b = ChannelByte(parts[2]);
                    byte a = 255;
                    if (parts.Length >= 4)
                    {
                        double av;
                        if (TryParseDouble(parts[3].TrimEnd('%'), out av))
                            a = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(parts[3].EndsWith("%") ? av * 2.55 : av * 255)));
                    }
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
                object o = ColorConverter.ConvertFromString(s);
                if (o is Color) { color = (Color)o; return true; }
            }
            catch { }
            return false;
        }

        private static byte H2(string hex, int i)
        {
            return byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static byte ChannelByte(string s)
        {
            s = s.Trim();
            double v;
            if (s.EndsWith("%"))
            {
                if (TryParseDouble(s.Substring(0, s.Length - 1), out v))
                    return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 2.55)));
                return 0;
            }
            if (TryParseDouble(s, out v)) return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
            return 0;
        }

        // ---- Données de chemin (attribut d) ----

        private sealed class PathScanner
        {
            private readonly string _s;
            private int _i;
            public PathScanner(string s) { _s = s ?? ""; }

            public void SkipSep()
            {
                while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ',')) _i++;
            }

            public bool More { get { SkipSep(); return _i < _s.Length; } }

            public bool PeekIsCommand()
            {
                SkipSep();
                if (_i >= _s.Length) return false;
                char c = _s[_i];
                return char.IsLetter(c) && c != 'e' && c != 'E';
            }

            public char ReadCommand() { SkipSep(); return _s[_i++]; }

            public bool TryNumber(out double v)
            {
                SkipSep();
                int start = _i;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                }
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    int save = _i;
                    _i++;
                    if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (_i < _s.Length && char.IsDigit(_s[_i])) { while (_i < _s.Length && char.IsDigit(_s[_i])) _i++; }
                    else _i = save;
                }
                if (_i == start) { v = 0; return false; }
                return TryParseDouble(_s.Substring(start, _i - start), out v);
            }

            // Drapeau d'arc : un seul caractère 0/1, éventuellement collé au nombre suivant (SVG minifié).
            public bool TryFlag(out bool f)
            {
                SkipSep();
                f = false;
                if (_i >= _s.Length) return false;
                if (_s[_i] == '0') { _i++; return true; }
                if (_s[_i] == '1') { _i++; f = true; return true; }
                return false;
            }
        }

        internal static PathGeometry ParsePath(string d, FillRule rule)
        {
            var pg = new PathGeometry();
            pg.FillRule = rule;
            var sc = new PathScanner(d);
            PathFigure fig = null;
            Point cur = new Point(0, 0), start = cur, lastC = cur, lastQ = cur;
            char lastCmd = ' ';
            char cmd = ' ';
            try
            {
                while (sc.More)
                {
                    if (sc.PeekIsCommand()) cmd = sc.ReadCommand();
                    else if (cmd == 'M') cmd = 'L';
                    else if (cmd == 'm') cmd = 'l';
                    else if (cmd == ' ') break;

                    bool rel = char.IsLower(cmd);
                    char up = char.ToUpperInvariant(cmd);
                    double x, y, x1, y1, x2, y2;
                    switch (up)
                    {
                        case 'M':
                            if (!sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            cur = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                            start = cur;
                            fig = new PathFigure();
                            fig.StartPoint = cur;
                            fig.IsFilled = true;
                            pg.Figures.Add(fig);
                            break;
                        case 'L':
                            if (!sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            cur = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                            Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new LineSegment(cur, true));
                            break;
                        case 'H':
                            if (!sc.TryNumber(out x)) return pg;
                            cur = new Point(rel ? cur.X + x : x, cur.Y);
                            Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new LineSegment(cur, true));
                            break;
                        case 'V':
                            if (!sc.TryNumber(out y)) return pg;
                            cur = new Point(cur.X, rel ? cur.Y + y : y);
                            Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new LineSegment(cur, true));
                            break;
                        case 'C':
                            if (!sc.TryNumber(out x1) || !sc.TryNumber(out y1) || !sc.TryNumber(out x2) || !sc.TryNumber(out y2) || !sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            {
                                Point p1 = rel ? new Point(cur.X + x1, cur.Y + y1) : new Point(x1, y1);
                                Point p2 = rel ? new Point(cur.X + x2, cur.Y + y2) : new Point(x2, y2);
                                Point p = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                                Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new BezierSegment(p1, p2, p, true));
                                lastC = p2;
                                cur = p;
                            }
                            break;
                        case 'S':
                            if (!sc.TryNumber(out x2) || !sc.TryNumber(out y2) || !sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            {
                                char lu = char.ToUpperInvariant(lastCmd);
                                Point p1 = (lu == 'C' || lu == 'S') ? new Point(2 * cur.X - lastC.X, 2 * cur.Y - lastC.Y) : cur;
                                Point p2 = rel ? new Point(cur.X + x2, cur.Y + y2) : new Point(x2, y2);
                                Point p = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                                Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new BezierSegment(p1, p2, p, true));
                                lastC = p2;
                                cur = p;
                            }
                            break;
                        case 'Q':
                            if (!sc.TryNumber(out x1) || !sc.TryNumber(out y1) || !sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            {
                                Point p1 = rel ? new Point(cur.X + x1, cur.Y + y1) : new Point(x1, y1);
                                Point p = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                                Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new QuadraticBezierSegment(p1, p, true));
                                lastQ = p1;
                                cur = p;
                            }
                            break;
                        case 'T':
                            if (!sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            {
                                char lu = char.ToUpperInvariant(lastCmd);
                                Point p1 = (lu == 'Q' || lu == 'T') ? new Point(2 * cur.X - lastQ.X, 2 * cur.Y - lastQ.Y) : cur;
                                Point p = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                                Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new QuadraticBezierSegment(p1, p, true));
                                lastQ = p1;
                                cur = p;
                            }
                            break;
                        case 'A':
                            double rx, ry, rot;
                            bool large, sweep;
                            if (!sc.TryNumber(out rx) || !sc.TryNumber(out ry) || !sc.TryNumber(out rot) || !sc.TryFlag(out large) || !sc.TryFlag(out sweep) || !sc.TryNumber(out x) || !sc.TryNumber(out y)) return pg;
                            {
                                Point p = rel ? new Point(cur.X + x, cur.Y + y) : new Point(x, y);
                                if (Math.Abs(rx) < 1e-9 || Math.Abs(ry) < 1e-9)
                                    Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new LineSegment(p, true));
                                else
                                    Ensure(pg, ref fig, ref start, cur, false).Segments.Add(new ArcSegment(p, new Size(Math.Abs(rx), Math.Abs(ry)), rot, large,
                                        sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
                                cur = p;
                            }
                            break;
                        case 'Z':
                            if (fig != null) fig.IsClosed = true;
                            cur = start;
                            fig = null; // un tracé après Z rouvre une figure au point de départ
                            break;
                        default:
                            return pg; // commande inconnue : on rend ce qu'on a
                    }
                    lastCmd = cmd;
                }
            }
            catch { }
            return pg;
        }

        private static PathFigure Ensure(PathGeometry pg, ref PathFigure fig, ref Point start, Point cur, bool unused)
        {
            if (fig == null)
            {
                fig = new PathFigure();
                fig.StartPoint = start;
                fig.IsFilled = true;
                pg.Figures.Add(fig);
            }
            return fig;
        }

        // ---- Mini-CSS interne (<style>) ----

        internal static List<CssRule> ParseCss(string css)
        {
            var rules = new List<CssRule>();
            if (string.IsNullOrEmpty(css)) return rules;
            css = StripComments(css);
            int order = 0;
            int i = 0;
            while (i < css.Length)
            {
                int open = css.IndexOf('{', i);
                if (open < 0) break;
                int close = css.IndexOf('}', open);
                if (close < 0) break;
                string selectors = css.Substring(i, open - i);
                var props = ParseDeclarations(css.Substring(open + 1, close - open - 1));
                foreach (string sel in selectors.Split(','))
                {
                    CssRule r = ParseSelector(sel.Trim());
                    if (r == null) continue;
                    r.Order = order++;
                    foreach (var kv in props) r.Props[kv.Key] = kv.Value;
                    rules.Add(r);
                }
                i = close + 1;
            }
            return rules;
        }

        private static string StripComments(string s)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + 2;
                    continue;
                }
                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }

        private static CssRule ParseSelector(string sel)
        {
            if (sel.Length == 0) return null;
            // Sélecteurs composés (espace, >, +, ~, :pseudo, [attr]) : hors périmètre.
            foreach (char c in sel)
                if (char.IsWhiteSpace(c) || c == '>' || c == '+' || c == '~' || c == ':' || c == '[') return null;
            var r = new CssRule();
            int i = 0;
            if (i < sel.Length && sel[i] != '.' && sel[i] != '#')
            {
                int start = i;
                while (i < sel.Length && sel[i] != '.' && sel[i] != '#') i++;
                string tag = sel.Substring(start, i - start);
                if (tag != "*") { r.Tag = tag; r.Specificity += 1; }
            }
            while (i < sel.Length)
            {
                char kind = sel[i];
                i++;
                int start = i;
                while (i < sel.Length && sel[i] != '.' && sel[i] != '#') i++;
                string name = sel.Substring(start, i - start);
                if (name.Length == 0) return null;
                if (kind == '.') { r.Classes.Add(name); r.Specificity += 10; }
                else if (kind == '#') { r.Id = name; r.Specificity += 100; }
                else return null;
            }
            return r;
        }

        internal static Dictionary<string, string> ParseDeclarations(string decls)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(decls)) return result;
            foreach (string part in decls.Split(';'))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                string key = part.Substring(0, colon).Trim().ToLowerInvariant();
                string val = part.Substring(colon + 1).Trim();
                if (key.Length > 0 && val.Length > 0) result[key] = val;
            }
            return result;
        }
    }
}
