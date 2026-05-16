using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

            if (args.Length == 0 || string.Equals(args[0], "demo", StringComparison.OrdinalIgnoreCase))
            {
                var demoOutDir = args.Length >= 2 ? args[1] : "out-demo";
                var demoSeed = args.Length >= 3 && int.TryParse(args[2], out var ds) ? ds : 12345;
                return RunDemo(demoOutDir, demoSeed, packingPallet, packingContainer);
            }

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
                PalletPacker.PackCsv(inPath, outPath, packingPallet, packingContainer, seed);
                sw.Stop();

                Console.WriteLine($"Packed. Seed={seed}. Output: {Path.GetFullPath(outPath)}");
                Console.WriteLine($"Packing time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed:hh\\:mm\\:ss\\.fff})");
                RenderPackingImage(inPath, outPath);
                return 0;
            }

            if (args.Length >= 1 && string.Equals(args[0], "pack-greedy", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: pack-greedy <in.csv> <out.csv> [seed] [random-starts]");
                    return 2;
                }

                var inPath = args[1];
                var outPath = args[2];
                var seed = args.Length >= 4 && int.TryParse(args[3], out var s) ? s : 12345;
                var randomStarts = args.Length >= 5 && int.TryParse(args[4], out var rs) ? rs : 40;
                var effectiveRandomStarts = Math.Clamp(randomStarts, 0, 500);

                var sw = Stopwatch.StartNew();
                PalletPacker.PackCsvMultiStartGreedy(inPath, outPath, packingPallet, packingContainer, seed, effectiveRandomStarts);
                sw.Stop();

                Console.WriteLine($"Packed with multi-start greedy. Seed={seed}. RandomStarts={effectiveRandomStarts}. Output: {Path.GetFullPath(outPath)}");
                Console.WriteLine($"Packing time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed:hh\\:mm\\:ss\\.fff})");
                RenderPackingImage(inPath, outPath);
                return 0;
            }

            if (args.Length >= 1 && string.Equals(args[0], "pack-greedy-bestfit", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: pack-greedy-bestfit <in.csv> <out.csv> [seed] [random-starts]");
                    return 2;
                }

                var inPath = args[1];
                var outPath = args[2];
                var seed = args.Length >= 4 && int.TryParse(args[3], out var s) ? s : 12345;
                var randomStarts = args.Length >= 5 && int.TryParse(args[4], out var rs) ? rs : 40;
                var effectiveRandomStarts = Math.Clamp(randomStarts, 0, 500);

                var sw = Stopwatch.StartNew();
                PalletPacker.PackCsvMultiStartGreedyBestFit(inPath, outPath, packingPallet, packingContainer, seed, effectiveRandomStarts);
                sw.Stop();

                Console.WriteLine($"Packed with multi-start greedy best-fit-lite. Seed={seed}. RandomStarts={effectiveRandomStarts}. Output: {Path.GetFullPath(outPath)}");
                Console.WriteLine($"Packing time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed:hh\\:mm\\:ss\\.fff})");
                RenderPackingImage(inPath, outPath);
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

            if (args.Length >= 1 && string.Equals(args[0], "tune", StringComparison.OrdinalIgnoreCase))
            {
                var outDir = args.Length >= 2 ? args[1] : "out-tune";
                var seed = args.Length >= 3 && int.TryParse(args[2], out var ts) ? ts : 12345;
                var maxOrders = args.Length >= 4 && int.TryParse(args[3], out var tm) ? tm : 6;
                var seedRuns = args.Length >= 5 && int.TryParse(args[4], out var tr) ? tr : 1;

                return FitnessWeightExperimentRunner.Run(outDir, seed, maxOrders, seedRuns, generationPallet, packingPallet, packingContainer);
            }

            if (args.Length >= 1 && string.Equals(args[0], "compare-orient", StringComparison.OrdinalIgnoreCase))
            {
                var outDir = args.Length >= 2 ? args[1] : "out-orient-compare";
                var seed = args.Length >= 3 && int.TryParse(args[2], out var os) ? os : 12345;
                var maxOrders = args.Length >= 4 && int.TryParse(args[3], out var om) ? om : 6;
                var seedRuns = args.Length >= 5 && int.TryParse(args[4], out var oruns) ? oruns : 1;

                return OrientationFallbackExperimentRunner.Run(outDir, seed, maxOrders, seedRuns, generationPallet, packingPallet, packingContainer);
            }

            if (args.Length >= 1 && string.Equals(args[0], "compare-search", StringComparison.OrdinalIgnoreCase))
            {
                var outDir = args.Length >= 2 ? args[1] : "out-search-compare";
                var seed = args.Length >= 3 && int.TryParse(args[2], out var cs) ? cs : 12345;
                var maxOrders = args.Length >= 4 && int.TryParse(args[3], out var cm) ? cm : 8;
                var seedRuns = args.Length >= 5 && int.TryParse(args[4], out var cr) ? cr : 1;
                var greedyRandomStarts = args.Length >= 6 && int.TryParse(args[5], out var cg) ? cg : 40;
                var maxBoxes = args.Length >= 7 && int.TryParse(args[6], out var cb) ? cb : 40;

                return SearchAlgorithmComparisonRunner.Run(
                    outDir,
                    seed,
                    maxOrders,
                    seedRuns,
                    greedyRandomStarts,
                    maxBoxes,
                    generationPallet,
                    packingPallet,
                    packingContainer);
            }


            var defaultOutDir = args.Length >= 1 ? args[0] : "out";
            var genSeed = args.Length >= 2 && int.TryParse(args[1], out var s3) ? s3 : 12345;

            Directory.CreateDirectory(defaultOutDir);

            var defaultRng = new Rng(genSeed);
            var defaultProfile = Profile.DefaultRetailLike();

            var defaultGen = new DatasetGenerator(defaultProfile, generationPallet, defaultRng);
            var totalStopwatch = Stopwatch.StartNew();

            Console.WriteLine("=== Р“РµРЅРµСЂР°С†РёСЏ С‚РµСЃС‚РѕРІС‹С… РґР°РЅРЅС‹С… ===");
            defaultGen.GenerateAll(defaultOutDir);

            totalStopwatch.Stop();

            Console.WriteLine($"Р“РµРЅРµСЂР°С†РёСЏ Р·Р°РІРµСЂС€РµРЅР°. Seed={genSeed}. Output: {Path.GetFullPath(defaultOutDir)}");
            Console.WriteLine($"Р’СЂРµРјСЏ РіРµРЅРµСЂР°С†РёРё: {totalStopwatch.ElapsedMilliseconds} ms ({totalStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");
            Console.WriteLine();

            Console.WriteLine("=== Р—Р°РїСѓСЃРє Р°Р»РіРѕСЂРёС‚РјР° СѓРїР°РєРѕРІРєРё ===");
            var firstOrderDir = Path.Combine(defaultOutDir, "group1", "1");
            var inputCsv = Path.Combine(firstOrderDir, "1.csv");
            var outputCsv = Path.Combine(defaultOutDir, "1-packed-out-container.csv");

            var packStopwatch = Stopwatch.StartNew();
            PalletPacker.PackCsv(inputCsv, outputCsv, packingPallet, packingContainer, genSeed);
            packStopwatch.Stop();

            Console.WriteLine($"РЈРїР°РєРѕРІРєР° Р·Р°РІРµСЂС€РµРЅР°. Seed={genSeed}");
            Console.WriteLine($"Р’С…РѕРґРЅРѕР№ С„Р°Р№Р»: {Path.GetFullPath(inputCsv)}");
            Console.WriteLine($"Р’С‹С…РѕРґРЅРѕР№ С„Р°Р№Р»: {Path.GetFullPath(outputCsv)}");
            Console.WriteLine($"Р’СЂРµРјСЏ СѓРїР°РєРѕРІРєРё: {packStopwatch.ElapsedMilliseconds} ms ({packStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");
            RenderPackingImage(inputCsv, outputCsv);

            return 0;
        }

        private static int RunDemo(string outDir, int seed, PalletSpec packingPallet, ContainerSpec packingContainer)
        {
            Directory.CreateDirectory(outDir);

            var inputCsv = Path.Combine(outDir, "demo-order.csv");
            var outputCsv = Path.Combine(outDir, "demo-packed.csv");
            WriteDemoOrderCsv(inputCsv);

            Console.WriteLine("=== Pallet packing demo ===");
            Console.WriteLine("Algorithm: multi-start greedy best-fit-lite");
            Console.WriteLine($"Seed: {seed}");
            Console.WriteLine($"Input:  {Path.GetFullPath(inputCsv)}");

            var packStopwatch = Stopwatch.StartNew();
            PalletPacker.PackCsv(inputCsv, outputCsv, packingPallet, packingContainer, seed);
            packStopwatch.Stop();

            var layout = PackingCsvValidator.ReadPackingCsv(outputCsv);
            int palletCount = Math.Max(1, layout.Pallets.Count);
            int containerCount = Math.Max(1, layout.Containers.Count);
            double maxHeight = layout.Boxes.Count == 0 ? 0.0 : layout.Boxes.Max(b => b.Z);

            Console.WriteLine();
            Console.WriteLine("Packing completed.");
            Console.WriteLine($"Output: {Path.GetFullPath(outputCsv)}");
            Console.WriteLine($"Boxes: {layout.Boxes.Count}; pallets: {palletCount}; containers: {containerCount}; max height: {maxHeight:F0} mm");
            Console.WriteLine($"Packing time: {packStopwatch.ElapsedMilliseconds} ms ({packStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");

            var imagePath = RenderPackingImage(inputCsv, outputCsv);

            Console.WriteLine();
            Console.WriteLine("Demo files are ready:");
            Console.WriteLine($"  CSV layout: {Path.GetFullPath(outputCsv)}");
            Console.WriteLine($"  PNG image:  {Path.GetFullPath(imagePath)}");

            return 0;
        }

        private static void WriteDemoOrderCsv(string path)
        {
            var lines = new[]
            {
                "1",
                "SKU,Quantity,Length,Width,Height,Weight,Strength,Caustic",
                "1001,8,400,300,220,4500,5,0",
                "1002,6,300,200,180,1800,4,0",
                "1003,4,600,400,250,6500,5,0",
                "1004,4,250,250,300,2100,3,0",
                "1005,3,500,300,160,2800,4,0",
                "1006,3,200,200,420,2400,2,0"
            };

            File.WriteAllLines(path, lines);
        }

        private static string RenderPackingImage(string inputCsv, string outputCsv)
        {
            var viewerPath = FindFileUpwards(Path.Combine("scripts", "pallet_viewer.py"));
            var pythonPath = FindPythonExecutable(viewerPath);
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputCsv)) ?? Directory.GetCurrentDirectory();
            var imagePath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(outputCsv)}-dashboard.png");

            Console.WriteLine();
            Console.WriteLine("=== Создание картинки упаковки ===");

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(viewerPath);
            startInfo.ArgumentList.Add(outputCsv);
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(inputCsv);
            startInfo.ArgumentList.Add("--view");
            startInfo.ArgumentList.Add("dashboard");
            startInfo.ArgumentList.Add("--save");
            startInfo.ArgumentList.Add(imagePath);
            startInfo.ArgumentList.Add("--color-by");
            startInfo.ArgumentList.Add("status");

            using var process = Process.Start(startInfo)!;
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Не удалось создать картинку упаковки: " + stderr.Trim());
            }

            Console.WriteLine($"Картинка сохранена: {Path.GetFullPath(imagePath)}");
            return imagePath;
        }

        private static string FindPythonExecutable(string viewerPath)
        {
            var scriptsDirectory = Path.GetDirectoryName(Path.GetFullPath(viewerPath));
            var rootDirectory = scriptsDirectory is null ? null : Directory.GetParent(scriptsDirectory)?.FullName;
            var localPython = rootDirectory is null ? null : Path.Combine(rootDirectory, ".venv", "Scripts", "python.exe");
            if (localPython is not null && File.Exists(localPython))
                return localPython;

            return "python";
        }

        private static string FindFileUpwards(string relativePath)
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory is not null)
                {
                    var candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                        return candidate;

                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException($"Не найден файл {relativePath}.");
        }
    }
}
