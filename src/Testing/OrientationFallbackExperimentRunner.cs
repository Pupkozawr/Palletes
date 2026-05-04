using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Palletes.Core;
using Palletes.Generation;
using Palletes.Models;

namespace Palletes.Testing
{
    public static class OrientationFallbackExperimentRunner
    {
        private sealed record Candidate(string Name, GeneticPalletPacker.OrientationFallbackMode Mode);

        private sealed class RunMetric
        {
            public string Candidate { get; init; } = "";
            public string OrderName { get; init; } = "";
            public int Seed { get; init; }
            public bool Ok { get; init; }
            public int Boxes { get; init; }
            public int Pallets { get; init; }
            public int Containers { get; init; }
            public double MaxHeight { get; init; }
            public double EmptyVolume { get; init; }
            public double FillByUsedHeight { get; init; }
            public double AvgSupport { get; init; }
            public int WeakSupportBoxes { get; init; }
            public long TimeMs { get; init; }
            public string Error { get; init; } = "";
        }

        private sealed class ComparisonMetric
        {
            public string OrderName { get; init; } = "";
            public int Seed { get; init; }
            public bool BothOk { get; init; }
            public int GeneOnlyPallets { get; init; }
            public int FallbackPallets { get; init; }
            public int DeltaPallets { get; init; }
            public double GeneOnlyHeight { get; init; }
            public double FallbackHeight { get; init; }
            public double DeltaHeight { get; init; }
            public double GeneOnlyEmptyVolume { get; init; }
            public double FallbackEmptyVolume { get; init; }
            public double DeltaEmptyVolume { get; init; }
            public double GeneOnlyFill { get; init; }
            public double FallbackFill { get; init; }
            public double DeltaFill { get; init; }
            public long GeneOnlyTimeMs { get; init; }
            public long FallbackTimeMs { get; init; }
            public long DeltaTimeMs { get; init; }
        }

        private sealed class AggregateMetric
        {
            public string Candidate { get; init; } = "";
            public int Runs { get; init; }
            public int Ok { get; init; }
            public int Fail { get; init; }
            public double AvgPallets { get; init; }
            public double AvgContainers { get; init; }
            public double AvgMaxHeight { get; init; }
            public double AvgEmptyVolume { get; init; }
            public double AvgFillByUsedHeight { get; init; }
            public double AvgSupport { get; init; }
            public double AvgWeakSupportBoxes { get; init; }
            public double AvgTimeMs { get; init; }
        }

        public static int Run(
            string outDir,
            int seed,
            int maxOrders,
            int seedRuns,
            PalletSpec generationPallet,
            PalletSpec packingPallet,
            ContainerSpec packingContainer)
        {
            Directory.CreateDirectory(outDir);

            var dataDir = Path.Combine(outDir, "data");
            var resultsDir = Path.Combine(outDir, "results");
            Directory.CreateDirectory(resultsDir);

            Console.WriteLine("== ORIENTATION FALLBACK EXPERIMENT ==");
            Console.WriteLine($"OutDir: {Path.GetFullPath(outDir)}");
            Console.WriteLine($"Seed: {seed}");
            Console.WriteLine($"MaxOrders: {maxOrders}");
            Console.WriteLine($"SeedRuns: {seedRuns}");
            Console.WriteLine();

            if (!Directory.Exists(dataDir) || !Directory.EnumerateFiles(dataDir, "*.csv", SearchOption.AllDirectories).Any())
            {
                var rng = new Rng(seed);
                var generator = new DatasetGenerator(Profile.DefaultRetailLike(), generationPallet, rng);
                generator.GenerateAll(dataDir);
            }

            var orders = Directory.EnumerateFiles(dataDir, "*.csv", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("-out.csv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(OrderIdFromPath)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxOrders))
                .ToList();

            var candidates = new List<Candidate>
            {
                new("gene-only", GeneticPalletPacker.OrientationFallbackMode.GeneOnly),
                new("fallback", GeneticPalletPacker.OrientationFallbackMode.Fallback)
            };

            var runs = new List<RunMetric>();

            foreach (var candidate in candidates)
            {
                Console.WriteLine($"-- {candidate.Name} --");
                var candidateDir = Path.Combine(resultsDir, SafeName(candidate.Name));
                Directory.CreateDirectory(candidateDir);

                foreach (var orderPath in orders)
                {
                    string orderName = Path.GetFileNameWithoutExtension(orderPath);
                    for (int s = 0; s < Math.Max(1, seedRuns); s++)
                    {
                        int runSeed = seed + s * 7919;
                        string outPath = Path.Combine(candidateDir, $"{orderName}-seed{runSeed}-out.csv");
                        var sw = Stopwatch.StartNew();

                        try
                        {
                            GeneticPalletPacker.PackCsvForOrientationExperiment(
                                orderPath,
                                outPath,
                                packingPallet,
                                packingContainer,
                                runSeed,
                                candidate.Mode);
                            sw.Stop();

                            var metric = Measure(candidate.Name, orderName, runSeed, outPath, sw.ElapsedMilliseconds);
                            runs.Add(metric);
                            Console.WriteLine(
                                $"OK   {orderName} seed={runSeed}: " +
                                $"boxes={metric.Boxes} pallets={metric.Pallets} containers={metric.Containers} " +
                                $"height={metric.MaxHeight:F0} fill={metric.FillByUsedHeight:P1} time={metric.TimeMs}ms");
                        }
                        catch (Exception ex)
                        {
                            sw.Stop();
                            runs.Add(new RunMetric
                            {
                                Candidate = candidate.Name,
                                OrderName = orderName,
                                Seed = runSeed,
                                Ok = false,
                                TimeMs = sw.ElapsedMilliseconds,
                                Error = ex.Message.Replace(Environment.NewLine, " ")
                            });
                            Console.WriteLine($"FAIL {orderName} seed={runSeed}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine();
            }

            var comparisons = Compare(runs);
            var aggregates = Aggregate(runs);

            WriteRunCsv(Path.Combine(outDir, "orientation-runs.csv"), runs);
            WriteComparisonCsv(Path.Combine(outDir, "orientation-comparison.csv"), comparisons);
            WriteAggregateCsv(Path.Combine(outDir, "orientation-summary.csv"), aggregates);
            WriteMarkdown(Path.Combine(outDir, "summary.md"), seed, maxOrders, seedRuns, aggregates, comparisons);
            WriteJson(Path.Combine(outDir, "summary.json"), seed, maxOrders, seedRuns, aggregates, comparisons);
            PrintSummary(aggregates, comparisons);

            return runs.Any(r => !r.Ok) ? 1 : 0;
        }

        private static RunMetric Measure(string candidate, string orderName, int seed, string outPath, long timeMs)
        {
            var vr = PackingCsvValidator.ReadPackingCsv(outPath);
            var byPallet = vr.Boxes
                .GroupBy(b => b.PalletId, StringComparer.Ordinal)
                .ToList();

            double maxHeight = 0.0;
            double totalBoxVolume = 0.0;
            double totalBoundingVolume = 0.0;
            double emptyVolume = 0.0;
            double supportSum = 0.0;
            int supportCount = 0;
            int weakSupportBoxes = 0;

            foreach (var group in byPallet)
            {
                if (!vr.Pallets.TryGetValue(group.Key, out var pallet))
                    continue;

                var boxes = group.ToList();
                double height = boxes.Count == 0 ? 0.0 : boxes.Max(b => b.Z) - pallet.OriginZ;
                double baseArea = pallet.SizeL * pallet.SizeW;
                double boundingVolume = baseArea * Math.Max(0.0, height);
                double boxVolume = boxes.Sum(b => b.Dx * b.Dy * b.Dz);

                maxHeight = Math.Max(maxHeight, height);
                totalBoxVolume += boxVolume;
                totalBoundingVolume += boundingVolume;
                emptyVolume += Math.Max(0.0, boundingVolume - boxVolume);

                foreach (var box in boxes)
                {
                    double ratio = SupportRatio(box, boxes, pallet);
                    supportSum += ratio;
                    supportCount++;
                    if (ratio < 0.65)
                        weakSupportBoxes++;
                }
            }

            return new RunMetric
            {
                Candidate = candidate,
                OrderName = orderName,
                Seed = seed,
                Ok = true,
                Boxes = vr.Boxes.Count,
                Pallets = Math.Max(1, vr.Pallets.Count),
                Containers = Math.Max(1, vr.Containers.Count),
                MaxHeight = maxHeight,
                EmptyVolume = emptyVolume,
                FillByUsedHeight = totalBoundingVolume > 0.0 ? totalBoxVolume / totalBoundingVolume : 0.0,
                AvgSupport = supportCount > 0 ? supportSum / supportCount : 1.0,
                WeakSupportBoxes = weakSupportBoxes,
                TimeMs = timeMs
            };
        }

        private static List<ComparisonMetric> Compare(IReadOnlyList<RunMetric> runs)
        {
            var result = new List<ComparisonMetric>();
            var groups = runs.GroupBy(r => (r.OrderName, r.Seed));

            foreach (var group in groups)
            {
                var geneOnly = group.FirstOrDefault(r => r.Candidate == "gene-only");
                var fallback = group.FirstOrDefault(r => r.Candidate == "fallback");
                if (geneOnly is null || fallback is null)
                    continue;

                bool bothOk = geneOnly.Ok && fallback.Ok;
                result.Add(new ComparisonMetric
                {
                    OrderName = group.Key.OrderName,
                    Seed = group.Key.Seed,
                    BothOk = bothOk,
                    GeneOnlyPallets = geneOnly.Pallets,
                    FallbackPallets = fallback.Pallets,
                    DeltaPallets = fallback.Pallets - geneOnly.Pallets,
                    GeneOnlyHeight = geneOnly.MaxHeight,
                    FallbackHeight = fallback.MaxHeight,
                    DeltaHeight = fallback.MaxHeight - geneOnly.MaxHeight,
                    GeneOnlyEmptyVolume = geneOnly.EmptyVolume,
                    FallbackEmptyVolume = fallback.EmptyVolume,
                    DeltaEmptyVolume = fallback.EmptyVolume - geneOnly.EmptyVolume,
                    GeneOnlyFill = geneOnly.FillByUsedHeight,
                    FallbackFill = fallback.FillByUsedHeight,
                    DeltaFill = fallback.FillByUsedHeight - geneOnly.FillByUsedHeight,
                    GeneOnlyTimeMs = geneOnly.TimeMs,
                    FallbackTimeMs = fallback.TimeMs,
                    DeltaTimeMs = fallback.TimeMs - geneOnly.TimeMs
                });
            }

            return result
                .OrderBy(c => c.OrderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Seed)
                .ToList();
        }

        private static List<AggregateMetric> Aggregate(IReadOnlyList<RunMetric> runs)
        {
            return runs
                .GroupBy(r => r.Candidate, StringComparer.Ordinal)
                .Select(g =>
                {
                    var ok = g.Where(r => r.Ok).ToList();
                    return new AggregateMetric
                    {
                        Candidate = g.Key,
                        Runs = g.Count(),
                        Ok = ok.Count,
                        Fail = g.Count(r => !r.Ok),
                        AvgPallets = ok.Count > 0 ? ok.Average(r => r.Pallets) : 0.0,
                        AvgContainers = ok.Count > 0 ? ok.Average(r => r.Containers) : 0.0,
                        AvgMaxHeight = ok.Count > 0 ? ok.Average(r => r.MaxHeight) : 0.0,
                        AvgEmptyVolume = ok.Count > 0 ? ok.Average(r => r.EmptyVolume) : 0.0,
                        AvgFillByUsedHeight = ok.Count > 0 ? ok.Average(r => r.FillByUsedHeight) : 0.0,
                        AvgSupport = ok.Count > 0 ? ok.Average(r => r.AvgSupport) : 0.0,
                        AvgWeakSupportBoxes = ok.Count > 0 ? ok.Average(r => r.WeakSupportBoxes) : 0.0,
                        AvgTimeMs = ok.Count > 0 ? ok.Average(r => r.TimeMs) : 0.0
                    };
                })
                .OrderBy(a => a.Candidate, StringComparer.Ordinal)
                .ToList();
        }

        private static double SupportRatio(BoxPlacement box, IReadOnlyList<BoxPlacement> samePalletBoxes, PalletMeta pallet)
        {
            if (Math.Abs(box.z - pallet.OriginZ) <= 1e-9)
                return 1.0;

            double footprint = box.Dx * box.Dy;
            if (footprint <= 0.0)
                return 0.0;

            double area = 0.0;
            foreach (var support in samePalletBoxes)
            {
                if (support.Id == box.Id)
                    continue;
                if (Math.Abs(support.Z - box.z) > 1e-9)
                    continue;

                area += OverlapArea(box, support);
            }

            return Math.Min(1.0, area / footprint);
        }

        private static double OverlapArea(BoxPlacement a, BoxPlacement b)
        {
            double dx = Math.Min(a.X, b.X) - Math.Max(a.x, b.x);
            double dy = Math.Min(a.Y, b.Y) - Math.Max(a.y, b.y);
            return dx > 0.0 && dy > 0.0 ? dx * dy : 0.0;
        }

        private static void WriteRunCsv(string path, IReadOnlyList<RunMetric> runs)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("candidate,order,seed,ok,boxes,pallets,containers,max_height,empty_volume,fill_by_used_height,avg_support,weak_support_boxes,time_ms,error");
            foreach (var r in runs)
            {
                sw.WriteLine(string.Join(",",
                    Csv(r.Candidate),
                    Csv(r.OrderName),
                    r.Seed.ToString(CultureInfo.InvariantCulture),
                    r.Ok ? "1" : "0",
                    r.Boxes.ToString(CultureInfo.InvariantCulture),
                    r.Pallets.ToString(CultureInfo.InvariantCulture),
                    r.Containers.ToString(CultureInfo.InvariantCulture),
                    r.MaxHeight.ToString("F3", CultureInfo.InvariantCulture),
                    r.EmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    r.FillByUsedHeight.ToString("F6", CultureInfo.InvariantCulture),
                    r.AvgSupport.ToString("F6", CultureInfo.InvariantCulture),
                    r.WeakSupportBoxes.ToString(CultureInfo.InvariantCulture),
                    r.TimeMs.ToString(CultureInfo.InvariantCulture),
                    Csv(r.Error)));
            }
        }

        private static void WriteComparisonCsv(string path, IReadOnlyList<ComparisonMetric> comparisons)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("order,seed,both_ok,gene_only_pallets,fallback_pallets,delta_pallets,gene_only_height,fallback_height,delta_height,gene_only_empty_volume,fallback_empty_volume,delta_empty_volume,gene_only_fill,fallback_fill,delta_fill,gene_only_time_ms,fallback_time_ms,delta_time_ms");
            foreach (var c in comparisons)
            {
                sw.WriteLine(string.Join(",",
                    Csv(c.OrderName),
                    c.Seed.ToString(CultureInfo.InvariantCulture),
                    c.BothOk ? "1" : "0",
                    c.GeneOnlyPallets.ToString(CultureInfo.InvariantCulture),
                    c.FallbackPallets.ToString(CultureInfo.InvariantCulture),
                    c.DeltaPallets.ToString(CultureInfo.InvariantCulture),
                    c.GeneOnlyHeight.ToString("F3", CultureInfo.InvariantCulture),
                    c.FallbackHeight.ToString("F3", CultureInfo.InvariantCulture),
                    c.DeltaHeight.ToString("F3", CultureInfo.InvariantCulture),
                    c.GeneOnlyEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    c.FallbackEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    c.DeltaEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    c.GeneOnlyFill.ToString("F6", CultureInfo.InvariantCulture),
                    c.FallbackFill.ToString("F6", CultureInfo.InvariantCulture),
                    c.DeltaFill.ToString("F6", CultureInfo.InvariantCulture),
                    c.GeneOnlyTimeMs.ToString(CultureInfo.InvariantCulture),
                    c.FallbackTimeMs.ToString(CultureInfo.InvariantCulture),
                    c.DeltaTimeMs.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private static void WriteAggregateCsv(string path, IReadOnlyList<AggregateMetric> aggregates)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("candidate,runs,ok,fail,avg_pallets,avg_containers,avg_max_height,avg_empty_volume,avg_fill_by_used_height,avg_support,avg_weak_support_boxes,avg_time_ms");
            foreach (var a in aggregates)
            {
                sw.WriteLine(string.Join(",",
                    Csv(a.Candidate),
                    a.Runs.ToString(CultureInfo.InvariantCulture),
                    a.Ok.ToString(CultureInfo.InvariantCulture),
                    a.Fail.ToString(CultureInfo.InvariantCulture),
                    a.AvgPallets.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgContainers.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgMaxHeight.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgFillByUsedHeight.ToString("F6", CultureInfo.InvariantCulture),
                    a.AvgSupport.ToString("F6", CultureInfo.InvariantCulture),
                    a.AvgWeakSupportBoxes.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgTimeMs.ToString("F3", CultureInfo.InvariantCulture)));
            }
        }

        private static void WriteMarkdown(
            string path,
            int seed,
            int maxOrders,
            int seedRuns,
            IReadOnlyList<AggregateMetric> aggregates,
            IReadOnlyList<ComparisonMetric> comparisons)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("# Orientation Fallback Experiment");
            sw.WriteLine();
            sw.WriteLine($"- Seed: `{seed}`");
            sw.WriteLine($"- Max orders: `{maxOrders}`");
            sw.WriteLine($"- Seed runs: `{seedRuns}`");
            sw.WriteLine();
            sw.WriteLine("## Summary");
            sw.WriteLine();
            sw.WriteLine("| candidate | ok/fail | avg pallets | avg height | avg empty | avg fill | avg time ms |");
            sw.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var a in aggregates)
            {
                sw.WriteLine(
                    $"| {a.Candidate} | {a.Ok}/{a.Fail} | {a.AvgPallets:F3} | {a.AvgMaxHeight:F1} | {a.AvgEmptyVolume:F0} | {a.AvgFillByUsedHeight:P2} | {a.AvgTimeMs:F0} |");
            }

            sw.WriteLine();
            sw.WriteLine("## Per-order Delta");
            sw.WriteLine();
            sw.WriteLine("Delta columns are `fallback - gene-only`, so negative pallets/empty/time is better.");
            sw.WriteLine();
            sw.WriteLine("| order | seed | delta pallets | delta height | delta empty | delta fill | delta time ms |");
            sw.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var c in comparisons)
            {
                sw.WriteLine(
                    $"| {c.OrderName} | {c.Seed} | {c.DeltaPallets} | {c.DeltaHeight:F1} | {c.DeltaEmptyVolume:F0} | {c.DeltaFill:P2} | {c.DeltaTimeMs} |");
            }
        }

        private static void WriteJson(
            string path,
            int seed,
            int maxOrders,
            int seedRuns,
            IReadOnlyList<AggregateMetric> aggregates,
            IReadOnlyList<ComparisonMetric> comparisons)
        {
            var payload = new
            {
                seed,
                maxOrders,
                seedRuns,
                aggregates,
                comparisons
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, options));
        }

        private static void PrintSummary(IReadOnlyList<AggregateMetric> aggregates, IReadOnlyList<ComparisonMetric> comparisons)
        {
            Console.WriteLine("== SUMMARY ==");
            Console.WriteLine("candidate   ok/fail  pallets  height  empty        fill    time_ms");
            foreach (var a in aggregates)
            {
                Console.WriteLine(
                    $"{a.Candidate,-10} {a.Ok,2}/{a.Fail,-3} " +
                    $"{a.AvgPallets,7:F2} {a.AvgMaxHeight,7:F0} " +
                    $"{a.AvgEmptyVolume,12:F0} {a.AvgFillByUsedHeight,7:P1} {a.AvgTimeMs,8:F0}");
            }

            int betterPallets = comparisons.Count(c => c.DeltaPallets < 0);
            int worsePallets = comparisons.Count(c => c.DeltaPallets > 0);
            double avgDeltaEmpty = comparisons.Count > 0 ? comparisons.Average(c => c.DeltaEmptyVolume) : 0.0;
            double avgDeltaTime = comparisons.Count > 0 ? comparisons.Average(c => c.DeltaTimeMs) : 0.0;

            Console.WriteLine();
            Console.WriteLine($"Fallback used fewer pallets in {betterPallets} runs, more pallets in {worsePallets} runs.");
            Console.WriteLine($"Avg delta empty volume: {avgDeltaEmpty:F0} mm^3");
            Console.WriteLine($"Avg delta time: {avgDeltaTime:F0} ms");
            Console.WriteLine();
            Console.WriteLine("Saved:");
            Console.WriteLine("  orientation-runs.csv");
            Console.WriteLine("  orientation-comparison.csv");
            Console.WriteLine("  orientation-summary.csv");
            Console.WriteLine("  summary.md");
            Console.WriteLine("  summary.json");
        }

        private static string SafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        private static int OrderIdFromPath(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : int.MaxValue;
        }

        private static string Csv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}
