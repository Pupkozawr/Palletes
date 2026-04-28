using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Palletes.Core;
using Palletes.Generation;
using Palletes.Models;

namespace Palletes.Testing
{
    public static class FitnessWeightExperimentRunner
    {
        private sealed record Candidate(string Name, GeneticPalletPacker.FitnessWeights Weights);

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

        private sealed class AggregateMetric
        {
            public string Candidate { get; init; } = "";
            public int Runs { get; init; }
            public int Ok { get; init; }
            public int Fail { get; init; }
            public double AvgBoxes { get; init; }
            public double AvgPallets { get; init; }
            public double AvgContainers { get; init; }
            public double AvgMaxHeight { get; init; }
            public double AvgEmptyVolume { get; init; }
            public double AvgFillByUsedHeight { get; init; }
            public double AvgSupport { get; init; }
            public double AvgWeakSupportBoxes { get; init; }
            public double AvgTimeMs { get; init; }
            public double RankScore { get; init; }
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

            Console.WriteLine("== FITNESS WEIGHT EXPERIMENT ==");
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
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxOrders))
                .ToList();

            var candidates = CandidateWeights();
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
                            GeneticPalletPacker.PackCsvForExperiment(orderPath, outPath, packingPallet, packingContainer, runSeed, candidate.Weights);
                            sw.Stop();

                            var metric = Measure(candidate.Name, orderName, runSeed, outPath, sw.ElapsedMilliseconds);
                            runs.Add(metric);
                            Console.WriteLine(
                                $"OK   {orderName} seed={runSeed}: " +
                                $"boxes={metric.Boxes} pallets={metric.Pallets} containers={metric.Containers} " +
                                $"fill={metric.FillByUsedHeight:P1} support={metric.AvgSupport:P0} time={metric.TimeMs}ms");
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

            var aggregates = Aggregate(runs);
            WriteRunCsv(Path.Combine(outDir, "fitness-weight-runs.csv"), runs);
            WriteAggregateCsv(Path.Combine(outDir, "fitness-weight-summary.csv"), aggregates);
            PrintRanking(aggregates);

            return runs.Any(r => !r.Ok) ? 1 : 0;
        }

        private static List<Candidate> CandidateWeights()
        {
            return new List<Candidate>
            {
                new("current_constants", new GeneticPalletPacker.FitnessWeights("current_constants", 0.18, 0.52, 0.06, 0.14, 0.06, 0.04, 0.02)),
                new("previous_balanced", new GeneticPalletPacker.FitnessWeights("previous_balanced", 0.26, 0.38, 0.10, 0.16, 0.06, 0.04, 0.02)),
                new("legacy_no_stability", new GeneticPalletPacker.FitnessWeights("legacy_no_stability", 0.30, 0.40, 0.10, 0.20, 0.00, 0.00, 0.02)),
                new("density_first", new GeneticPalletPacker.FitnessWeights("density_first", 0.42, 0.30, 0.08, 0.14, 0.04, 0.02, 0.02)),
                new("stability_first", new GeneticPalletPacker.FitnessWeights("stability_first", 0.22, 0.32, 0.08, 0.13, 0.18, 0.07, 0.02)),
                new("grouping_first", new GeneticPalletPacker.FitnessWeights("grouping_first", 0.22, 0.34, 0.24, 0.12, 0.05, 0.03, 0.02)),
                new("heavy_low_first", new GeneticPalletPacker.FitnessWeights("heavy_low_first", 0.23, 0.34, 0.08, 0.13, 0.07, 0.15, 0.02))
            };
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

        private static List<AggregateMetric> Aggregate(List<RunMetric> runs)
        {
            return runs
                .GroupBy(r => r.Candidate, StringComparer.Ordinal)
                .Select(g =>
                {
                    var ok = g.Where(r => r.Ok).ToList();
                    double avgContainers = ok.Count > 0 ? ok.Average(r => r.Containers) : double.PositiveInfinity;
                    double avgPallets = ok.Count > 0 ? ok.Average(r => r.Pallets) : double.PositiveInfinity;
                    double avgFill = ok.Count > 0 ? ok.Average(r => r.FillByUsedHeight) : 0.0;
                    double avgSupport = ok.Count > 0 ? ok.Average(r => r.AvgSupport) : 0.0;
                    double avgWeak = ok.Count > 0 ? ok.Average(r => r.WeakSupportBoxes) : double.PositiveInfinity;
                    double avgHeight = ok.Count > 0 ? ok.Average(r => r.MaxHeight) : double.PositiveInfinity;
                    double avgTime = ok.Count > 0 ? ok.Average(r => r.TimeMs) : double.PositiveInfinity;
                    int fail = g.Count(r => !r.Ok);

                    return new AggregateMetric
                    {
                        Candidate = g.Key,
                        Runs = g.Count(),
                        Ok = ok.Count,
                        Fail = fail,
                        AvgBoxes = ok.Count > 0 ? ok.Average(r => r.Boxes) : 0.0,
                        AvgPallets = avgPallets,
                        AvgContainers = avgContainers,
                        AvgMaxHeight = avgHeight,
                        AvgEmptyVolume = ok.Count > 0 ? ok.Average(r => r.EmptyVolume) : 0.0,
                        AvgFillByUsedHeight = avgFill,
                        AvgSupport = avgSupport,
                        AvgWeakSupportBoxes = avgWeak,
                        AvgTimeMs = avgTime,
                        RankScore =
                            fail * 1000.0 +
                            avgContainers * 100.0 +
                            avgPallets * 20.0 +
                            avgWeak * 5.0 +
                            (1.0 - avgSupport) * 10.0 +
                            (1.0 - avgFill) * 5.0 +
                            avgHeight / 10000.0 +
                            avgTime / 1_000_000.0
                    };
                })
                .OrderBy(a => a.RankScore)
                .ThenBy(a => a.AvgContainers)
                .ThenBy(a => a.AvgPallets)
                .ToList();
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

        private static void WriteAggregateCsv(string path, IReadOnlyList<AggregateMetric> aggregates)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("candidate,runs,ok,fail,avg_boxes,avg_pallets,avg_containers,avg_max_height,avg_empty_volume,avg_fill_by_used_height,avg_support,avg_weak_support_boxes,avg_time_ms,rank_score");
            foreach (var a in aggregates)
            {
                sw.WriteLine(string.Join(",",
                    Csv(a.Candidate),
                    a.Runs.ToString(CultureInfo.InvariantCulture),
                    a.Ok.ToString(CultureInfo.InvariantCulture),
                    a.Fail.ToString(CultureInfo.InvariantCulture),
                    a.AvgBoxes.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgPallets.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgContainers.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgMaxHeight.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgFillByUsedHeight.ToString("F6", CultureInfo.InvariantCulture),
                    a.AvgSupport.ToString("F6", CultureInfo.InvariantCulture),
                    a.AvgWeakSupportBoxes.ToString("F3", CultureInfo.InvariantCulture),
                    a.AvgTimeMs.ToString("F3", CultureInfo.InvariantCulture),
                    a.RankScore.ToString("F6", CultureInfo.InvariantCulture)));
            }
        }

        private static void PrintRanking(IReadOnlyList<AggregateMetric> aggregates)
        {
            Console.WriteLine("== SUMMARY ==");
            Console.WriteLine("candidate              ok/fail  pallets  containers  fill    support  weak  time_ms  rank");
            foreach (var a in aggregates)
            {
                Console.WriteLine(
                    $"{a.Candidate,-22} {a.Ok,2}/{a.Fail,-3} " +
                    $"{a.AvgPallets,7:F2} {a.AvgContainers,10:F2} " +
                    $"{a.AvgFillByUsedHeight,6:P1} {a.AvgSupport,8:P0} " +
                    $"{a.AvgWeakSupportBoxes,5:F2} {a.AvgTimeMs,8:F0} {a.RankScore,8:F3}");
            }
            Console.WriteLine();
            Console.WriteLine("Saved:");
            Console.WriteLine("  fitness-weight-runs.csv");
            Console.WriteLine("  fitness-weight-summary.csv");
        }

        private static string SafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        private static string Csv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
