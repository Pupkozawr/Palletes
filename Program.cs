using System;
using System.Diagnostics;
using System.IO;
using Palletes.Core;
using Palletes.Generation;
using Palletes.Models;
using Palletes.Testing;

namespace Palletes
{
    internal class Program
    {
        public static int Main(string[] args)
        {
            var generationPallet = new PalletSpec
            {
                PalletType = "EUR",
                Length = 1200,
                Width = 800,
                MaxHeight = 2000
            };

            var packingPallet = new PalletSpec
            {
                PalletType = generationPallet.PalletType,
                Length = generationPallet.Length,
                Width = generationPallet.Width,
                MaxHeight = generationPallet.MaxHeight
            };

            var packingContainer = new ContainerSpec
            {
                ContainerType = "UK-3",
                Length = 1930,
                Width = 1225,
                Height = 2128
            };

            if (args.Length >= 1 && string.Equals(args[0], "pack", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: pack <in.csv> <out.csv> [seed]");
                    return 2;
                }

                var inPath = args[1];
                var outPath = args[2];
                var seed = args.Length >= 4 && int.TryParse(args[3], out var s) ? s : 12345;

                var sw = Stopwatch.StartNew();
                GeneticPalletPacker.PackCsv(inPath, outPath, packingPallet, packingContainer, seed);
                sw.Stop();

                Console.WriteLine($"Packed. Seed={seed}. Output: {Path.GetFullPath(outPath)}");
                Console.WriteLine($"Packing time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed:hh\\:mm\\:ss\\.fff})");
                return 0;
            }

            if (args.Length >= 1 && string.Equals(args[0], "verify", StringComparison.OrdinalIgnoreCase))
            {
                var seed = args.Length >= 2 && int.TryParse(args[1], out var vs) ? vs : 12345;
                var maxOrders = args.Length >= 3 && int.TryParse(args[2], out var vm) ? vm : 50;
                bool runUnitTests = true;
                for (int i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--skip-tests", StringComparison.OrdinalIgnoreCase))
                        runUnitTests = false;
                }

                return VerificationRunner.Run(seed, maxOrders, runUnitTests);
            }

            if (args.Length >= 1 && string.Equals(args[0], "runtests", StringComparison.OrdinalIgnoreCase))
            {
                var outDir = args.Length >= 2 ? args[1] : "out-tests";
                var seed = args.Length >= 3 && int.TryParse(args[2], out var s2) ? s2 : 12345;
                var maxOrders = args.Length >= 4 && int.TryParse(args[3], out var m) ? m : int.MaxValue;

                return GeneratedPackingTestRunner.Run(outDir, seed, maxOrders, generationPallet, packingPallet, packingContainer);
            }

            var defaultOutDir = args.Length >= 1 ? args[0] : "out";
            var genSeed = args.Length >= 2 && int.TryParse(args[1], out var s3) ? s3 : 12345;

            Directory.CreateDirectory(defaultOutDir);

            var defaultRng = new Rng(genSeed);
            var defaultProfile = Profile.DefaultRetailLike();

            var defaultGen = new DatasetGenerator(defaultProfile, generationPallet, defaultRng);
            var totalStopwatch = Stopwatch.StartNew();

            Console.WriteLine("=== Генерация тестовых данных ===");
            defaultGen.GenerateAll(defaultOutDir);

            totalStopwatch.Stop();

            Console.WriteLine($"Генерация завершена. Seed={genSeed}. Output: {Path.GetFullPath(defaultOutDir)}");
            Console.WriteLine($"Время генерации: {totalStopwatch.ElapsedMilliseconds} ms ({totalStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");
            Console.WriteLine();

            Console.WriteLine("=== Запуск алгоритма упаковки ===");
            var firstOrderDir = Path.Combine(defaultOutDir, "group1", "1");
            var inputCsv = Path.Combine(firstOrderDir, "1.csv");
            var outputCsv = Path.Combine(defaultOutDir, "1-packed-out-container.csv");

            if (File.Exists(inputCsv))
            {
                var packStopwatch = Stopwatch.StartNew();
                GeneticPalletPacker.PackCsv(inputCsv, outputCsv, packingPallet, packingContainer, genSeed);
                packStopwatch.Stop();

                Console.WriteLine($"Упаковка завершена. Seed={genSeed}");
                Console.WriteLine($"Входной файл: {Path.GetFullPath(inputCsv)}");
                Console.WriteLine($"Выходной файл: {Path.GetFullPath(outputCsv)}");
                Console.WriteLine($"Время упаковки: {packStopwatch.ElapsedMilliseconds} ms ({packStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");
            }
            else
            {
                Console.WriteLine($"Предупреждение: не найден файл {inputCsv} для упаковки");
            }

            return 0;
        }
    }
}
