using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Fabrique tests\fixtures\anim.gif : 200x200, rouge puis bleu, 500 ms par frame, boucle infinie.
internal static class MakeTestGif
{
    private static void Main()
    {
        var o = new List<byte>();
        o.AddRange(Encoding.ASCII.GetBytes("GIF89a"));
        o.AddRange(U16(200)); o.AddRange(U16(200));
        o.Add(0xF1); o.Add(0); o.Add(0);
        o.AddRange(new byte[] { 255, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0 });
        o.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
        o.AddRange(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        o.AddRange(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        AddFrame(o, 200, 200, 50, 0);
        AddFrame(o, 200, 200, 50, 1);
        o.Add(0x3B);
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests", "fixtures", "anim.gif");
        File.WriteAllBytes(path, o.ToArray());
        Console.WriteLine("OK : " + path);
    }

    private static IEnumerable<byte> U16(int v)
    {
        yield return (byte)(v & 0xFF);
        yield return (byte)((v >> 8) & 0xFF);
    }

    private static void AddFrame(List<byte> o, int w, int h, int delayCs, byte colorIndex)
    {
        o.AddRange(new byte[] { 0x21, 0xF9, 0x04, 0x00 });
        o.AddRange(U16(delayCs));
        o.Add(0); o.Add(0);
        o.Add(0x2C);
        o.AddRange(U16(0)); o.AddRange(U16(0)); o.AddRange(U16(w)); o.AddRange(U16(h));
        o.Add(0);
        o.Add(2);
        var bits = new BitWriter();
        for (int i = 0; i < w * h; i++) { bits.Write(4, 3); bits.Write(colorIndex, 3); }
        bits.Write(5, 3);
        byte[] data = bits.ToArray();
        int pos = 0;
        while (pos < data.Length)
        {
            int chunk = Math.Min(255, data.Length - pos);
            o.Add((byte)chunk);
            for (int i = 0; i < chunk; i++) o.Add(data[pos + i]);
            pos += chunk;
        }
        o.Add(0);
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new List<byte>();
        private int _acc, _nbits;
        public void Write(int value, int width)
        {
            _acc |= value << _nbits;
            _nbits += width;
            while (_nbits >= 8) { _bytes.Add((byte)(_acc & 0xFF)); _acc >>= 8; _nbits -= 8; }
        }
        public byte[] ToArray()
        {
            var r = new List<byte>(_bytes);
            if (_nbits > 0) r.Add((byte)(_acc & 0xFF));
            return r.ToArray();
        }
    }
}
