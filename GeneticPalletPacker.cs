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
            public string SizeKey { get; }

            public PackBox(string id, int l, int w, int h)
            {
                Id = id;
                L = l;
                W = w;
                H = h;

                var dims = new[] { l, w, h };
                Array.Sort(dims);
                SizeKey = string.Join("x", dims);
            }
        }

        private readonly record struct PlacedBox(PackBox Source, string Id, int x, int y, int z, int X, int Y, int Z)
        {
            public int Dx => X - x;
            public int Dy => Y - y;
            public int Dz => Z - z;
            public long Volume => (long)Dx * Dy * Dz;
            public string SizeKey => Source.SizeKey;
        }

        private sealed class PackedPallet
        {
            public string PalletId { get; }
            public List<PlacedBox> Boxes { get; }
            public string ContainerId { get; set; } = "";
            public int OriginX { get; set; }
            public int OriginY { get; set; }
            public int OriginZ { get; set; }
            public bool RotatedOnFloor { get; set; }
            public int FootprintLength { get; set; }
            public int FootprintWidth { get; set; }

            public PackedPallet(string palletId, List<PlacedBox> boxes)
            {
                PalletId = palletId;
                Boxes = boxes;
            }
        }

        private sealed class PackedContainer
        {
            public string ContainerId { get; }
            public List<PackedPallet> Pallets { get; } = new();

            public PackedContainer(string containerId)
            {
                ContainerId = containerId;
            }
        }

        private readonly record struct Rect2(int X1, int Y1, int X2, int Y2);

        private readonly record struct DecodeMetrics(
            int PlacedCount,
            int Height,
            long PlacedVolume,
            long BoundingVolume,
            int SameSizeTouchingCount);

        private sealed class Chromosome
        {
            public int[] Order { get; }
            public byte[] Orientation { get; }
            public double Fitness { get; set; }
            public int PlacedCount { get; set; }
            public int Height { get; set; }
            public long EmptyVolume { get; set; }

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
                c.Fitness = Fitness;
                c.PlacedCount = PlacedCount;
                c.Height = Height;
                c.EmptyVolume = EmptyVolume;
                return c;
            }
        }

        public static void PackCsv(string inPath, string outPath, PalletSpec pallet, int seed = 12345)
            => PackCsv(inPath, outPath, pallet, container: null, seed);

        public static void PackCsv(string inPath, string outPath, PalletSpec pallet, ContainerSpec? container, int seed = 12345)
        {
            if (pallet.Length <= 0 || pallet.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(pallet), "Pallet base dimensions must be positive.");

            var items = ItemRow.ParseSimple(inPath);
            var boxes = ExpandBoxes(items).Select(b => new PackBox(b.Id, b.L, b.W, b.H)).ToList();
            var effectivePallet = NormalizePallet(pallet);

            var packedPallets = PackAcrossPallets(boxes, effectivePallet, seed);
            List<PackedContainer> packedContainers;
            if (container is not null)
            {
                var effectiveContainer = NormalizeContainer(container);
                packedContainers = PlacePalletsInContainers(packedPallets, effectivePallet, effectiveContainer);
            }
            else
            {
                packedContainers = PlacePalletsInVirtualContainers(packedPallets, effectivePallet);
            }

            int placedCount = packedPallets.Sum(p => p.Boxes.Count);

            if (placedCount != boxes.Count)
                throw new InvalidOperationException($"Could not place all boxes. Placed={placedCount}, total={boxes.Count}.");

            WritePackingCsv(outPath, effectivePallet, container, packedContainers);

            var vr = PackingCsvValidator.ReadPackingCsv(outPath);
            var report = new ValidationReport();
            PackingCsvValidator.ValidateBasic(vr.Boxes, report);
            PackingCsvValidator.ValidatePalletsInsideContainers(vr.Pallets, vr.Containers, report);
            if (vr.Pallets.Count > 0)
            {
                PackingCsvValidator.ValidateInsidePallet(vr.Boxes, vr.Pallets, report);
            }
            else if (vr.Pallet is not null)
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
            var effectivePallet = NormalizePallet(pallet);
            var placed = PackSinglePallet(packBoxes, effectivePallet, seed);

            if (placed.Count != packBoxes.Count)
                throw new InvalidOperationException($"Could not place all boxes on a single pallet. Placed={placed.Count}, total={packBoxes.Count}.");

            return placed.Select(p => new BoxPlacement
            {
                Id = p.Id,
                PalletId = effectivePallet.PalletType,
                x = p.x,
                y = p.y,
                z = p.z,
                X = p.X,
                Y = p.Y,
                Z = p.Z
            }).ToList();
        }

        private static PalletSpec NormalizePallet(PalletSpec pallet)
        {
            return new PalletSpec
            {
                PalletType = pallet.PalletType,
                Length = pallet.Length,
                Width = pallet.Width,
                MaxHeight = pallet.MaxHeight > 0 ? pallet.MaxHeight : 2000
            };
        }

        private static ContainerSpec NormalizeContainer(ContainerSpec container)
        {
            return new ContainerSpec
            {
                ContainerType = container.ContainerType,
                Length = container.Length,
                Width = container.Width,
                Height = container.Height
            };
        }

        private static List<PackedPallet> PackAcrossPallets(IReadOnlyList<PackBox> boxes, PalletSpec pallet, int seed)
        {
            var remaining = boxes
                .OrderByDescending(b => b.Volume)
                .ThenByDescending(b => Math.Max(b.L, Math.Max(b.W, b.H)))
                .ToList();

            var pallets = new List<PackedPallet>();
            int palletIndex = 1;

            while (remaining.Count > 0)
            {
                var placed = PackSinglePallet(remaining, pallet, seed + palletIndex * 104729);
                if (placed.Count == 0)
                {
                    throw new InvalidOperationException($"Could not place any of the remaining {remaining.Count} boxes on pallet {palletIndex}.");
                }

                string palletId = $"{pallet.PalletType}_{palletIndex}";
                pallets.Add(new PackedPallet(palletId, placed));

                var usedIds = new HashSet<string>(placed.Select(p => p.Id), StringComparer.Ordinal);
                remaining = remaining.Where(b => !usedIds.Contains(b.Id)).ToList();
                palletIndex++;
            }

            return pallets;
        }

        private static List<PackedContainer> PlacePalletsInVirtualContainers(IReadOnlyList<PackedPallet> pallets, PalletSpec pallet)
        {
            var result = new List<PackedContainer>();
            for (int i = 0; i < pallets.Count; i++)
            {
                var container = new PackedContainer($"VIRTUAL_{i + 1}");
                pallets[i].ContainerId = container.ContainerId;
                pallets[i].OriginX = 0;
                pallets[i].OriginY = 0;
                pallets[i].OriginZ = 0;
                pallets[i].RotatedOnFloor = false;
                pallets[i].FootprintLength = pallet.Length;
                pallets[i].FootprintWidth = pallet.Width;
                container.Pallets.Add(pallets[i]);
                result.Add(container);
            }

            return result;
        }

        private static List<PackedContainer> PlacePalletsInContainers(
            IReadOnlyList<PackedPallet> pallets,
            PalletSpec pallet,
            ContainerSpec containerSpec)
        {
            var result = new List<PackedContainer>();
            var order = pallets
                .OrderByDescending(p => p.Boxes.Count)
                .ThenBy(p => p.PalletId, StringComparer.Ordinal)
                .ToList();

            for (int i = 0; i < order.Count; i++)
            {
                var palletToPlace = order[i];
                bool placed = false;

                for (int c = 0; c < result.Count && !placed; c++)
                {
                    if (TryPlacePalletInContainer(result[c], palletToPlace, pallet, containerSpec))
                    {
                        placed = true;
                    }
                }

                if (!placed)
                {
                    var container = new PackedContainer($"{containerSpec.ContainerType}_{result.Count + 1}");
                    if (!TryPlacePalletInContainer(container, palletToPlace, pallet, containerSpec))
                    {
                        throw new InvalidOperationException($"Pallet '{palletToPlace.PalletId}' does not fit into container '{containerSpec.ContainerType}'.");
                    }

                    result.Add(container);
                }
            }

            return result;
        }

        private static bool TryPlacePalletInContainer(
            PackedContainer container,
            PackedPallet palletToPlace,
            PalletSpec pallet,
            ContainerSpec containerSpec)
        {
            var points = new List<(int X, int Y)> { (0, 0) };
            var seen = new HashSet<(int X, int Y)> { (0, 0) };

            for (int i = 0; i < container.Pallets.Count; i++)
            {
                var p = container.Pallets[i];
                Add2DPoint(points, seen, p.OriginX + p.FootprintLength, p.OriginY, containerSpec);
                Add2DPoint(points, seen, p.OriginX, p.OriginY + p.FootprintWidth, containerSpec);
            }

            points.Sort(static (a, b) =>
            {
                int y = a.Y.CompareTo(b.Y);
                if (y != 0) return y;
                return a.X.CompareTo(b.X);
            });

            foreach (var point in points)
            {
                bool canNormal = CanPlaceAt(container, pallet, containerSpec, point.X, point.Y, rotated: false);
                bool canRotated = CanPlaceAt(container, pallet, containerSpec, point.X, point.Y, rotated: true);

                if (!canNormal && !canRotated)
                    continue;

                bool useRotated;
                if (canNormal && canRotated)
                {
                    useRotated = pallet.Width < pallet.Length;
                }
                else
                {
                    useRotated = canRotated;
                }

                if (TryPlaceAt(container, palletToPlace, pallet, containerSpec, point.X, point.Y, useRotated))
                    return true;
            }

            return false;
        }

        private static bool CanPlaceAt(
            PackedContainer container,
            PalletSpec pallet,
            ContainerSpec containerSpec,
            int originX,
            int originY,
            bool rotated)
        {
            int footprintLength = rotated ? pallet.Width : pallet.Length;
            int footprintWidth = rotated ? pallet.Length : pallet.Width;

            if (originX < 0 || originY < 0) return false;
            if (originX + footprintLength > containerSpec.Length || originY + footprintWidth > containerSpec.Width)
                return false;
            if (pallet.MaxHeight > containerSpec.Height)
                return false;

            var candidate = new Rect2(originX, originY, originX + footprintLength, originY + footprintWidth);
            for (int i = 0; i < container.Pallets.Count; i++)
            {
                var other = container.Pallets[i];
                var otherRect = new Rect2(other.OriginX, other.OriginY, other.OriginX + other.FootprintLength, other.OriginY + other.FootprintWidth);
                bool separated =
                    candidate.X2 <= otherRect.X1 || otherRect.X2 <= candidate.X1 ||
                    candidate.Y2 <= otherRect.Y1 || otherRect.Y2 <= candidate.Y1;
                if (!separated) return false;
            }

            return true;
        }

        private static bool TryPlaceAt(
            PackedContainer container,
            PackedPallet palletToPlace,
            PalletSpec pallet,
            ContainerSpec containerSpec,
            int originX,
            int originY,
            bool rotated)
        {
            int footprintLength = rotated ? pallet.Width : pallet.Length;
            int footprintWidth = rotated ? pallet.Length : pallet.Width;

            if (!CanPlaceAt(container, pallet, containerSpec, originX, originY, rotated))
                return false;

            palletToPlace.ContainerId = container.ContainerId;
            palletToPlace.OriginX = originX;
            palletToPlace.OriginY = originY;
            palletToPlace.OriginZ = 0;
            palletToPlace.RotatedOnFloor = rotated;
            palletToPlace.FootprintLength = footprintLength;
            palletToPlace.FootprintWidth = footprintWidth;

            if (!container.Pallets.Contains(palletToPlace))
            {
                container.Pallets.Add(palletToPlace);
            }

            return true;
        }

        private static void Add2DPoint(List<(int X, int Y)> points, HashSet<(int X, int Y)> seen, int x, int y, ContainerSpec container)
        {
            if (x < 0 || y < 0 || x > container.Length || y > container.Width) return;
            var point = (x, y);
            if (seen.Add(point))
            {
                points.Add(point);
            }
        }

        private static List<PlacedBox> PackSinglePallet(IReadOnlyList<PackBox> boxes, PalletSpec pallet, int seed)
        {
            int n = boxes.Count;
            if (n == 0) return new List<PlacedBox>();

            var rng = new Rng(seed);

            int populationSize = Math.Clamp(80 + n / 3, 80, 180);
            int generations = Math.Clamp(280 + n, 280, 700);
            int tournamentK = 3;

            var population = new Chromosome[populationSize];
            population[0] = CreateHeuristicChromosome(boxes, descendingVolume: true);
            Evaluate(population[0], boxes, pallet);

            if (populationSize > 1)
            {
                population[1] = CreateHeuristicChromosome(boxes, descendingVolume: false);
                Evaluate(population[1], boxes, pallet);
            }

            for (int i = 2; i < populationSize; i++)
            {
                population[i] = RandomChromosome(n, rng);
                Evaluate(population[i], boxes, pallet);
            }

            Chromosome best = population.OrderByDescending(c => c.Fitness)
                .ThenByDescending(c => c.PlacedCount)
                .ThenBy(c => c.Height)
                .ThenBy(c => c.EmptyVolume)
                .First()
                .Clone();

            int noImprove = 0;
            int noImproveLimit = Math.Clamp(90 + n / 2, 90, 220);

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
                    Evaluate(child, boxes, pallet);
                    next[i] = child;
                }

                population = next;

                var genBest = population.OrderByDescending(c => c.Fitness)
                    .ThenByDescending(c => c.PlacedCount)
                    .ThenBy(c => c.Height)
                    .ThenBy(c => c.EmptyVolume)
                    .First();

                if (IsBetter(genBest, best))
                {
                    best = genBest.Clone();
                    noImprove = 0;
                }
                else
                {
                    noImprove++;
                    if (noImprove >= noImproveLimit)
                        break;
                }
            }

            return Decode(best, boxes, pallet);
        }

        private static bool IsBetter(Chromosome candidate, Chromosome incumbent)
        {
            if (candidate.Fitness != incumbent.Fitness)
                return candidate.Fitness > incumbent.Fitness;

            if (candidate.PlacedCount != incumbent.PlacedCount)
                return candidate.PlacedCount > incumbent.PlacedCount;

            if (candidate.Height != incumbent.Height)
                return candidate.Height < incumbent.Height;

            return candidate.EmptyVolume < incumbent.EmptyVolume;
        }

        private static Chromosome CreateHeuristicChromosome(IReadOnlyList<PackBox> boxes, bool descendingVolume)
        {
            var order = Enumerable.Range(0, boxes.Count)
                .OrderBy(i => descendingVolume ? 0 : 1)
                .ThenByDescending(i => descendingVolume ? boxes[i].Volume : (long)boxes[i].L * boxes[i].W)
                .ThenByDescending(i => boxes[i].H)
                .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                .ToArray();

            if (!descendingVolume)
            {
                Array.Reverse(order);
            }

            var c = new Chromosome(boxes.Count);
            for (int i = 0; i < boxes.Count; i++)
            {
                c.Order[i] = order[i];
                c.Orientation[i] = ChooseHeuristicOrientation(boxes[order[i]]);
            }

            return c;
        }

        private static byte ChooseHeuristicOrientation(PackBox b)
        {
            byte bestOri = 0;
            long bestScore = long.MinValue;

            for (byte ori = 0; ori < 6; ori++)
            {
                var (l, w, h) = OrientedDims(b, ori);
                long score = (long)l * w * 10 - h;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOri = ori;
                }
            }

            return bestOri;
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
                if (IsBetter(c, best)) best = c;
            }
            return best;
        }

        private static Chromosome Crossover(Chromosome a, Chromosome b, Rng rng)
        {
            int n = a.Order.Length;
            var child = new Chromosome(n);

            int cut1 = rng.Int(0, n - 1);
            int cut2 = rng.Int(0, n - 1);
            if (cut1 > cut2) (cut1, cut2) = (cut2, cut1);

            var used = new bool[n];
            for (int i = cut1; i <= cut2; i++)
            {
                child.Order[i] = a.Order[i];
                child.Orientation[i] = a.Orientation[i];
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
                    child.Orientation[write] = b.Orientation[read];
                    used[gene] = true;
                    write = (write + 1) % n;
                    filled++;
                }
                read = (read + 1) % n;
            }

            return child;
        }

        private static void Mutate(Chromosome c, Rng rng)
        {
            int n = c.Order.Length;

            if (n >= 2 && rng.Bool(0.35))
            {
                int i = rng.Int(0, n - 1);
                int j = rng.Int(0, n - 1);
                (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
                (c.Orientation[i], c.Orientation[j]) = (c.Orientation[j], c.Orientation[i]);
            }

            if (n >= 3 && rng.Bool(0.20))
            {
                int i = rng.Int(0, n - 1);
                int j = rng.Int(0, n - 1);
                if (i > j) (i, j) = (j, i);
                while (i < j)
                {
                    (c.Order[i], c.Order[j]) = (c.Order[j], c.Order[i]);
                    (c.Orientation[i], c.Orientation[j]) = (c.Orientation[j], c.Orientation[i]);
                    i++;
                    j--;
                }
            }

            if (rng.Bool(0.45))
            {
                int i = rng.Int(0, n - 1);
                c.Orientation[i] = (byte)rng.Int(0, 5);
            }
        }

        private static void Evaluate(Chromosome c, IReadOnlyList<PackBox> boxes, PalletSpec pallet)
        {
            var placed = Decode(c, boxes, pallet);
            var metrics = Measure(placed, boxes.Count, pallet);

            c.PlacedCount = metrics.PlacedCount;
            c.Height = metrics.Height;
            c.EmptyVolume = Math.Max(0, metrics.BoundingVolume - metrics.PlacedVolume);
            c.Fitness = ComputeFitness(metrics, boxes.Count, pallet);
        }

        private static DecodeMetrics Measure(IReadOnlyList<PlacedBox> placed, int totalBoxes, PalletSpec pallet)
        {
            long placedVol = 0;
            int height = 0;

            for (int i = 0; i < placed.Count; i++)
            {
                placedVol += placed[i].Volume;
                if (placed[i].Z > height) height = placed[i].Z;
            }

            long baseArea = (long)pallet.Length * pallet.Width;
            long boundingVolume = baseArea * height;

            return new DecodeMetrics(
                placed.Count,
                height,
                placedVol,
                boundingVolume,
                CountSameSizeTouchingRuns(placed));
        }

        private static double ComputeFitness(DecodeMetrics metrics, int totalBoxes, PalletSpec pallet)
        {
            double p1 = metrics.BoundingVolume > 0
                ? (double)metrics.PlacedVolume / metrics.BoundingVolume
                : 0.0;

            double p2 = totalBoxes > 0
                ? (double)metrics.PlacedCount / totalBoxes
                : 0.0;

            double p3 = metrics.PlacedCount > 1
                ? (double)metrics.SameSizeTouchingCount / (metrics.PlacedCount - 1)
                : 0.0;

            long palletVolume = (long)pallet.Length * pallet.Width * pallet.MaxHeight;
            double p4 = palletVolume > 0
                ? (double)metrics.PlacedVolume / palletVolume
                : p2;

            const double q1 = 0.30;
            const double q2 = 0.40;
            const double q3 = 0.10;
            const double q4 = 0.20;

            double heightPenalty = pallet.MaxHeight > 0 && metrics.Height > 0
                ? (double)metrics.Height / pallet.MaxHeight
                : 0.0;

            return q1 * p1 + q2 * p2 + q3 * p3 + q4 * p4 - 0.02 * heightPenalty;
        }

        private static int CountSameSizeTouchingRuns(IReadOnlyList<PlacedBox> placed)
        {
            int count = 0;
            for (int i = 1; i < placed.Count; i++)
            {
                if (placed[i - 1].SizeKey == placed[i].SizeKey && TouchByFace(placed[i - 1], placed[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TouchByFace(PlacedBox a, PlacedBox b)
        {
            bool touchX = (a.X == b.x || b.X == a.x) && OverlapStrict(a.y, a.Y, b.y, b.Y) && OverlapStrict(a.z, a.Z, b.z, b.Z);
            bool touchY = (a.Y == b.y || b.Y == a.y) && OverlapStrict(a.x, a.X, b.x, b.X) && OverlapStrict(a.z, a.Z, b.z, b.Z);
            bool touchZ = (a.Z == b.z || b.Z == a.z) && OverlapStrict(a.x, a.X, b.x, b.X) && OverlapStrict(a.y, a.Y, b.y, b.Y);
            return touchX || touchY || touchZ;
        }

        private static bool OverlapStrict(int a1, int a2, int b1, int b2) => Math.Min(a2, b2) > Math.Max(a1, b1);

        private static List<PlacedBox> Decode(Chromosome c, IReadOnlyList<PackBox> boxes, PalletSpec pallet)
        {
            var placed = new List<PlacedBox>(boxes.Count);

            var points = new List<Int3>(1 + boxes.Count * 7) { new Int3(0, 0, 0) };
            var pointsSet = new HashSet<Int3> { new Int3(0, 0, 0) };

            for (int pos = 0; pos < c.Order.Length; pos++)
            {
                var box = boxes[c.Order[pos]];
                byte ori = (byte)(c.Orientation[pos] % 6);

                points.Sort(static (a, b) =>
                {
                    long da = (long)a.X * a.X + (long)a.Y * a.Y + (long)a.Z * a.Z;
                    long db = (long)b.X * b.X + (long)b.Y * b.Y + (long)b.Z * b.Z;

                    int d = da.CompareTo(db);
                    if (d != 0) return d;

                    int z = a.Z.CompareTo(b.Z);
                    if (z != 0) return z;

                    int y = a.Y.CompareTo(b.Y);
                    if (y != 0) return y;

                    return a.X.CompareTo(b.X);
                });

                var (l, w, h) = OrientedDims(box, ori);

                for (int pi = 0; pi < points.Count; pi++)
                {
                    var p = points[pi];
                    if (!Fits(p, l, w, h, placed, pallet))
                        continue;

                    var pb = new PlacedBox(box, box.Id, p.X, p.Y, p.Z, p.X + l, p.Y + w, p.Z + h);
                    placed.Add(pb);

                    pointsSet.Remove(p);
                    points.RemoveAt(pi);
                    AddPoints(points, pointsSet, pallet, pb);
                    break;
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
            if (X > pallet.Length || Y > pallet.Width || Z > pallet.MaxHeight) return false;

            for (int i = 0; i < placed.Count; i++)
            {
                var b = placed[i];
                bool separated = (X <= b.x) || (b.X <= x) || (Y <= b.y) || (b.Y <= y) || (Z <= b.z) || (b.Z <= z);
                if (!separated) return false;
            }

            if (z == 0) return true;

            return
                CornerSupported(x, y, z, placed) &&
                CornerSupported(X, y, z, placed) &&
                CornerSupported(x, Y, z, placed) &&
                CornerSupported(X, Y, z, placed);
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
            if (x > pallet.Length || y > pallet.Width || z > pallet.MaxHeight) return;

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

        private static void WritePackingCsv(
            string path,
            PalletSpec pallet,
            ContainerSpec? container,
            IReadOnlyList<PackedContainer> containers)
        {
            using var sw = new StreamWriter(path);

            if (container is not null)
            {
                sw.WriteLine(string.Join(",",
                    "CONTAINER",
                    containers[0].ContainerId,
                    "0",
                    "0",
                    "0",
                    container.Length.ToString(CultureInfo.InvariantCulture),
                    container.Width.ToString(CultureInfo.InvariantCulture),
                    container.Height.ToString(CultureInfo.InvariantCulture)));

                for (int i = 1; i < containers.Count; i++)
                {
                    sw.WriteLine(string.Join(",",
                        "CONTAINER",
                        containers[i].ContainerId,
                        "0",
                        "0",
                        "0",
                        container.Length.ToString(CultureInfo.InvariantCulture),
                        container.Width.ToString(CultureInfo.InvariantCulture),
                        container.Height.ToString(CultureInfo.InvariantCulture)));
                }
            }

            foreach (var packedContainer in containers)
            {
                foreach (var packedPallet in packedContainer.Pallets)
                {
                    sw.WriteLine(string.Join(",",
                        "PALLET",
                        packedPallet.PalletId,
                        packedPallet.OriginX.ToString(CultureInfo.InvariantCulture),
                        packedPallet.OriginY.ToString(CultureInfo.InvariantCulture),
                        packedPallet.OriginZ.ToString(CultureInfo.InvariantCulture),
                        packedPallet.FootprintLength.ToString(CultureInfo.InvariantCulture),
                        packedPallet.FootprintWidth.ToString(CultureInfo.InvariantCulture),
                        pallet.MaxHeight.ToString(CultureInfo.InvariantCulture),
                        packedPallet.ContainerId));
                }
            }

            sw.WriteLine("ID,PalletId,x,y,z,X,Y,Z");

            foreach (var packedContainer in containers)
            {
                foreach (var packedPallet in packedContainer.Pallets)
                {
                    foreach (var b in packedPallet.Boxes)
                    {
                        var (x1, y1, x2, y2) = TransformToContainerXY(b, packedPallet, pallet);
                        sw.WriteLine(string.Join(",",
                            b.Id,
                            packedPallet.PalletId,
                            x1.ToString(CultureInfo.InvariantCulture),
                            y1.ToString(CultureInfo.InvariantCulture),
                            (packedPallet.OriginZ + b.z).ToString(CultureInfo.InvariantCulture),
                            x2.ToString(CultureInfo.InvariantCulture),
                            y2.ToString(CultureInfo.InvariantCulture),
                            (packedPallet.OriginZ + b.Z).ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }
        }

        private static (int x1, int y1, int x2, int y2) TransformToContainerXY(PlacedBox box, PackedPallet pallet, PalletSpec palletSpec)
        {
            if (!pallet.RotatedOnFloor)
            {
                return (
                    pallet.OriginX + box.x,
                    pallet.OriginY + box.y,
                    pallet.OriginX + box.X,
                    pallet.OriginY + box.Y);
            }

            int x1 = pallet.OriginX + box.y;
            int x2 = pallet.OriginX + box.Y;
            int y1 = pallet.OriginY + (palletSpec.Length - box.X);
            int y2 = pallet.OriginY + (palletSpec.Length - box.x);
            return (x1, y1, x2, y2);
        }
    }
}
