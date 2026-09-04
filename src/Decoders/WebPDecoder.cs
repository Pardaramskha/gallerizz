using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallerizz
{
    // Replis WebP quand le codec WIC de Windows est absent :
    //   1. libwebp.dll posée à côté de l'exe (si l'utilisateur en fournit une) — décodage BGRA direct ;
    //   2. dwebp.exe, le décodeur officiel Google livré avec Gallerizz — conversion silencieuse en PNG.
    internal static class WebPDecoder
    {
        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int WebPGetInfo(byte[] data, UIntPtr dataSize, out int width, out int height);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr WebPDecodeBGRAInto(byte[] data, UIntPtr dataSize, IntPtr output, UIntPtr outputSize, int stride);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr WebPEncodeBGRA(byte[] bgra, int width, int height, int stride, float quality, out IntPtr output);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WebPFree(IntPtr ptr);

        internal static bool IsAvailable()
        {
            try
            {
                int w, h;
                WebPGetInfo(new byte[4], (UIntPtr)4, out w, out h);
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch { return true; }
        }

        internal static BitmapSource Decode(byte[] bytes)
        {
            BitmapSource viaDll = DecodeViaDll(bytes);
            if (viaDll != null) return viaDll;
            return DecodeViaDwebp(bytes);
        }

        private static BitmapSource DecodeViaDll(byte[] bytes)
        {
            try
            {
                int w, h;
                if (WebPGetInfo(bytes, (UIntPtr)bytes.Length, out w, out h) == 0) return null;
                if (w <= 0 || h <= 0) return null;
                int stride = w * 4;
                int size = stride * h;
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    IntPtr ok = WebPDecodeBGRAInto(bytes, (UIntPtr)bytes.Length, buffer, (UIntPtr)size, stride);
                    if (ok == IntPtr.Zero) return null;
                    var pixels = new byte[size];
                    Marshal.Copy(buffer, pixels, 0, size);
                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                    bmp.Freeze();
                    return bmp;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }

        private static BitmapSource DecodeViaDwebp(byte[] bytes)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dwebp.exe");
            if (!File.Exists(exe)) return null;
            string input = Path.Combine(Path.GetTempPath(), "gallerizz-" + Guid.NewGuid().ToString("N") + ".webp");
            string output = Path.ChangeExtension(input, ".png");
            try
            {
                File.WriteAllBytes(input, bytes);
                var psi = new ProcessStartInfo(exe, "\"" + input + "\" -o \"" + output + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process proc = Process.Start(psi))
                {
                    if (!proc.WaitForExit(15000)) { try { proc.Kill(); } catch { } return null; }
                    if (proc.ExitCode != 0) return null;
                }
                if (!File.Exists(output)) return null;
                var dec = BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(output)),
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource frame = dec.Frames[0];
                frame.Freeze();
                return frame;
            }
            catch { return null; }
            finally
            {
                try { if (File.Exists(input)) File.Delete(input); } catch { }
                try { if (File.Exists(output)) File.Delete(output); } catch { }
            }
        }

        // Encodeur, utilisé uniquement par les sondes (fabriquer un .webp témoin).
        internal static byte[] Encode(byte[] bgra, int width, int height, float quality)
        {
            IntPtr output;
            UIntPtr size = WebPEncodeBGRA(bgra, width, height, width * 4, quality, out output);
            if (size == UIntPtr.Zero || output == IntPtr.Zero) return null;
            try
            {
                var result = new byte[(int)(uint)size];
                Marshal.Copy(output, result, 0, result.Length);
                return result;
            }
            finally
            {
                WebPFree(output);
            }
        }
    }
}
