﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace convert
{
    class Program
    {
        const string BitmapsOutputDir = "Bitmaps";
        const string ResourcesOutputDir = "Resources";
        const string BitmapDataFileName = "BMPDATA.BIN";
        const string DescriptorsTableFileName = "TABLE.BIN";
        const string OptimizeKey = "-o";
        const string IgnoreConstraitsKey = "-f";
        const int MaxBitmapSize = 3500000;
        const int NumFilesRequired = 603;
        enum Role { Encoder, Decoder, Invalid };
        static Role GetRole(string dir)
        {
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f).ToUpperInvariant())
                .ToArray();
            if (files.Length >= 2 && files.Contains(BitmapDataFileName) && files.Contains(DescriptorsTableFileName))
                return Role.Decoder;
            else if (files.All(f => f.EndsWith(".BMP")))
                return Role.Encoder;
            return Role.Invalid;
        }
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                var inputDirectory = args[0];
                var ignoreConstraits = args.Any(a => a.ToLowerInvariant() == IgnoreConstraitsKey);
                var optimizeEncoding = args.Any(a => a.ToLowerInvariant() == OptimizeKey);
                if (Directory.Exists(inputDirectory))
                {
                    var role = GetRole(inputDirectory);
                    if (role == Role.Decoder)
                    {
                        Console.WriteLine("Decoding files...");
                        DecodeBitmaps(inputDirectory, ignoreConstraits);
                    }
                    else if (role == Role.Encoder)
                    {
                        Console.WriteLine("Encoding files...");
                        EncodeBitmaps(inputDirectory, optimizeEncoding, ignoreConstraits);
                    }
                    else
                    {
                        Console.WriteLine("ERROR! I don't know what to do with directory \"{0}\".{1}Make sure there are some bitmaps or binary files extracted from screen.",
                            inputDirectory, Environment.NewLine);
                    }
                }
                else Console.WriteLine("Specified directory is missing!");
            }
            else  Console.WriteLine("Missing arguments!");
            Console.Write("Press any key to exit...");
            Console.ReadKey(true);
        }

        private static bool EncoderInputIsOkay(IEnumerable<ushort> ids)
        {
            if (ids.Distinct().Count() < NumFilesRequired)
            {
                Console.WriteLine("ERROR! Expected more than {0} files instead of {1} given.", NumFilesRequired - 1, ids.Distinct().Count());
                return false;
            }
            if (ids.First() != 0 || ids.Where((val, idx) => val == idx).Count() != ids.Count())
            {
                Console.WriteLine("ERROR! Identifiers must be consecutive, but the index {0} is missing!",
                    ids.Where((val, idx) => val != idx).First() - 1);
                return false;
            }
            return true;
        }

        private static void EncodeBitmaps(string inputDirectory, bool optimize, bool ignoreConstraits)
        {
            var reg = new Regex("(\\d+)_.*");
            var files = Directory.EnumerateFiles(inputDirectory, "*_*.bmp", SearchOption.TopDirectoryOnly)
                .Where(f => reg.IsMatch(f))
                .Select(f => new { Path = f, Identifier = ushort.Parse(reg.Match(Path.GetFileName(f)).Groups[1].Value, CultureInfo.InvariantCulture) })
                .OrderBy(f => f.Identifier) // order by identifier, as th screens checks if identifier match an index
                .ToArray();
            if (!ignoreConstraits && !EncoderInputIsOkay(files.Select(f => f.Identifier)))
                return;
            var result = true;
            var outputDir = Directory.CreateDirectory(ResourcesOutputDir).FullName;
            uint curOffset = 0;
            using (FileStream fsTable = new FileStream(Path.Combine(outputDir, DescriptorsTableFileName), FileMode.Create, FileAccess.Write))
            using (FileStream fsImage = new FileStream(Path.Combine(outputDir, BitmapDataFileName), FileMode.Create, FileAccess.Write))
            using (BinaryWriter bwTable = new BinaryWriter(fsTable))
            using (BinaryWriter bwImage = new BinaryWriter(fsImage))
            {
                if (optimize)
                {
                    var numBytesSaved = 0;
                    try
                    {
                        var images = files.Select(file =>
                        {
                            var img = new ImageData(file.Path);
                            var data = img.CompressImage();
                            return new { Id = file.Identifier, Img = img, Data = data };
                        }).ToList();
                        var table = new TableEntry[images.Count];
                        for (int i = 0, j = 0; i < images.Count; i++)
                        {
                            var found = false;
                            var id = images[i].Id;
                            var img = images[i].Img;
                            var data = images[i].Data;
                            for (j = 0; j < i; j++)
                            {
                                var oimg = images[j].Img;
                                var odata = images[j].Data;
                                if (oimg.Width == img.Width && oimg.Height == img.Height && odata.SequenceEqual(data))
                                {
                                    found = true;
                                    break;
                                }
                            }
                            if (i == 0 || !found) // first or not found
                            {
                                var entry = new TableEntry(id, img.Width, img.Height, curOffset);
                                table[i] = entry;
                                entry.Write(bwTable);
                                for (int k = 0; k < data.Length; k++)
                                {
                                    bwImage.Write(data[k]);
                                }
                                curOffset += (uint)(data.Length * 4);
                            }
                            else
                            {
                                var entry = new TableEntry(id, img.Width, img.Height, table[j].Offset);
                                table[i] = entry;
                                entry.Write(bwTable);
                                numBytesSaved += data.Length * 4;
                            }
                        }
                        Console.WriteLine($"Saved {numBytesSaved} bytes!");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR! Failed to compress!{Environment.NewLine}{ex}");
                        result = false;
                    }
                }
                else
                {
                    foreach (var file in files)
                    {
                        try
                        {
                            var img = new ImageData(file.Path);
                            var data = img.CompressImage();
                            new TableEntry(file.Identifier, img.Width, img.Height, curOffset).Write(bwTable);
                            for (int i = 0; i < data.Length; i++)
                            {
                                bwImage.Write(data[i]);
                            }
                            curOffset += (uint)(data.Length * 4);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"ERROR! Failed to compress a file {file.Path}!{Environment.NewLine}{ex}");
                            result = false;
                            break;
                        }
                    }
                }   
            }
            if (!ignoreConstraits && curOffset >= MaxBitmapSize)
            {
                Console.WriteLine($"ERROR! Output file is too big {curOffset}, max {MaxBitmapSize} bytes!");
                File.Delete(Path.Combine(outputDir, DescriptorsTableFileName));
                File.Delete(Path.Combine(outputDir, BitmapDataFileName));
            }
            Console.WriteLine($"Encoding {(result ? "finished" : "failed")}");
        }

        private static void DecodeBitmaps(string inputDirectory, bool ignoreConstraits)
        {
            var table = TableEntry.LoadFromFile(Path.Combine(inputDirectory, "TABLE.BIN"));
            var imgData = new ImageData(Path.Combine(inputDirectory, "BMPDATA.BIN"));
            var dir = Directory.CreateDirectory(BitmapsOutputDir).FullName;
            var result = true;
            Parallel.ForEach(table, (entry) =>
            {
                try
                {
                    imgData.LoadBitmap(entry).Save(Path.Combine(dir, $"{entry.Id}_{entry.Width}x{entry.Height}.bmp"), ImageFormat.Bmp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR! Failed to convert a file with id {entry.Id}!{Environment.NewLine}{ex}");
                    result = false;
                }
            });
            Console.WriteLine($"Decoding {(result ? "finished" : "failed")}");
        }
    }
}
