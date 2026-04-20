using System;
using System.Diagnostics;
using System.IO;

namespace Palletes
{
    public static class VerificationRunner
    {
        public static int Run(int seed, int maxOrders, bool runUnitTests)
        {
            var cwd = Directory.GetCurrentDirectory();
            var outDir = Path.Combine(cwd, "out-verify");
            Directory.CreateDirectory(outDir);

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
                MaxHeight = 0
            };

            int failures = 0;

            Console.WriteLine("== VERIFY ==");
            Console.WriteLine($"CWD: {cwd}");
            Console.WriteLine($"Seed: {seed}");
            Console.WriteLine($"MaxOrders: {maxOrders}");
            Console.WriteLine($"OutDir: {outDir}");
            Console.WriteLine();

            var sampleIn = Path.Combine(cwd, "1.csv");
            if (File.Exists(sampleIn))
            {
                var sampleOut = Path.Combine(outDir, "1-packed-out.csv");
                Console.WriteLine("-- Step 1: pack 1.csv");
                try
                {
                    var sw = Stopwatch.StartNew();
                    GeneticPalletPacker.PackCsv(sampleIn, sampleOut, packingPallet, seed);
                    sw.Stop();
                    Console.WriteLine($"OK: {sampleOut}");
                    Console.WriteLine($"Time: {sw.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"FAIL: pack 1.csv: {ex.GetType().Name}: {ex.Message}");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("-- Step 1: pack 1.csv (skipped; file not found)");
                Console.WriteLine();
            }

            Console.WriteLine("-- Step 2: runtests (generate + pack)");
            try
            {
                var genOut = Path.Combine(outDir, "generated");
                var sw = Stopwatch.StartNew();
                int rc = GeneratedPackingTestRunner.Run(genOut, seed, maxOrders, generationPallet, packingPallet);
                sw.Stop();

                if (rc == 0)
                {
                    Console.WriteLine("OK: runtests");
                }
                else
                {
                    failures++;
                    Console.WriteLine("FAIL: runtests returned non-zero");
                }

                Console.WriteLine($"Time: {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL: runtests: {ex.GetType().Name}: {ex.Message}");
            }
            Console.WriteLine();

            if (runUnitTests)
            {
                Console.WriteLine("-- Step 3: dotnet test");
                var testProj = Path.Combine(cwd, "Palletes.Tests", "Palletes.Tests.csproj");
                if (!File.Exists(testProj))
                {
                    failures++;
                    Console.WriteLine($"FAIL: test project not found: {testProj}");
                }
                else
                {
                    try
                    {
                        int rc = RunProcess(
                            fileName: "dotnet",
                            arguments: $"test \"{testProj}\" -c Release",
                            workingDirectory: cwd);

                        if (rc == 0)
                        {
                            Console.WriteLine("OK: unit tests");
                        }
                        else
                        {
                            failures++;
                            Console.WriteLine($"FAIL: unit tests (exit code {rc})");
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine($"FAIL: unit tests: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                Console.WriteLine();
            }

            Console.WriteLine(failures == 0 ? "VERIFY: OK" : $"VERIFY: FAIL ({failures} step(s) failed)");
            return failures == 0 ? 0 : 1;
        }

        private static int RunProcess(string fileName, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

            if (!p.Start())
                throw new InvalidOperationException($"Failed to start process: {fileName}");

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode;
        }
    }
}
