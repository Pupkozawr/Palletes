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
using Palletes.Utils;

namespace Palletes.Testing
{
    public static class SearchAlgorithmComparisonRunner
    {
        private sealed record Candidate(string Name);

        private sealed class OrderInfo
        {
            public string Path { get; init; } = "";
            public string Name { get; init; } = "";
            public string Group { get; init; } = "";
            public string Scenario { get; init; } = "";
            public int Boxes { get; init; }
            public int Skus { get; init; }
            public int? TargetPallets { get; init; }
        }

        private sealed class RunMetric
        {
            public string Candidate { get; init; } = "";
            public string OrderName { get; init; } = "";
            public string Group { get; init; } = "";
            public string Scenario { get; init; } = "";
            public int Seed { get; init; }
            public int GreedyRandomStarts { get; init; }
            public bool Ok { get; init; }
            public int InputBoxes { get; init; }
            public int OutputBoxes { get; init; }
            public int Skus { get; init; }
            public int Pallets { get; init; }
            public int Containers { get; init; }
            public double MaxHeight { get; init; }
            public double EmptyVolume { get; init; }
            public double FillByUsedHeight { get; init; }
            public double PalletVolumeUse { get; init; }
            public double AvgSupport { get; init; }
            public int WeakSupportBoxes { get; init; }
            public long TimeMs { get; init; }
            public string Error { get; init; } = "";
        }

        private sealed class ComparisonMetric
        {
            public string Candidate { get; init; } = "";
            public string OrderName { get; init; } = "";
            public string Group { get; init; } = "";
            public string Scenario { get; init; } = "";
            public int Boxes { get; init; }
            public int Seed { get; init; }
            public bool BothOk { get; init; }
            public int GaPallets { get; init; }
            public int CandidatePallets { get; init; }
            public int DeltaPallets { get; init; }
            public double GaHeight { get; init; }
            public double CandidateHeight { get; init; }
            public double DeltaHeight { get; init; }
            public double GaEmptyVolume { get; init; }
            public double CandidateEmptyVolume { get; init; }
            public double DeltaEmptyVolume { get; init; }
            public double GaFill { get; init; }
            public double CandidateFill { get; init; }
            public double DeltaFill { get; init; }
            public double GaSupport { get; init; }
            public double CandidateSupport { get; init; }
            public double DeltaSupport { get; init; }
            public long GaTimeMs { get; init; }
            public long CandidateTimeMs { get; init; }
            public long DeltaTimeMs { get; init; }
            public double Speedup { get; init; }
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
            public double AvgPalletVolumeUse { get; init; }
            public double AvgSupport { get; init; }
            public double AvgWeakSupportBoxes { get; init; }
            public double AvgTimeMs { get; init; }
        }

        public static int Run(
            string outDir,
            int seed,
            int maxOrders,
            int seedRuns,
            int greedyRandomStarts,
            int maxBoxes,
            PalletSpec generationPallet,
            PalletSpec packingPallet,
            ContainerSpec packingContainer)
        {
            Directory.CreateDirectory(outDir);

            var dataDir = Path.Combine(outDir, "data");
            var resultsDir = Path.Combine(outDir, "results");
            var plotsDir = Path.Combine(outDir, "plots");
            Directory.CreateDirectory(resultsDir);
            Directory.CreateDirectory(plotsDir);

            greedyRandomStarts = Math.Clamp(greedyRandomStarts, 0, 500);
            seedRuns = Math.Max(1, seedRuns);
            maxOrders = Math.Max(1, maxOrders);

            Console.WriteLine("== SEARCH ALGORITHM COMPARISON ==");
            Console.WriteLine($"OutDir: {Path.GetFullPath(outDir)}");
            Console.WriteLine($"Seed: {seed}");
            Console.WriteLine($"MaxOrders: {maxOrders}");
            Console.WriteLine($"SeedRuns: {seedRuns}");
            Console.WriteLine($"GreedyRandomStarts: {greedyRandomStarts}");
            Console.WriteLine($"MaxBoxes: {(maxBoxes > 0 ? maxBoxes.ToString(CultureInfo.InvariantCulture) : "none")}");
            Console.WriteLine();

            if (!Directory.Exists(dataDir) || !Directory.EnumerateFiles(dataDir, "*.csv", SearchOption.AllDirectories).Any())
            {
                var rng = new Rng(seed);
                var generator = new DatasetGenerator(Profile.DefaultRetailLike(), generationPallet, rng);
                generator.GenerateAll(dataDir);
            }

            var orders = SelectOrders(dataDir, maxOrders, maxBoxes);
            if (orders.Count == 0)
            {
                Console.WriteLine("No orders selected for comparison.");
                return 2;
            }

            WriteOrderCsv(Path.Combine(outDir, "benchmark-orders.csv"), orders);

            var candidates = new List<Candidate>
            {
                new("ga"),
                new("multi-start-greedy"),
                new("multi-start-greedy-bestfit")
            };

            var runs = new List<RunMetric>();

            foreach (var order in orders)
            {
                Console.WriteLine($"== {order.Name} ({order.Group}, {order.Scenario}, boxes={order.Boxes}) ==");

                for (int s = 0; s < seedRuns; s++)
                {
                    int runSeed = seed + s * 7919;

                    foreach (var candidate in candidates)
                    {
                        var candidateDir = Path.Combine(resultsDir, SafeName(candidate.Name));
                        Directory.CreateDirectory(candidateDir);
                        var outPath = Path.Combine(candidateDir, $"{order.Name}-seed{runSeed}-out.csv");

                        var sw = Stopwatch.StartNew();
                        try
                        {
                            if (candidate.Name == "ga")
                            {
                                GeneticPalletPacker.PackCsv(order.Path, outPath, packingPallet, packingContainer, runSeed);
                            }
                            else if (candidate.Name == "multi-start-greedy-bestfit")
                            {
                                GeneticPalletPacker.PackCsvMultiStartGreedyBestFit(
                                    order.Path,
                                    outPath,
                                    packingPallet,
                                    packingContainer,
                                    runSeed,
                                    greedyRandomStarts);
                            }
                            else
                            {
                                GeneticPalletPacker.PackCsvMultiStartGreedy(
                                    order.Path,
                                    outPath,
                                    packingPallet,
                                    packingContainer,
                                    runSeed,
                                    greedyRandomStarts);
                            }

                            sw.Stop();
                            var metric = Measure(candidate.Name, order, runSeed, greedyRandomStarts, outPath, sw.ElapsedMilliseconds, packingPallet);
                            runs.Add(metric);

                            Console.WriteLine(
                                $"OK   {candidate.Name,-18} seed={runSeed}: " +
                                $"pallets={metric.Pallets} height={metric.MaxHeight:F0} " +
                                $"fill={metric.FillByUsedHeight:P1} support={metric.AvgSupport:P0} " +
                                $"time={metric.TimeMs}ms");
                        }
                        catch (Exception ex)
                        {
                            sw.Stop();
                            runs.Add(new RunMetric
                            {
                                Candidate = candidate.Name,
                                OrderName = order.Name,
                                Group = order.Group,
                                Scenario = order.Scenario,
                                Seed = runSeed,
                                GreedyRandomStarts = greedyRandomStarts,
                                Ok = false,
                                InputBoxes = order.Boxes,
                                Skus = order.Skus,
                                TimeMs = sw.ElapsedMilliseconds,
                                Error = ex.Message.Replace(Environment.NewLine, " ")
                            });

                            Console.WriteLine($"FAIL {candidate.Name,-18} seed={runSeed}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine();
            }

            var comparisons = Compare(runs);
            var aggregates = Aggregate(runs);

            WriteRunCsv(Path.Combine(outDir, "search-runs.csv"), runs);
            WriteComparisonCsv(Path.Combine(outDir, "search-comparison.csv"), comparisons);
            WriteAggregateCsv(Path.Combine(outDir, "search-summary.csv"), aggregates);
            WriteMarkdown(Path.Combine(outDir, "summary.md"), seed, maxOrders, seedRuns, greedyRandomStarts, maxBoxes, orders, aggregates, comparisons);
            WriteJson(Path.Combine(outDir, "summary.json"), seed, maxOrders, seedRuns, greedyRandomStarts, maxBoxes, orders, aggregates, comparisons);
            WritePlots(plotsDir, runs, comparisons, aggregates);
            PrintSummary(aggregates, comparisons, outDir);

            return runs.Any(r => !r.Ok) ? 1 : 0;
        }

        private static List<OrderInfo> SelectOrders(string dataDir, int maxOrders, int maxBoxes)
        {
            var all = Directory.EnumerateFiles(dataDir, "*.csv", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("-out.csv", StringComparison.OrdinalIgnoreCase))
                .Select(ReadOrderInfo)
                .Where(o => maxBoxes <= 0 || o.Boxes <= maxBoxes)
                .OrderBy(o => o.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Boxes)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new List<OrderInfo>();
            var byGroup = all
                .GroupBy(o => o.Group, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.ToList())
                .ToList();

            int round = 0;
            while (result.Count < maxOrders)
            {
                bool added = false;
                foreach (var group in byGroup)
                {
                    if (round >= group.Count)
                        continue;

                    result.Add(group[round]);
                    added = true;
                    if (result.Count >= maxOrders)
                        break;
                }

                if (!added)
                    break;

                round++;
            }

            return result
                .OrderBy(o => o.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Boxes)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static OrderInfo ReadOrderInfo(string path)
        {
            var items = ItemRow.ParseSimple(path);
            var meta = ReadMeta(Path.Combine(Path.GetDirectoryName(path) ?? "", "meta.txt"));
            string group = meta.TryGetValue("group", out var g) ? g : new DirectoryInfo(Path.GetDirectoryName(path) ?? "").Parent?.Name ?? "";
            string scenario = meta.TryGetValue("scenario", out var sc) ? sc : "";
            int? targetPallets = meta.TryGetValue("target_pallets", out var tp) && int.TryParse(tp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTp)
                ? parsedTp
                : null;

            string id = Path.GetFileNameWithoutExtension(path);
            string name = $"{group}_{id}";

            return new OrderInfo
            {
                Path = path,
                Name = name,
                Group = group,
                Scenario = scenario,
                Boxes = items.Sum(i => Math.Max(0, i.Quantity)),
                Skus = items.Count,
                TargetPallets = targetPallets
            };
        }

        private static Dictionary<string, string> ReadMeta(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return result;

            foreach (var line in File.ReadLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }

            return result;
        }

        private static RunMetric Measure(
            string candidate,
            OrderInfo order,
            int seed,
            int greedyRandomStarts,
            string outPath,
            long timeMs,
            PalletSpec palletSpec)
        {
            var vr = PackingCsvValidator.ReadPackingCsv(outPath);
            var byPallet = vr.Boxes
                .GroupBy(b => b.PalletId, StringComparer.Ordinal)
                .ToList();

            double maxHeight = 0.0;
            double totalBoxVolume = 0.0;
            double totalBoundingVolume = 0.0;
            double totalPalletVolume = 0.0;
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
                totalPalletVolume += baseArea * palletSpec.MaxHeight;
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
                OrderName = order.Name,
                Group = order.Group,
                Scenario = order.Scenario,
                Seed = seed,
                GreedyRandomStarts = greedyRandomStarts,
                Ok = true,
                InputBoxes = order.Boxes,
                OutputBoxes = vr.Boxes.Count,
                Skus = order.Skus,
                Pallets = Math.Max(1, vr.Pallets.Count),
                Containers = Math.Max(1, vr.Containers.Count),
                MaxHeight = maxHeight,
                EmptyVolume = emptyVolume,
                FillByUsedHeight = totalBoundingVolume > 0.0 ? totalBoxVolume / totalBoundingVolume : 0.0,
                PalletVolumeUse = totalPalletVolume > 0.0 ? totalBoxVolume / totalPalletVolume : 0.0,
                AvgSupport = supportCount > 0 ? supportSum / supportCount : 1.0,
                WeakSupportBoxes = weakSupportBoxes,
                TimeMs = timeMs
            };
        }

        private static List<ComparisonMetric> Compare(IReadOnlyList<RunMetric> runs)
        {
            var result = new List<ComparisonMetric>();

            foreach (var group in runs.GroupBy(r => (r.OrderName, r.Seed)))
            {
                var ga = group.FirstOrDefault(r => r.Candidate == "ga");
                if (ga is null)
                    continue;

                foreach (var candidate in group.Where(r => r.Candidate != "ga").OrderBy(r => r.Candidate, StringComparer.Ordinal))
                {
                    bool bothOk = ga.Ok && candidate.Ok;
                    result.Add(new ComparisonMetric
                    {
                        Candidate = candidate.Candidate,
                        OrderName = group.Key.OrderName,
                        Group = ga.Group,
                        Scenario = ga.Scenario,
                        Boxes = Math.Max(ga.InputBoxes, candidate.InputBoxes),
                        Seed = group.Key.Seed,
                        BothOk = bothOk,
                        GaPallets = ga.Pallets,
                        CandidatePallets = candidate.Pallets,
                        DeltaPallets = candidate.Pallets - ga.Pallets,
                        GaHeight = ga.MaxHeight,
                        CandidateHeight = candidate.MaxHeight,
                        DeltaHeight = candidate.MaxHeight - ga.MaxHeight,
                        GaEmptyVolume = ga.EmptyVolume,
                        CandidateEmptyVolume = candidate.EmptyVolume,
                        DeltaEmptyVolume = candidate.EmptyVolume - ga.EmptyVolume,
                        GaFill = ga.FillByUsedHeight,
                        CandidateFill = candidate.FillByUsedHeight,
                        DeltaFill = candidate.FillByUsedHeight - ga.FillByUsedHeight,
                        GaSupport = ga.AvgSupport,
                        CandidateSupport = candidate.AvgSupport,
                        DeltaSupport = candidate.AvgSupport - ga.AvgSupport,
                        GaTimeMs = ga.TimeMs,
                        CandidateTimeMs = candidate.TimeMs,
                        DeltaTimeMs = candidate.TimeMs - ga.TimeMs,
                        Speedup = candidate.TimeMs > 0 ? ga.TimeMs / (double)candidate.TimeMs : 0.0
                    });
                }
            }

            return result
                .OrderBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Boxes)
                .ThenBy(c => c.OrderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Candidate, StringComparer.Ordinal)
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
                        AvgBoxes = ok.Count > 0 ? ok.Average(r => r.InputBoxes) : 0.0,
                        AvgPallets = ok.Count > 0 ? ok.Average(r => r.Pallets) : 0.0,
                        AvgContainers = ok.Count > 0 ? ok.Average(r => r.Containers) : 0.0,
                        AvgMaxHeight = ok.Count > 0 ? ok.Average(r => r.MaxHeight) : 0.0,
                        AvgEmptyVolume = ok.Count > 0 ? ok.Average(r => r.EmptyVolume) : 0.0,
                        AvgFillByUsedHeight = ok.Count > 0 ? ok.Average(r => r.FillByUsedHeight) : 0.0,
                        AvgPalletVolumeUse = ok.Count > 0 ? ok.Average(r => r.PalletVolumeUse) : 0.0,
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

        private static void WriteOrderCsv(string path, IReadOnlyList<OrderInfo> orders)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("order,group,scenario,boxes,skus,target_pallets,path");
            foreach (var o in orders)
            {
                sw.WriteLine(string.Join(",",
                    Csv(o.Name),
                    Csv(o.Group),
                    Csv(o.Scenario),
                    o.Boxes.ToString(CultureInfo.InvariantCulture),
                    o.Skus.ToString(CultureInfo.InvariantCulture),
                    o.TargetPallets?.ToString(CultureInfo.InvariantCulture) ?? "",
                    Csv(o.Path)));
            }
        }

        private static void WriteRunCsv(string path, IReadOnlyList<RunMetric> runs)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("candidate,order,group,scenario,seed,greedy_random_starts,ok,input_boxes,output_boxes,skus,pallets,containers,max_height,empty_volume,fill_by_used_height,pallet_volume_use,avg_support,weak_support_boxes,time_ms,error");
            foreach (var r in runs)
            {
                sw.WriteLine(string.Join(",",
                    Csv(r.Candidate),
                    Csv(r.OrderName),
                    Csv(r.Group),
                    Csv(r.Scenario),
                    r.Seed.ToString(CultureInfo.InvariantCulture),
                    r.GreedyRandomStarts.ToString(CultureInfo.InvariantCulture),
                    r.Ok ? "1" : "0",
                    r.InputBoxes.ToString(CultureInfo.InvariantCulture),
                    r.OutputBoxes.ToString(CultureInfo.InvariantCulture),
                    r.Skus.ToString(CultureInfo.InvariantCulture),
                    r.Pallets.ToString(CultureInfo.InvariantCulture),
                    r.Containers.ToString(CultureInfo.InvariantCulture),
                    r.MaxHeight.ToString("F3", CultureInfo.InvariantCulture),
                    r.EmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    r.FillByUsedHeight.ToString("F6", CultureInfo.InvariantCulture),
                    r.PalletVolumeUse.ToString("F6", CultureInfo.InvariantCulture),
                    r.AvgSupport.ToString("F6", CultureInfo.InvariantCulture),
                    r.WeakSupportBoxes.ToString(CultureInfo.InvariantCulture),
                    r.TimeMs.ToString(CultureInfo.InvariantCulture),
                    Csv(r.Error)));
            }
        }

        private static void WriteComparisonCsv(string path, IReadOnlyList<ComparisonMetric> comparisons)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("candidate,order,group,scenario,boxes,seed,both_ok,ga_pallets,candidate_pallets,delta_pallets,ga_height,candidate_height,delta_height,ga_empty_volume,candidate_empty_volume,delta_empty_volume,ga_fill,candidate_fill,delta_fill,ga_support,candidate_support,delta_support,ga_time_ms,candidate_time_ms,delta_time_ms,speedup_ga_over_candidate");
            foreach (var c in comparisons)
            {
                sw.WriteLine(string.Join(",",
                    Csv(c.Candidate),
                    Csv(c.OrderName),
                    Csv(c.Group),
                    Csv(c.Scenario),
                    c.Boxes.ToString(CultureInfo.InvariantCulture),
                    c.Seed.ToString(CultureInfo.InvariantCulture),
                    c.BothOk ? "1" : "0",
                    c.GaPallets.ToString(CultureInfo.InvariantCulture),
                    c.CandidatePallets.ToString(CultureInfo.InvariantCulture),
                    c.DeltaPallets.ToString(CultureInfo.InvariantCulture),
                    c.GaHeight.ToString("F3", CultureInfo.InvariantCulture),
                    c.CandidateHeight.ToString("F3", CultureInfo.InvariantCulture),
                    c.DeltaHeight.ToString("F3", CultureInfo.InvariantCulture),
                    c.GaEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    c.CandidateEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    c.DeltaEmptyVolume.ToString("F3", CultureInfo.InvariantCulture),
                    c.GaFill.ToString("F6", CultureInfo.InvariantCulture),
                    c.CandidateFill.ToString("F6", CultureInfo.InvariantCulture),
                    c.DeltaFill.ToString("F6", CultureInfo.InvariantCulture),
                    c.GaSupport.ToString("F6", CultureInfo.InvariantCulture),
                    c.CandidateSupport.ToString("F6", CultureInfo.InvariantCulture),
                    c.DeltaSupport.ToString("F6", CultureInfo.InvariantCulture),
                    c.GaTimeMs.ToString(CultureInfo.InvariantCulture),
                    c.CandidateTimeMs.ToString(CultureInfo.InvariantCulture),
                    c.DeltaTimeMs.ToString(CultureInfo.InvariantCulture),
                    c.Speedup.ToString("F6", CultureInfo.InvariantCulture)));
            }
        }

        private static void WriteAggregateCsv(string path, IReadOnlyList<AggregateMetric> aggregates)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("candidate,runs,ok,fail,avg_boxes,avg_pallets,avg_containers,avg_max_height,avg_empty_volume,avg_fill_by_used_height,avg_pallet_volume_use,avg_support,avg_weak_support_boxes,avg_time_ms");
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
                    a.AvgPalletVolumeUse.ToString("F6", CultureInfo.InvariantCulture),
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
            int greedyRandomStarts,
            int maxBoxes,
            IReadOnlyList<OrderInfo> orders,
            IReadOnlyList<AggregateMetric> aggregates,
            IReadOnlyList<ComparisonMetric> comparisons)
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("# Search Algorithm Comparison");
            sw.WriteLine();
            sw.WriteLine($"- Seed: `{seed}`");
            sw.WriteLine($"- Max orders: `{maxOrders}`");
            sw.WriteLine($"- Seed runs: `{seedRuns}`");
            sw.WriteLine($"- Multi-start greedy random starts: `{greedyRandomStarts}`");
            sw.WriteLine($"- Max boxes filter: `{(maxBoxes > 0 ? maxBoxes.ToString(CultureInfo.InvariantCulture) : "none")}`");
            sw.WriteLine();
            sw.WriteLine("## Dataset");
            sw.WriteLine();
            sw.WriteLine("| order | group | scenario | boxes | skus |");
            sw.WriteLine("|---|---|---|---:|---:|");
            foreach (var o in orders)
            {
                sw.WriteLine($"| {o.Name} | {o.Group} | {o.Scenario} | {o.Boxes} | {o.Skus} |");
            }

            sw.WriteLine();
            sw.WriteLine("## Aggregate Summary");
            sw.WriteLine();
            sw.WriteLine("| candidate | ok/fail | avg boxes | avg pallets | avg height | avg empty | avg fill | avg support | avg time ms |");
            sw.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var a in aggregates)
            {
                sw.WriteLine(
                    $"| {a.Candidate} | {a.Ok}/{a.Fail} | {a.AvgBoxes:F1} | {a.AvgPallets:F2} | {a.AvgMaxHeight:F1} | {a.AvgEmptyVolume:F0} | {a.AvgFillByUsedHeight:P2} | {a.AvgSupport:P1} | {a.AvgTimeMs:F0} |");
            }

            sw.WriteLine();
            sw.WriteLine("## Pairwise Delta");
            sw.WriteLine();
            sw.WriteLine("Delta columns are `candidate - ga`. Negative pallets, height, empty volume and time are better for the candidate; positive fill/support are better for the candidate.");
            sw.WriteLine();
            sw.WriteLine("| candidate | order | boxes | seed | delta pallets | delta height | delta empty | delta fill | delta support | speedup ga/candidate |");
            sw.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var c in comparisons)
            {
                sw.WriteLine(
                    $"| {c.Candidate} | {c.OrderName} | {c.Boxes} | {c.Seed} | {c.DeltaPallets} | {c.DeltaHeight:F1} | {c.DeltaEmptyVolume:F0} | {c.DeltaFill:P2} | {c.DeltaSupport:P2} | {c.Speedup:F2}x |");
            }

            sw.WriteLine();
            sw.WriteLine("## Plots");
            sw.WriteLine();
            sw.WriteLine("- `plots/avg_time.svg`");
            sw.WriteLine("- `plots/avg_quality.svg`");
            sw.WriteLine("- `plots/per_order_time.svg`");
            sw.WriteLine("- `plots/time_vs_boxes.svg`");
            sw.WriteLine("- `plots/pallets_by_order.svg`");
        }

        private static void WriteJson(
            string path,
            int seed,
            int maxOrders,
            int seedRuns,
            int greedyRandomStarts,
            int maxBoxes,
            IReadOnlyList<OrderInfo> orders,
            IReadOnlyList<AggregateMetric> aggregates,
            IReadOnlyList<ComparisonMetric> comparisons)
        {
            var payload = new
            {
                seed,
                maxOrders,
                seedRuns,
                greedyRandomStarts,
                maxBoxes,
                orders,
                aggregates,
                comparisons
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void WritePlots(
            string plotsDir,
            IReadOnlyList<RunMetric> runs,
            IReadOnlyList<ComparisonMetric> comparisons,
            IReadOnlyList<AggregateMetric> aggregates)
        {
            WriteBarChart(
                Path.Combine(plotsDir, "avg_time.svg"),
                "Average packing time, ms",
                aggregates.Select(a => (a.Candidate, a.AvgTimeMs, ColorFor(a.Candidate))).ToList());

            WriteGroupedQualityChart(Path.Combine(plotsDir, "avg_quality.svg"), aggregates);
            WritePerOrderTimeChart(Path.Combine(plotsDir, "per_order_time.svg"), comparisons);
            WriteScatterChart(Path.Combine(plotsDir, "time_vs_boxes.svg"), runs);
            WritePalletsChart(Path.Combine(plotsDir, "pallets_by_order.svg"), comparisons);
        }

        private static void WriteBarChart(string path, string title, IReadOnlyList<(string Label, double Value, string Color)> bars)
        {
            const int width = 860;
            const int height = 430;
            const int left = 80;
            const int top = 60;
            const int plotWidth = 720;
            const int plotHeight = 280;

            double max = Math.Max(1.0, bars.Max(b => b.Value) * 1.15);
            double slot = plotWidth / (double)Math.Max(1, bars.Count);
            double barWidth = Math.Min(110.0, slot * 0.55);

            using var sw = new StreamWriter(path);
            WriteSvgHeader(sw, width, height, title);
            DrawAxes(sw, left, top, plotWidth, plotHeight, max);

            for (int i = 0; i < bars.Count; i++)
            {
                var b = bars[i];
                double x = left + i * slot + (slot - barWidth) / 2.0;
                double h = plotHeight * b.Value / max;
                double y = top + plotHeight - h;
                sw.WriteLine($"<rect x='{x:F1}' y='{y:F1}' width='{barWidth:F1}' height='{h:F1}' fill='{b.Color}' rx='3' />");
                sw.WriteLine($"<text x='{x + barWidth / 2:F1}' y='{top + plotHeight + 28}' text-anchor='middle' font-size='13'>{Escape(b.Label)}</text>");
                sw.WriteLine($"<text x='{x + barWidth / 2:F1}' y='{y - 8:F1}' text-anchor='middle' font-size='13'>{b.Value:F0}</text>");
            }

            WriteSvgFooter(sw);
        }

        private static void WriteGroupedQualityChart(string path, IReadOnlyList<AggregateMetric> aggregates)
        {
            const int width = 900;
            const int height = 460;
            const int left = 80;
            const int top = 70;
            const int plotWidth = 760;
            const int plotHeight = 280;

            var metrics = new[]
            {
                ("pallets", aggregates.Select(a => (a.Candidate, a.AvgPallets)).ToList()),
                ("fill %", aggregates.Select(a => (a.Candidate, a.AvgFillByUsedHeight * 100.0)).ToList()),
                ("support %", aggregates.Select(a => (a.Candidate, a.AvgSupport * 100.0)).ToList())
            };

            double max = Math.Max(1.0, metrics.SelectMany(m => m.Item2).Max(x => x.Item2) * 1.15);
            double groupSlot = plotWidth / (double)metrics.Length;
            double barWidth = 42.0;

            using var sw = new StreamWriter(path);
            WriteSvgHeader(sw, width, height, "Average quality metrics");
            DrawAxes(sw, left, top, plotWidth, plotHeight, max);

            for (int g = 0; g < metrics.Length; g++)
            {
                var metric = metrics[g];
                double center = left + g * groupSlot + groupSlot / 2.0;
                double totalBarsWidth = metric.Item2.Count * barWidth + Math.Max(0, metric.Item2.Count - 1) * 12.0;
                double startX = center - totalBarsWidth / 2.0;
                for (int i = 0; i < metric.Item2.Count; i++)
                {
                    var value = metric.Item2[i];
                    double x = startX + i * (barWidth + 12);
                    double h = plotHeight * value.Item2 / max;
                    double y = top + plotHeight - h;
                    sw.WriteLine($"<rect x='{x:F1}' y='{y:F1}' width='{barWidth:F1}' height='{h:F1}' fill='{ColorFor(value.Candidate)}' rx='3' />");
                    sw.WriteLine($"<text x='{x + barWidth / 2:F1}' y='{y - 7:F1}' text-anchor='middle' font-size='11'>{value.Item2:F1}</text>");
                }

                sw.WriteLine($"<text x='{center:F1}' y='{top + plotHeight + 28}' text-anchor='middle' font-size='13'>{Escape(metric.Item1)}</text>");
            }

            DrawLegend(sw, width - 230, top, aggregates.Select(a => a.Candidate).Distinct().ToList());
            WriteSvgFooter(sw);
        }

        private static void WritePerOrderTimeChart(string path, IReadOnlyList<ComparisonMetric> comparisons)
        {
            const int width = 1100;
            const int height = 520;
            const int left = 80;
            const int top = 70;
            const int plotWidth = 950;
            const int plotHeight = 310;

            var rows = comparisons.Take(18).ToList();
            double max = Math.Max(1.0, rows.SelectMany(c => new[] { (double)c.GaTimeMs, c.CandidateTimeMs }).DefaultIfEmpty(1.0).Max() * 1.15);
            double slot = plotWidth / (double)Math.Max(1, rows.Count);
            double barWidth = Math.Min(18.0, slot * 0.32);

            using var sw = new StreamWriter(path);
            WriteSvgHeader(sw, width, height, "Packing time by order, ms");
            DrawAxes(sw, left, top, plotWidth, plotHeight, max);

            for (int i = 0; i < rows.Count; i++)
            {
                var c = rows[i];
                double baseX = left + i * slot + slot / 2.0;
                DrawSingleBar(sw, baseX - barWidth - 2, top, plotHeight, max, c.GaTimeMs, barWidth, ColorFor("ga"));
                DrawSingleBar(sw, baseX + 2, top, plotHeight, max, c.CandidateTimeMs, barWidth, ColorFor(c.Candidate));
                sw.WriteLine($"<text x='{baseX:F1}' y='{top + plotHeight + 18}' text-anchor='end' font-size='10' transform='rotate(-35 {baseX:F1},{top + plotHeight + 18})'>{Escape(c.OrderName + " " + ShortName(c.Candidate))}</text>");
            }

            DrawLegend(sw, width - 330, top, new[] { "ga", "multi-start-greedy", "multi-start-greedy-bestfit" });
            WriteSvgFooter(sw);
        }

        private static void WriteScatterChart(string path, IReadOnlyList<RunMetric> runs)
        {
            const int width = 860;
            const int height = 470;
            const int left = 80;
            const int top = 70;
            const int plotWidth = 700;
            const int plotHeight = 300;

            var ok = runs.Where(r => r.Ok).ToList();
            double maxX = Math.Max(1.0, ok.Select(r => (double)r.InputBoxes).DefaultIfEmpty(1.0).Max() * 1.1);
            double maxY = Math.Max(1.0, ok.Select(r => (double)r.TimeMs).DefaultIfEmpty(1.0).Max() * 1.15);

            using var sw = new StreamWriter(path);
            WriteSvgHeader(sw, width, height, "Time vs input boxes");
            DrawAxes(sw, left, top, plotWidth, plotHeight, maxY);
            sw.WriteLine($"<text x='{left + plotWidth / 2}' y='{top + plotHeight + 52}' text-anchor='middle' font-size='13'>input boxes</text>");

            foreach (var r in ok)
            {
                double x = left + plotWidth * r.InputBoxes / maxX;
                double y = top + plotHeight - plotHeight * r.TimeMs / maxY;
                sw.WriteLine($"<circle cx='{x:F1}' cy='{y:F1}' r='5' fill='{ColorFor(r.Candidate)}' opacity='0.78'><title>{Escape(r.Candidate)} {Escape(r.OrderName)}: {r.TimeMs} ms</title></circle>");
            }

            DrawLegend(sw, width - 260, top, ok.Select(r => r.Candidate).Distinct().ToList());
            WriteSvgFooter(sw);
        }

        private static void WritePalletsChart(string path, IReadOnlyList<ComparisonMetric> comparisons)
        {
            const int width = 1100;
            const int height = 480;
            const int left = 80;
            const int top = 70;
            const int plotWidth = 950;
            const int plotHeight = 270;

            var rows = comparisons.Take(18).ToList();
            double max = Math.Max(1.0, rows.SelectMany(c => new[] { (double)c.GaPallets, c.CandidatePallets }).DefaultIfEmpty(1.0).Max() + 1);
            double slot = plotWidth / (double)Math.Max(1, rows.Count);
            double barWidth = Math.Min(18.0, slot * 0.32);

            using var sw = new StreamWriter(path);
            WriteSvgHeader(sw, width, height, "Pallets by order");
            DrawAxes(sw, left, top, plotWidth, plotHeight, max);

            for (int i = 0; i < rows.Count; i++)
            {
                var c = rows[i];
                double baseX = left + i * slot + slot / 2.0;
                DrawSingleBar(sw, baseX - barWidth - 2, top, plotHeight, max, c.GaPallets, barWidth, ColorFor("ga"));
                DrawSingleBar(sw, baseX + 2, top, plotHeight, max, c.CandidatePallets, barWidth, ColorFor(c.Candidate));
                sw.WriteLine($"<text x='{baseX:F1}' y='{top + plotHeight + 18}' text-anchor='end' font-size='10' transform='rotate(-35 {baseX:F1},{top + plotHeight + 18})'>{Escape(c.OrderName + " " + ShortName(c.Candidate))}</text>");
            }

            DrawLegend(sw, width - 330, top, new[] { "ga", "multi-start-greedy", "multi-start-greedy-bestfit" });
            WriteSvgFooter(sw);
        }

        private static void DrawSingleBar(StreamWriter sw, double x, int top, int plotHeight, double max, double value, double barWidth, string color)
        {
            double h = plotHeight * value / max;
            double y = top + plotHeight - h;
            sw.WriteLine($"<rect x='{x:F1}' y='{y:F1}' width='{barWidth:F1}' height='{h:F1}' fill='{color}' rx='2' />");
        }

        private static void WriteSvgHeader(StreamWriter sw, int width, int height, string title)
        {
            sw.WriteLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>");
            sw.WriteLine("<rect width='100%' height='100%' fill='white' />");
            sw.WriteLine($"<text x='{width / 2}' y='32' text-anchor='middle' font-family='Arial, sans-serif' font-size='20' font-weight='700'>{Escape(title)}</text>");
            sw.WriteLine("<g font-family='Arial, sans-serif' fill='#1f2937'>");
        }

        private static void WriteSvgFooter(StreamWriter sw)
        {
            sw.WriteLine("</g>");
            sw.WriteLine("</svg>");
        }

        private static void DrawAxes(StreamWriter sw, int left, int top, int plotWidth, int plotHeight, double maxY)
        {
            sw.WriteLine($"<line x1='{left}' y1='{top + plotHeight}' x2='{left + plotWidth}' y2='{top + plotHeight}' stroke='#374151' stroke-width='1' />");
            sw.WriteLine($"<line x1='{left}' y1='{top}' x2='{left}' y2='{top + plotHeight}' stroke='#374151' stroke-width='1' />");

            for (int i = 0; i <= 4; i++)
            {
                double value = maxY * i / 4.0;
                double y = top + plotHeight - plotHeight * i / 4.0;
                sw.WriteLine($"<line x1='{left - 4}' y1='{y:F1}' x2='{left + plotWidth}' y2='{y:F1}' stroke='#e5e7eb' stroke-width='1' />");
                sw.WriteLine($"<text x='{left - 10}' y='{y + 4:F1}' text-anchor='end' font-size='12'>{value:F0}</text>");
            }
        }

        private static void DrawLegend(StreamWriter sw, int x, int y, IEnumerable<string> labels)
        {
            int i = 0;
            foreach (var label in labels)
            {
                int yy = y + i * 22;
                sw.WriteLine($"<rect x='{x}' y='{yy}' width='14' height='14' fill='{ColorFor(label)}' rx='2' />");
                sw.WriteLine($"<text x='{x + 22}' y='{yy + 12}' font-size='13'>{Escape(label)}</text>");
                i++;
            }
        }

        private static void PrintSummary(IReadOnlyList<AggregateMetric> aggregates, IReadOnlyList<ComparisonMetric> comparisons, string outDir)
        {
            Console.WriteLine("== SUMMARY ==");
            Console.WriteLine("candidate             ok/fail  pallets  height  fill    support  time_ms");
            foreach (var a in aggregates)
            {
                Console.WriteLine(
                    $"{a.Candidate,-21} {a.Ok,2}/{a.Fail,-3} " +
                    $"{a.AvgPallets,7:F2} {a.AvgMaxHeight,7:F0} " +
                    $"{a.AvgFillByUsedHeight,7:P1} {a.AvgSupport,8:P0} {a.AvgTimeMs,8:F0}");
            }

            var okComparisons = comparisons.Where(c => c.BothOk).ToList();
            Console.WriteLine();
            Console.WriteLine($"Both OK comparisons: {okComparisons.Count}");
            foreach (var group in okComparisons.GroupBy(c => c.Candidate, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var list = group.ToList();
                Console.WriteLine($"{group.Key}:");
                Console.WriteLine($"  fewer pallets than GA: {list.Count(c => c.DeltaPallets < 0)}");
                Console.WriteLine($"  more pallets than GA:  {list.Count(c => c.DeltaPallets > 0)}");
                Console.WriteLine($"  avg speedup GA/candidate: {list.Average(c => c.Speedup):F2}x");
                Console.WriteLine($"  avg delta fill: {list.Average(c => c.DeltaFill):P2}");
                Console.WriteLine($"  avg delta empty volume: {list.Average(c => c.DeltaEmptyVolume):F0} mm^3");
            }

            Console.WriteLine();
            Console.WriteLine("Saved:");
            Console.WriteLine($"  {Path.Combine(outDir, "benchmark-orders.csv")}");
            Console.WriteLine($"  {Path.Combine(outDir, "search-runs.csv")}");
            Console.WriteLine($"  {Path.Combine(outDir, "search-comparison.csv")}");
            Console.WriteLine($"  {Path.Combine(outDir, "search-summary.csv")}");
            Console.WriteLine($"  {Path.Combine(outDir, "summary.md")}");
            Console.WriteLine($"  {Path.Combine(outDir, "plots")}");
        }

        private static string ColorFor(string candidate)
        {
            return candidate switch
            {
                "ga" => "#2563eb",
                "multi-start-greedy-bestfit" => "#16a34a",
                _ => "#f97316"
            };
        }

        private static string ShortName(string candidate)
        {
            return candidate switch
            {
                "multi-start-greedy-bestfit" => "bestfit",
                "multi-start-greedy" => "greedy",
                _ => candidate
            };
        }

        private static string SafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        private static string Csv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }

        private static string Escape(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }
    }
}
