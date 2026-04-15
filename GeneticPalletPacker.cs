using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Palletes
{
    public static class GeneticPalletPacker
    {
        private readonly record struct Int3(int X, int Y, int Z);

        private sealed class PackBox
        {
            public string Id { get; }
            public int L { get; }
            public int W { get; }
            public int H { get; }
            public long Volume => (long)L * W * H;

            public PackBox(string id, int l, int w, int h)
            {
                Id = id;
                L = l;
                W = w;
                H = h;
            }
        }

        private readonly record struct PlacedBox(string Id, int x, int y, int z, int X, int Y, int Z)
        {
            public int Dx => X - x;
            public int Dy => Y - y;
            public int Dz => Z - z;
            public long Volume => (long)Dx * Dy * Dz;
        }

        private sealed class Chromosome
        {
            public int[] Order { get; }
            public byte[] Orientation { get; }
            public long Cost { get; set; }

            public Chromosome(int n)
            {
                Order = new int[n];
                Orientation = new byte[n];
            }

            public Chromosome Clone()
            {
                var c = new Chromosome(Order.Length);
                Array.Copy(Order, c.Order, Order.Length);
                Array.Copy(Orientation, c.Orientation, Orientation.Length);
                c.Cost = Cost;
                return c;
            }
        }

        public static void PackCsv(string inPath, string outPath, PalletSpec pallet, int seed = 12345)
        {
            if (pallet.Length <= 0 || pallet.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(pallet), "Pallet base dimensions must be positive.");

            var items = ItemRow.ParseSimple(inPath);
            var boxes = ExpandBoxes(items);

            var placements = Pack(boxes, pallet, seed);

            if (placements.Count != boxes.Count)
                throw new InvalidOperationException($"Could not place all boxes. Placed={placements.Count}, total={boxes.Count}.");

            WritePackingCsv(outPath, pallet, placements);

            // Basic self-check.
            var vr = PackingCsvValidator.ReadPackingCsv(outPath);
            var report = new ValidationReport();
            PackingCsvValidator.ValidateBasic(vr.Boxes, report);
            if (vr.Pallet is not null)
            {
                PackingCsvValidator.ValidateInsidePallet(vr.Boxes, vr.Pallet, report);
            }
            PackingCsvValidator.ValidateNoOverlap(vr.Boxes, report, touchIsOk: true);

            if (report.ErrorCount > 0)
            {
                throw new InvalidOperationException("Internal validation failed:\n" + string.Join("\n", report.Errors.Take(20)));
            }
        }

        public static List<BoxPlacement> Pack(IReadOnlyList<(string Id, int L, int W, int H)> boxes, PalletSpec pallet, int seed = 12345)
        {
            var packBoxes = boxes.Select(b => new PackBox(b.Id, b.L, b.W, b.H)).ToList();
            return Pack(packBoxes, pallet, seed);
        }

        private static List<BoxPlacement> Pack(IReadOnlyList<PackBox> boxes, PalletSpec pallet, int seed)
        {
            int n = boxes.Count;
            if (n == 0) return new List<BoxPlacement>();

            var rng = new Rng(seed);

            int populationSize = Math.Clamp(30 + n / 10, 30, 60);
            int generations = Math.Clamp(80 + n / 3, 80, 200);
            int tournamentK = 3;

            var population = new Chromosome[populationSize];
            for (int i = 0; i < populationSize; i++)
            {
                population[i] = RandomChromosome(n, rng);
                population[i].Cost = EvaluateCost(population[i], boxes, pallet);
            }

            Chromosome best = population.OrderBy(c => c.Cost).First().Clone();
            long bestCost = best.Cost;
            int noImprove = 0;

            for (int gen = 0; gen < generations; gen++)
            {
                var next = new Chromosome[populationSize];
                next[0] = best.Clone();

                for (int i = 1; i < populationSize; i++)
                {
                    var p1 = Tournament(population, rng, tournamentK);
                    var p2 = Tournament(population, rng, tournamentK);

                    var child = Crossover(p1, p2, rng);
                    Mutate(child, rng);

                    child.Cost = EvaluateCost(child, boxes, pallet);
                    next[i] = child;
                }

                population = next;

                var genBest = population.OrderBy(c => c.Cost).First();
                if (genBest.Cost < bestCost)
                {
                    best = genBest.Clone();
                    bestCost = genBest.Cost;
                    noImprove = 0;
                }
                else
                {
                    noImprove++;
                    if (noImprove >= 40)
                        break;
                }
            }

            var placed = Decode(best, boxes, pallet);
            return placed.Select(p => new BoxPlacement
            {
                Id = p.Id,
                x = p.x,
                y = p.y,
                z = p.z,
                X = p.X,
                Y = p.Y,
                Z = p.Z
            }).ToList();
        }

        private static Chromosome RandomChromosome(int n, Rng rng)
        {
            var c = new Chromosome(n);
            for (int i = 0; i < n; i++) c.Order[i] = i;

            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Int(0, i);
                (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
            }

            for (int i = 0; i < n; i++) c.Orientation[i] = (byte)rng.Int(0, 5);
            return c;
        }

        private static Chromosome Tournament(Chromosome[] pop, Rng rng, int k)
        {
            var best = pop[rng.Int(0, pop.Length - 1)];
            for (int i = 1; i < k; i++)
            {
                var c = pop[rng.Int(0, pop.Length - 1)];
                if (c.Cost < best.Cost) best = c;
            }
            return best;
        }

        private static Chromosome Crossover(Chromosome a, Chromosome b, Rng rng)
        {
            int n = a.Order.Length;
            var child = new Chromosome(n);

            // Order crossover (OX)
            int cut1 = rng.Int(0, n - 1);
            int cut2 = rng.Int(0, n - 1);
            if (cut1 > cut2) (cut1, cut2) = (cut2, cut1);

            var used = new bool[n];
            for (int i = cut1; i <= cut2; i++)
            {
                child.Order[i] = a.Order[i];
                used[child.Order[i]] = true;
            }

            int write = (cut2 + 1) % n;
            int read = (cut2 + 1) % n;
            for (int filled = cut2 - cut1 + 1; filled < n;)
            {
                int gene = b.Order[read];
                if (!used[gene])
                {
                    child.Order[write] = gene;
                    used[gene] = true;
                    write = (write + 1) % n;
                    filled++;
                }
                read = (read + 1) % n;
            }

            // Orientation: single point crossover
            int oCut = rng.Int(0, n - 1);
            for (int i = 0; i < n; i++)
            {
                child.Orientation[i] = i < oCut ? a.Orientation[i] : b.Orientation[i];
            }

            return child;
        }

        private static void Mutate(Chromosome c, Rng rng)
        {
            int n = c.Order.Length;

            if (rng.Bool(0.35))
            {
                int i = rng.Int(0, n - 1);
                int j = rng.Int(0, n - 1);
                (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
            }

            if (rng.Bool(0.45))
            {
                int i = rng.Int(0, n - 1);
                c.Orientation[i] = (byte)rng.Int(0, 5);
            }
        }

        private static long EvaluateCost(Chromosome c, IReadOnlyList<PackBox> boxes, PalletSpec pallet)
        {
            var placed = Decode(c, boxes, pallet);

            int placedCount = placed.Count;
            int total = boxes.Count;

            long placedVol = 0;
            int height = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                placedVol += placed[i].Volume;
                if (placed[i].Z > height) height = placed[i].Z;
            }

            long baseArea = (long)pallet.Length * pallet.Width;
            long emptyVolume = baseArea * height - placedVol;

            int penaltyHeight = pallet.MaxHeight > 0 ? pallet.MaxHeight : 2000;
            long unplacedPenalty = (long)(total - placedCount) * (baseArea * penaltyHeight + 1);
            return unplacedPenalty + Math.Max(0, emptyVolume);
        }

        private static List<PlacedBox> Decode(Chromosome c, IReadOnlyList<PackBox> boxes, PalletSpec pallet)
        {
            var placed = new List<PlacedBox>(boxes.Count);

            var points = new List<Int3>(1 + boxes.Count * 7) { new Int3(0, 0, 0) };
            var pointsSet = new HashSet<Int3> { new Int3(0, 0, 0) };

            for (int pos = 0; pos < c.Order.Length; pos++)
            {
                var box = boxes[c.Order[pos]];
                byte oriGene = c.Orientation[c.Order[pos]];

                points.Sort(static (a, b) =>
                {
                    int z = a.Z.CompareTo(b.Z);
                    if (z != 0) return z;
                    int y = a.Y.CompareTo(b.Y);
                    if (y != 0) return y;
                    return a.X.CompareTo(b.X);
                });

                bool done = false;

                for (int pi = 0; pi < points.Count && !done; pi++)
                {
                    var p = points[pi];

                    for (int t = 0; t < 6; t++)
                    {
                        byte ori = (byte)((oriGene + t) % 6);
                        var (L, W, H) = OrientedDims(box, ori);

                        if (!Fits(p, L, W, H, placed, pallet))
                            continue;

                        int x = p.X;
                        int y = p.Y;
                        int z = p.Z;
                        var pb = new PlacedBox(box.Id, x, y, z, x + L, y + W, z + H);
                        placed.Add(pb);

                        // consume point
                        pointsSet.Remove(p);
                        points.RemoveAt(pi);

                        AddPoints(points, pointsSet, pallet, pb);
                        done = true;
                        break;
                    }
                }
            }

            return placed;
        }

        private static (int L, int W, int H) OrientedDims(PackBox b, byte ori)
        {
            int l = b.L, w = b.W, h = b.H;
            return ori switch
            {
                0 => (l, w, h),
                1 => (l, h, w),
                2 => (w, l, h),
                3 => (w, h, l),
                4 => (h, l, w),
                5 => (h, w, l),
                _ => (l, w, h)
            };
        }

        private static bool Fits(Int3 p, int L, int W, int H, List<PlacedBox> placed, PalletSpec pallet)
        {
            if (L <= 0 || W <= 0 || H <= 0) return false;

            int x = p.X, y = p.Y, z = p.Z;
            int X = x + L, Y = y + W, Z = z + H;

            if (x < 0 || y < 0 || z < 0) return false;
            if (X > pallet.Length || Y > pallet.Width) return false;
            if (pallet.MaxHeight > 0 && Z > pallet.MaxHeight) return false;

            for (int i = 0; i < placed.Count; i++)
            {
                var b = placed[i];
                bool separated = (X <= b.x) || (b.X <= x) || (Y <= b.y) || (b.Y <= y) || (Z <= b.z) || (b.Z <= z);
                if (!separated) return false;
            }

            if (z == 0) return true;

            // Relaxed support check: require that placement point touches the top face of some previously placed box.
            return CornerSupported(x, y, z, placed);
        }

        private static bool CornerSupported(int cx, int cy, int z, List<PlacedBox> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                var b = placed[i];
                if (b.Z != z) continue;
                if (cx >= b.x && cx <= b.X && cy >= b.y && cy <= b.Y)
                    return true;
            }
            return false;
        }

        private static void AddPoints(List<Int3> points, HashSet<Int3> pointsSet, PalletSpec pallet, PlacedBox b)
        {
            AddPoint(points, pointsSet, pallet, b.X, b.y, b.z);
            AddPoint(points, pointsSet, pallet, b.x, b.Y, b.z);
            AddPoint(points, pointsSet, pallet, b.x, b.y, b.Z);
            AddPoint(points, pointsSet, pallet, b.X, b.Y, b.z);
            AddPoint(points, pointsSet, pallet, b.X, b.y, b.Z);
            AddPoint(points, pointsSet, pallet, b.x, b.Y, b.Z);
            AddPoint(points, pointsSet, pallet, b.X, b.Y, b.Z);
        }

        private static void AddPoint(List<Int3> points, HashSet<Int3> pointsSet, PalletSpec pallet, int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0) return;
            if (x > pallet.Length || y > pallet.Width) return;
            if (pallet.MaxHeight > 0 && z > pallet.MaxHeight) return;

            var p = new Int3(x, y, z);
            if (pointsSet.Add(p)) points.Add(p);
        }

        private static List<(string Id, int L, int W, int H)> ExpandBoxes(List<ItemRow> items)
        {
            var boxes = new List<(string Id, int L, int W, int H)>();

            foreach (var it in items)
            {
                if (it.Quantity <= 0) continue;

                for (int k = 1; k <= it.Quantity; k++)
                {
                    string id = it.Quantity == 1 ? it.SKU.ToString(CultureInfo.InvariantCulture) : $"{it.SKU}_{k}";
                    boxes.Add((id, it.Length, it.Width, it.Height));
                }
            }

            return boxes;
        }

        private static void WritePackingCsv(string path, PalletSpec pallet, IReadOnlyList<BoxPlacement> placements)
        {
            long usedHeight = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var z = (long)Math.Round(placements[i].Z);
                if (z > usedHeight) usedHeight = z;
            }

            long hToWrite = pallet.MaxHeight > 0 ? pallet.MaxHeight : usedHeight;

            using var sw = new StreamWriter(path);
            sw.WriteLine(string.Join(",",
                "PALLET",
                pallet.PalletType,
                "0",
                "0",
                "0",
                pallet.Length.ToString(CultureInfo.InvariantCulture),
                pallet.Width.ToString(CultureInfo.InvariantCulture),
                hToWrite.ToString(CultureInfo.InvariantCulture)));

            sw.WriteLine("ID,x,y,z,X,Y,Z");

            foreach (var b in placements)
            {
                sw.WriteLine(string.Join(",",
                    b.Id,
                    b.x.ToString(CultureInfo.InvariantCulture),
                    b.y.ToString(CultureInfo.InvariantCulture),
                    b.z.ToString(CultureInfo.InvariantCulture),
                    b.X.ToString(CultureInfo.InvariantCulture),
                    b.Y.ToString(CultureInfo.InvariantCulture),
                    b.Z.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }
}
