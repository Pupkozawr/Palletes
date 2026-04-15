using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Palletes
{
    public static class GeneratedPackingTestRunner
    {
        public static int Run(string outDir, int seed, int maxOrders, PalletSpec generationPallet, PalletSpec packingPallet)
        {
            Directory.CreateDirectory(outDir);

            var rng = new Rng(seed);
            var profile = Profile.DefaultRetailLike();
            var gen = new DatasetGenerator(profile, generationPallet, rng);

            var generationSw = Stopwatch.StartNew();
            gen.GenerateAll(outDir);
            generationSw.Stop();

            var orderCsvs = Directory.EnumerateFiles(outDir, "*.csv", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("-out.csv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, maxOrders))
                .ToList();

            int ok = 0;
            int fail = 0;
            long sumHeight = 0;
            long maxHeightSeen = 0;
            long sumEmptyVolume = 0;

            var packingSw = Stopwatch.StartNew();

            foreach (var inPath in orderCsvs)
            {
                var orderName = Path.GetFileNameWithoutExtension(inPath);
                var outPath = Path.Combine(Path.GetDirectoryName(inPath)!, orderName + "-out.csv");

                try
                {
                    var oneSw = Stopwatch.StartNew();
                    GeneticPalletPacker.PackCsv(inPath, outPath, packingPallet, seed);
                    oneSw.Stop();

                    var vr = PackingCsvValidator.ReadPackingCsv(outPath);
                    long h = vr.Boxes.Count == 0 ? 0 : (long)Math.Round(vr.Boxes.Max(b => b.Z));

                    long vol = 0;
                    foreach (var b in vr.Boxes)
                    {
                        vol += (long)Math.Round(b.Dx) * (long)Math.Round(b.Dy) * (long)Math.Round(b.Dz);
                    }

                    long baseArea = (long)packingPallet.Length * packingPallet.Width;
                    long empty = baseArea * h - vol;

                    ok++;
                    sumHeight += h;
                    if (h > maxHeightSeen) maxHeightSeen = h;
                    sumEmptyVolume += Math.Max(0, empty);

                    Console.WriteLine($"OK  {orderName}: boxes={vr.Boxes.Count} height={h} empty={Math.Max(0, empty)} time={oneSw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine($"FAIL {orderName}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            packingSw.Stop();

            int total = ok + fail;
            Console.WriteLine();
            Console.WriteLine($"Generation: {generationSw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Packing: {packingSw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Orders: {total} (ok={ok}, fail={fail})");
            if (ok > 0)
            {
                Console.WriteLine($"Avg height: {sumHeight / ok} mm; Max height: {maxHeightSeen} mm");
                Console.WriteLine($"Avg empty volume: {sumEmptyVolume / ok} mm^3");
            }

            return fail == 0 ? 0 : 1;
        }
    }
}
