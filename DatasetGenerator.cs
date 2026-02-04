using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Palletes
{
    public sealed class DatasetGenerator
    {
        private readonly Profile _profile;
        private readonly PalletSpec _pallet;
        private readonly Rng _rng;

        private int _orderIdSeq = 0;
        private int _globalSkuSeq = 700000;

        public DatasetGenerator(Profile profile, PalletSpec pallet, Rng rng)
        {
            _profile = profile;
            _pallet = pallet;
            _rng = rng;
        }

        public void GenerateAll(string outDir)
        {
            GenerateGroup1(Path.Combine(outDir, "group1"));
            GenerateGroup2(Path.Combine(outDir, "group2"));
            GenerateGroup3(Path.Combine(outDir, "group3"));
            GenerateGroup4(Path.Combine(outDir, "group4"));
        }

        private void GenerateGroup1(string dir)
        {
            Directory.CreateDirectory(dir);

            var groupStopwatch = Stopwatch.StartNew();
            int totalBoxes = 0;
            int ordersCount = 0;

            for (int N = 10; N <= 100; N += 10)
            {
                for (int rep = 0; rep < 5; rep++)
                {
                    var sw = Stopwatch.StartNew();

                    int orderId = NextOrderId();
                    int K = SampleKGivenN(N);
                    var qty = SampleQtyPerSkuSumToN(K, N);
                    int numAisles = _rng.Int(1, Math.Min(6, K));

                    var lines = new List<SkuLine>(K);
                    for (int i = 0; i < K; i++)
                    {
                        int sku = NextSku();
                        var (L, W, H) = SampleDims();
                        int weightG = EstimateWeightGrams(L, W, H);
                        int strength = _rng.Int(1, 5);
                        int aisle = _rng.Int(1, numAisles);
                        int caustic = _rng.Bool(0.03) ? 1 : 0;

                        lines.Add(new SkuLine
                        {
                            SKU = sku,
                            Quantity = qty[i],
                            Length = L,
                            Width = W,
                            Height = H,
                            Weight = weightG,
                            Strength = strength,
                            Aisle = aisle,
                            Caustic = caustic
                        });
                    }

                    sw.Stop();
                    int boxCount = lines.Sum(l => l.Quantity);
                    totalBoxes += boxCount;
                    ordersCount++;

                    WriteOrder(dir, orderId, group: "group1", scenario: $"realistic_fixedN_{N}", lines,
                        notes: "Realistic-like orders, fixed total boxes N to test scaling.",
                        generationTimeUs: sw.Elapsed.TotalMilliseconds * 1000);
                }
            }

            groupStopwatch.Stop();
            Console.WriteLine($"Group 1 completed: {ordersCount} orders, {totalBoxes} boxes, {groupStopwatch.ElapsedMilliseconds} ms");
        }

        private void GenerateGroup2(string dir)
        {
            Directory.CreateDirectory(dir);

            var groupStopwatch = Stopwatch.StartNew();
            int totalBoxes = 0;
            int ordersCount = 0;

            var scenarios = new List<(string name, int[] qtyPerSku)>
            {
                ("single_sku_20", new[] { 20 }),
                ("single_sku_60", new[] { 60 }),
                ("five_skus_20_each", new[] { 20,20,20,20,20 }),
                ("pattern_20_10_10_10_10", new[] { 20,10,10,10,10 }),
                ("pattern_30_5_5_5_5", new[] { 30,5,5,5,5 }),
                ("twenty_skus_5_each", Enumerable.Repeat(5, 20).ToArray()),
                ("hundred_boxes_all_unique_sku", Enumerable.Repeat(1, 100).ToArray())
            };

            foreach (var sc in scenarios)
            {
                for (int rep = 0; rep < 3; rep++)
                {
                    var sw = Stopwatch.StartNew();

                    int orderId = NextOrderId();
                    int K = sc.qtyPerSku.Length;
                    int numAisles = _rng.Int(1, Math.Min(8, K));

                    var lines = new List<SkuLine>(K);
                    for (int i = 0; i < K; i++)
                    {
                        int sku = NextSku();

                        var (L, W, H) = SampleDims(diversifyKey: sc.name.Contains("all_unique") ? i : (int?)null);

                        lines.Add(new SkuLine
                        {
                            SKU = sku,
                            Quantity = sc.qtyPerSku[i],
                            Length = L,
                            Width = W,
                            Height = H,
                            Weight = EstimateWeightGrams(L, W, H),
                            Strength = _rng.Int(1, 5),
                            Aisle = _rng.Int(1, numAisles),
                            Caustic = _rng.Bool(0.03) ? 1 : 0
                        });
                    }

                    sw.Stop();
                    int boxCount = lines.Sum(l => l.Quantity);
                    totalBoxes += boxCount;
                    ordersCount++;

                    WriteOrder(dir, orderId, "group2", sc.name, lines,
                        notes: "Special qty-per-SKU stress cases.",
                        generationTimeUs: sw.Elapsed.TotalMilliseconds * 1000);
                }
            }

            for (int rep = 0; rep < 3; rep++)
            {
                var sw = Stopwatch.StartNew();

                int orderId = NextOrderId();

                var qty = new List<int> { 20, 20 };
                int tail = _rng.Int(15, 45);
                for (int i = 0; i < tail; i++) qty.Add(_rng.Bool(0.7) ? 1 : 2);

                int K = qty.Count;
                int numAisles = _rng.Int(1, Math.Min(8, K));

                var lines = new List<SkuLine>(K);
                for (int i = 0; i < K; i++)
                {
                    int sku = NextSku();
                    var (L, W, H) = SampleDims();

                    lines.Add(new SkuLine
                    {
                        SKU = sku,
                        Quantity = qty[i],
                        Length = L,
                        Width = W,
                        Height = H,
                        Weight = EstimateWeightGrams(L, W, H),
                        Strength = _rng.Int(1, 5),
                        Aisle = _rng.Int(1, numAisles),
                        Caustic = _rng.Bool(0.03) ? 1 : 0
                    });
                }

                sw.Stop();
                int boxCount = lines.Sum(l => l.Quantity);
                totalBoxes += boxCount;
                ordersCount++;

                WriteOrder(dir, orderId, "group2", "many_skus_1to2_plus_two_big", lines,
                    notes: "Two very large SKUs + many small tail SKUs (1-2 boxes).",
                    generationTimeUs: sw.Elapsed.TotalMilliseconds * 1000);
            }

            groupStopwatch.Stop();
            Console.WriteLine($"Group 2 completed: {ordersCount} orders, {totalBoxes} boxes, {groupStopwatch.ElapsedMilliseconds} ms");
        }

        private void GenerateGroup3(string dir)
        {
            Directory.CreateDirectory(dir);

            var groupStopwatch = Stopwatch.StartNew();
            int totalBoxes = 0;
            int ordersCount = 0;

            int[] Ns = { 30, 60, 100 };

            foreach (int N in Ns)
            {
                var (boxes, orders) = GenerateGeometryScenario(dir, N, "all_large", sampler: SampleLargeDims);
                totalBoxes += boxes;
                ordersCount += orders;

                (boxes, orders) = GenerateGeometryScenario(dir, N, "all_small", sampler: SampleSmallDims);
                totalBoxes += boxes;
                ordersCount += orders;

                (boxes, orders) = GenerateGeometryScenario(dir, N, "half_large_half_small", sampler: () => _rng.Bool(0.5) ? SampleLargeDims() : SampleSmallDims());
                totalBoxes += boxes;
                ordersCount += orders;

                (boxes, orders) = GenerateGeometryScenario(dir, N, "wide_spectrum", sampler: () => SampleDims());
                totalBoxes += boxes;
                ordersCount += orders;

                (boxes, orders) = GenerateGeometryScenario(dir, N, "shape_mix", sampler: SampleShapeMixDims);
                totalBoxes += boxes;
                ordersCount += orders;
            }

            groupStopwatch.Stop();
            Console.WriteLine($"Group 3 completed: {ordersCount} orders, {totalBoxes} boxes, {groupStopwatch.ElapsedMilliseconds} ms");
        }

        private (int totalBoxes, int ordersCount) GenerateGeometryScenario(string dir, int N, string scenario, Func<(int L, int W, int H)> sampler)
        {
            int totalBoxes = 0;
            int ordersCount = 0;

            for (int rep = 0; rep < 3; rep++)
            {
                var sw = Stopwatch.StartNew();

                int orderId = NextOrderId();
                int K = SampleKGivenN(N);
                var qty = SampleQtyPerSkuSumToN(K, N);
                int numAisles = _rng.Int(1, Math.Min(6, K));

                var lines = new List<SkuLine>(K);
                for (int i = 0; i < K; i++)
                {
                    int sku = NextSku();
                    var (L, W, H) = sampler();

                    lines.Add(new SkuLine
                    {
                        SKU = sku,
                        Quantity = qty[i],
                        Length = L,
                        Width = W,
                        Height = H,
                        Weight = EstimateWeightGrams(L, W, H),
                        Strength = _rng.Int(1, 5),
                        Aisle = _rng.Int(1, numAisles),
                        Caustic = _rng.Bool(0.03) ? 1 : 0
                    });
                }

                sw.Stop();
                int boxCount = lines.Sum(l => l.Quantity);
                totalBoxes += boxCount;
                ordersCount++;

                WriteOrder(dir, orderId, "group3", $"{scenario}_N{N}", lines,
                    notes: "Geometry stress: large/small/mixed/shape patterns.",
                    generationTimeUs: sw.Elapsed.TotalMilliseconds * 1000);
            }

            return (totalBoxes, ordersCount);
        }

        private void GenerateGroup4(string dir)
        {
            Directory.CreateDirectory(dir);

            var groupStopwatch = Stopwatch.StartNew();
            int totalBoxes = 0;
            int ordersCount = 0;

            for (int targetPallets = 2; targetPallets <= 10; targetPallets++)
            {
                for (int rep = 0; rep < 2; rep++)
                {
                    var sw = Stopwatch.StartNew();

                    int orderId = NextOrderId();
                    int categories = _rng.Int(6, 20);

                    string scenario = _rng.Bool(0.5) ? "one_big_many_small" : "balanced_categories";
                    var shares = scenario == "one_big_many_small"
                        ? MakeSharesOneBigManySmall(categories)
                        : MakeSharesBalanced(categories);

                    double palletVolM3 = (_pallet.Length / 1000.0) * (_pallet.Width / 1000.0) * (_pallet.MaxHeight / 1000.0);
                    double utilization = _rng.Double(0.55, 0.85);
                    double targetVolM3 = targetPallets * utilization * palletVolM3;

                    var lines = new List<SkuLine>();
                    for (int cat = 1; cat <= categories; cat++)
                    {
                        double catTargetVol = shares[cat - 1] * targetVolM3;
                        double catVol = 0;

                        int skusInCat = _rng.Int(2, 10);
                        for (int s = 0; s < skusInCat && catVol < catTargetVol; s++)
                        {
                            int sku = NextSku();
                            var (L, W, H) = SampleDims(diversifyKey: cat * 1000 + s);
                            int qty = _rng.Int(1, shares[cat - 1] > 0.15 ? 35 : 12);

                            double boxVol = (L / 1000.0) * (W / 1000.0) * (H / 1000.0);
                            catVol += boxVol * qty;

                            lines.Add(new SkuLine
                            {
                                SKU = sku,
                                Quantity = qty,
                                Length = L,
                                Width = W,
                                Height = H,
                                Weight = EstimateWeightGrams(L, W, H),
                                Strength = _rng.Int(1, 5),
                                Aisle = cat,
                                Caustic = _rng.Bool(0.03) ? 1 : 0
                            });
                        }
                    }

                    sw.Stop();
                    int boxCount = lines.Sum(l => l.Quantity);
                    totalBoxes += boxCount;
                    ordersCount++;

                    WriteOrder(dir, orderId, "group4", $"{scenario}_P{targetPallets}", lines,
                        notes: $"Multi-pallet + categories. Target pallets ~{targetPallets}.",
                        targetPallets: targetPallets,
                        generationTimeUs: sw.Elapsed.TotalMilliseconds * 1000);
                }
            }

            groupStopwatch.Stop();
            Console.WriteLine($"Group 4 completed: {ordersCount} orders, {totalBoxes} boxes, {groupStopwatch.ElapsedMilliseconds} ms");
        }

        private void WriteOrder(
            string groupDir,
            int orderId,
            string group,
            string scenario,
            List<SkuLine> lines,
            string notes,
            int? targetPallets = null,
            double? generationTimeUs = null)
        {
            string orderFolder = Path.Combine(groupDir, orderId.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(orderFolder);

            string csvPath = Path.Combine(orderFolder, $"{orderId}.csv");

            using (var sw = new StreamWriter(csvPath))
            {
                sw.WriteLine(orderId.ToString(CultureInfo.InvariantCulture));

                sw.WriteLine("SKU,Quantity,Length,Width,Height,Weight,Strength,Aisle,Caustic");

                foreach (var l in lines)
                {
                    sw.WriteLine(string.Join(",",
                        l.SKU.ToString(CultureInfo.InvariantCulture),
                        l.Quantity.ToString(CultureInfo.InvariantCulture),
                        l.Length.ToString(CultureInfo.InvariantCulture),
                        l.Width.ToString(CultureInfo.InvariantCulture),
                        l.Height.ToString(CultureInfo.InvariantCulture),
                        l.Weight.ToString(CultureInfo.InvariantCulture),
                        l.Strength.ToString(CultureInfo.InvariantCulture),
                        l.Aisle.ToString(CultureInfo.InvariantCulture),
                        l.Caustic.ToString(CultureInfo.InvariantCulture),
                        ""
                    ));
                }
            }

            string metaPath = Path.Combine(orderFolder, "meta.txt");
            int totalBoxes = lines.Sum(x => x.Quantity);
            int totalSkus = lines.Count;
            int totalAisles = lines.Select(x => x.Aisle).Distinct().Count();

            var metaLines = new List<string>
            {
                $"order_id={orderId}",
                $"group={group}",
                $"scenario={scenario}",
                $"total_boxes={totalBoxes}",
                $"total_skus={totalSkus}",
                $"total_aisles={totalAisles}",
                targetPallets.HasValue ? $"target_pallets={targetPallets.Value}" : "target_pallets=",
                $"pallet_type={_pallet.PalletType}",
                $"pallet_length_mm={_pallet.Length}",
                $"pallet_width_mm={_pallet.Width}",
                $"pallet_max_height_mm={_pallet.MaxHeight}",
                $"notes={notes}"
            };

            if (generationTimeUs.HasValue)
            {
                metaLines.Add($"generation_time_us={generationTimeUs.Value:F3}");
            }

            File.WriteAllLines(metaPath, metaLines);
        }


        private int NextOrderId() => ++_orderIdSeq;
        private int NextSku() => ++_globalSkuSeq;

        private int SampleKGivenN(int N)
        {
            int k = (int)Math.Round(N * _profile.KSlope + _profile.KBias + _rng.Normal(0, _profile.KNoiseStd));
            return Math.Max(1, Math.Min(N, k));
        }

        private int[] SampleQtyPerSkuSumToN(int K, int N)
        {
            var qty = Enumerable.Repeat(1, K).ToArray();
            int remaining = N - K;
            if (remaining <= 0) return qty;

            double alpha = 1.2;
            var idx = Enumerable.Range(0, K).OrderBy(_ => _rng.Double()).ToArray();
            var w = new double[K];
            for (int i = 0; i < K; i++) w[idx[i]] = 1.0 / Math.Pow(i + 1, alpha);

            for (int r = 0; r < remaining; r++)
            {
                int pick = WeightedIndex(w);
                qty[pick]++;
            }
            return qty;
        }

        private int WeightedIndex(double[] weights)
        {
            double sum = 0;
            for (int i = 0; i < weights.Length; i++) sum += Math.Max(0, weights[i]);
            if (sum <= 0) return _rng.Int(0, weights.Length - 1);

            double x = _rng.Double() * sum;
            double acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += Math.Max(0, weights[i]);
                if (acc >= x) return i;
            }
            return weights.Length - 1;
        }

        private (int L, int W, int H) SampleDims(int? diversifyKey = null)
        {
            var fam = _rng.WeightedPick(_profile.Families, f => f.Weight);

            int l = _rng.Int(fam.L.Min, fam.L.Max);
            int w = _rng.Int(fam.W.Min, fam.W.Max);
            int h = _rng.Int(fam.H.Min, fam.H.Max);

            if (fam.Shape == ShapeHint.CubeLike)
            {
                int baseSide = (l + w) / 2;
                l = Clamp(baseSide + _rng.Int(-30, 30), fam.L.Min, fam.L.Max);
                w = Clamp(baseSide + _rng.Int(-30, 30), fam.W.Min, fam.W.Max);
            }

            if (diversifyKey.HasValue)
            {
                int noise = (diversifyKey.Value % 7) - 3; // -3..+3
                l = Clamp(l + noise * 7, 50, 2000);
                w = Clamp(w - noise * 5, 50, 2000);
                h = Clamp(h + noise * 3, 20, 2000);
            }

            if (w > l) (l, w) = (w, l);
            return (l, w, h);
        }

        private (int L, int W, int H) SampleLargeDims()
        {
            int l = _rng.Int(450, 900);
            int w = _rng.Int(300, 650);
            int h = _rng.Int(200, 650);
            if (w > l) (l, w) = (w, l);
            return (l, w, h);
        }

        private (int L, int W, int H) SampleSmallDims()
        {
            int l = _rng.Int(80, 220);
            int w = _rng.Int(80, 200);
            int h = _rng.Int(50, 180);
            if (w > l) (l, w) = (w, l);
            return (l, w, h);
        }

        private (int L, int W, int H) SampleShapeMixDims()
        {
            double r = _rng.Double();
            if (r < 0.35)
            {
                int s = _rng.Int(150, 320);
                return (s, _rng.Int(Math.Max(80, s - 40), s + 40), _rng.Int(100, 280));
            }
            if (r < 0.55)
            {
                return (_rng.Int(450, 1000), _rng.Int(80, 180), _rng.Int(80, 250)); // elongated slim
            }
            if (r < 0.75)
            {
                int l = _rng.Int(350, 950);
                int w = _rng.Int(250, 800);
                int h = _rng.Int(25, 90);
                if (w > l) (l, w) = (w, l);
                return (l, w, h);
            }
            else
            {
                int l = _rng.Int(180, 450);
                int w = _rng.Int(180, 450);
                int h = _rng.Int(500, 1100);
                if (w > l) (l, w) = (w, l);
                return (l, w, h);
            }
        }

        private int EstimateWeightGrams(int Lmm, int Wmm, int Hmm)
        {
            double volM3 = (Lmm / 1000.0) * (Wmm / 1000.0) * (Hmm / 1000.0);
            double densityKgM3 = 180.0;
            double kg = densityKgM3 * volM3 + _rng.Normal(0, 0.4);
            kg = Math.Max(0.05, kg);
            return (int)Math.Round(kg * 1000.0);
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private double[] MakeSharesBalanced(int cats)
        {
            var x = new double[cats];
            for (int i = 0; i < cats; i++) x[i] = 1.0 + _rng.Double(-0.2, 0.2);
            Normalize(x);
            return x;
        }

        private double[] MakeSharesOneBigManySmall(int cats)
        {
            var x = new double[cats];
            double big = _rng.Double(0.40, 0.70);
            x[0] = big;
            double rest = 1.0 - big;

            double sum = 0;
            for (int i = 1; i < cats; i++)
            {
                x[i] = _rng.Double(0.1, 1.0);
                sum += x[i];
            }
            for (int i = 1; i < cats; i++) x[i] = x[i] / sum * rest;

            for (int i = cats - 1; i > 0; i--)
            {
                int j = _rng.Int(0, i);
                (x[i], x[j]) = (x[j], x[i]);
            }
            return x;
        }

        private static void Normalize(double[] x)
        {
            double s = x.Sum();
            if (s <= 0) return;
            for (int i = 0; i < x.Length; i++) x[i] /= s;
        }
    }
}
