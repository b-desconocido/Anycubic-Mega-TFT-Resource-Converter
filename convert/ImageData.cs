using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace convert
{
    class ImageData
    {
        const uint RepetitionMask = 0x3FFFU;
        const uint RunLengthMax = RepetitionMask;

        private readonly object _consoleLock = new object();
        public string Path { get; private set; }
        public ushort Width { get; private set; }
        public ushort Height { get; private set; }

        public ImageData(string path)
        {
            Path = path;
        }
        private static uint ReverseBytes(uint value)
        {
            return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 | (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
        }

        private static uint EncodePixel(short color, int num)
        {
            return ReverseBytes((uint)((num & RepetitionMask) | ((uint)color << 16 | ((uint)color << 28 >> 12) & ~RepetitionMask)));
        }

        private short[] ReadRGB565Pixels(Bitmap bmp)
        {
            var result = new short[bmp.Width * bmp.Height];
            var bits = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, bmp.PixelFormat);
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), result, y * bmp.Width, bmp.Width);
            }
            bmp.UnlockBits(bits);
            return result;
        }

        private uint[] Compress(short[] pixels)
        {
            var result = new uint[pixels.Length];
            int reps = 1, dest = 0, src = 1;
            while (src < pixels.Length)
            {
                if (pixels[src] != pixels[src - 1] || reps == RunLengthMax)
                {
                    result[dest++] = EncodePixel(pixels[src - 1], reps);
                    reps = 0;
                }
                reps++;
                src++;
            }
            result[dest++] = EncodePixel(pixels[pixels.Length - 1], reps);
            Array.Resize(ref result, dest);
            return result;
        }

        public uint[] CompressImage()
        {
            using (var newBmp = new Bitmap(Path))
            using (var bmp = newBmp.Clone(new Rectangle(0, 0, newBmp.Width, newBmp.Height), PixelFormat.Format16bppRgb565))
            {
                if (bmp.Width >= ushort.MaxValue || bmp.Height >= ushort.MaxValue)
                    throw new InvalidOperationException("Bitmap is too large!");
                Width = (ushort)bmp.Width;
                Height = (ushort)bmp.Height;
                var pixels = ReadRGB565Pixels(bmp);
                return Compress(pixels);
            }
        }

        public Bitmap LoadBitmap(TableEntry entry)
        {
            var result = new Bitmap(entry.Width, entry.Height, PixelFormat.Format16bppRgb565);
            Width = (ushort)result.Width;
            Height = (ushort)result.Height;
            int size = result.Width * result.Height, idx = 0;
            var pixels = new short[size];
            using (FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                fs.Position = entry.Offset;
                while (idx < size)
                {
                    var data = ReverseBytes(br.ReadUInt32());
                    var reps = data & RepetitionMask;
                    for (uint i = 0; i < reps; i++, idx++)
                    {
                        if (idx >= size)
                        {
                            lock (_consoleLock)
                                Console.WriteLine($"WARNING! {entry.Id} image with expected size {size} overwrites itself by {reps - i} pixels!");
                            break;
                        }
                        pixels[idx] = (short)((data >> 16) | (data << 12 >> 28));
                    }
                }
            }
            var bmp = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, result.PixelFormat);
            for (int y = 0; y < result.Height; y++)
            {
                Marshal.Copy(pixels, y * result.Width, IntPtr.Add(bmp.Scan0, y * bmp.Stride), result.Width);
            }
            result.UnlockBits(bmp);
            return result;
        }
    }
}
