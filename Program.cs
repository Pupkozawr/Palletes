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
                TryRenderPackingImage(inPath, outPath);
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

            if (args.Length == 0)
            {
                var seed = 12345;

                Console.WriteLine("=== Запуск алгоритма упаковки ===");

                var defaultInputCsv = FindFileUpwards("1.csv");
                if (defaultInputCsv is null)
                {
                    Console.WriteLine("Предупреждение: не найден файл 1.csv для упаковки");
                    return 1;
                }

                var defaultOutputCsv = Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(defaultInputCsv)) ?? Directory.GetCurrentDirectory(),
                    "1-packed-out-container.csv");

                var packStopwatch = Stopwatch.StartNew();
                GeneticPalletPacker.PackCsv(defaultInputCsv, defaultOutputCsv, packingPallet, packingContainer, seed);
                packStopwatch.Stop();

                Console.WriteLine($"Упаковка завершена. Seed={seed}");
                Console.WriteLine($"Входной файл: {Path.GetFullPath(defaultInputCsv)}");
                Console.WriteLine($"Выходной файл: {Path.GetFullPath(defaultOutputCsv)}");
                Console.WriteLine($"Время упаковки: {packStopwatch.ElapsedMilliseconds} ms ({packStopwatch.Elapsed:hh\\:mm\\:ss\\.fff})");
                TryRenderPackingImage(defaultInputCsv, defaultOutputCsv);
                return 0;
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
                TryRenderPackingImage(inputCsv, outputCsv);
            }
            else
            {
                Console.WriteLine($"Предупреждение: не найден файл {inputCsv} для упаковки");
            }

            return 0;
        }

        private static void TryRenderPackingImage(string inputCsv, string outputCsv)
        {
            var viewerPath = FindFileUpwards(Path.Combine("scripts", "pallet_viewer.py"));
            if (viewerPath is null)
            {
                Console.WriteLine("Предупреждение: scripts\\pallet_viewer.py не найден, картинка не создана.");
                return;
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputCsv)) ?? Directory.GetCurrentDirectory();
            var imagePath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(outputCsv)}-dashboard.png");
            var pythonPath = FindPythonExecutable(viewerPath);

            Console.WriteLine();
            Console.WriteLine("=== Создание картинки упаковки ===");

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
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

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    Console.WriteLine("Предупреждение: не удалось запустить Python для создания картинки.");
                    return;
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(120_000))
                {
                    process.Kill(entireProcessTree: true);
                    Console.WriteLine("Предупреждение: создание картинки заняло слишком много времени и было остановлено.");
                    return;
                }

                _ = stdoutTask.Result;
                var stderr = stderrTask.Result;

                if (process.ExitCode != 0)
                {
                    Console.WriteLine("Предупреждение: viewer завершился с ошибкой, картинка не создана.");
                    if (!string.IsNullOrWhiteSpace(stderr))
                        Console.WriteLine(stderr.Trim());
                    return;
                }

                Console.WriteLine($"Картинка сохранена: {Path.GetFullPath(imagePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Предупреждение: не удалось создать картинку упаковки: {ex.Message}");
            }
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

        private static string? FindFileUpwards(string relativePath)
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

            return null;
        }
    }
}
