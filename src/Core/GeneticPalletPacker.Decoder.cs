using System;
using System.Collections.Generic;
using System.Linq;
using Palletes.Models;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        private static List<PlacedBox> Decode(
            Chromosome c,
            IReadOnlyList<PackBox> boxes,
            PalletSpec pallet,
            OrientationFallbackMode orientationMode)
        {
            var placed = new List<PlacedBox>(boxes.Count);

            var points = new List<Int3>(1 + boxes.Count * 7) { new Int3(0, 0, 0) };
            var pointsSet = new HashSet<Int3> { new Int3(0, 0, 0) };
            long placedWeightGrams = 0;

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

                if (TryPlaceBox(box, ori, points, pointsSet, placed, ref placedWeightGrams, pallet))
                    continue;

                if (orientationMode == OrientationFallbackMode.GeneOnly)
                    continue;

                foreach (byte fallbackOri in FallbackOrientations(box, ori))
                {
                    if (TryPlaceBox(box, fallbackOri, points, pointsSet, placed, ref placedWeightGrams, pallet))
                        break;
                }
            }

            return placed;
        }

        private static bool TryPlaceBox(
            PackBox box,
            byte ori,
            List<Int3> points,
            HashSet<Int3> pointsSet,
            List<PlacedBox> placed,
            ref long placedWeightGrams,
            PalletSpec pallet)
        {
            var (l, w, h) = OrientedDims(box, ori);

            for (int pi = 0; pi < points.Count; pi++)
            {
                var p = points[pi];
                if (!Fits(box, p, l, w, h, placed, placedWeightGrams, pallet))
                    continue;

                var pb = new PlacedBox(box, box.Id, p.X, p.Y, p.Z, p.X + l, p.Y + w, p.Z + h);
                placed.Add(pb);
                placedWeightGrams += box.WeightGrams;

                pointsSet.Remove(p);
                points.RemoveAt(pi);
                AddPoints(points, pointsSet, pallet, pb);
                return true;
            }

            return false;
        }

        private static IEnumerable<byte> FallbackOrientations(PackBox box, byte preferred)
        {
            var seen = new HashSet<(int L, int W, int H)> { OrientedDims(box, preferred) };

            return Enumerable.Range(0, 6)
                .Select(i => (Ori: (byte)i, Dims: OrientedDims(box, (byte)i)))
                .Where(x => x.Ori != preferred && seen.Add(x.Dims))
                .OrderBy(x => x.Dims.H)
                .ThenByDescending(x => (long)x.Dims.L * x.Dims.W)
                .ThenBy(x => x.Ori)
                .Take(3)
                .Select(x => x.Ori);
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

        private static bool Fits(PackBox box, Int3 p, int L, int W, int H, List<PlacedBox> placed, long placedWeightGrams, PalletSpec pallet)
        {
            if (L <= 0 || W <= 0 || H <= 0) return false;

            int x = p.X, y = p.Y, z = p.Z;
            int X = x + L, Y = y + W, Z = z + H;

            if (x < 0 || y < 0 || z < 0) return false;
            if (X > pallet.Length || Y > pallet.Width || Z > pallet.MaxHeight) return false;

            if (pallet.MaxWeight > 0)
            {
                if (placedWeightGrams + box.WeightGrams > pallet.MaxWeight)
                    return false;
            }

            for (int i = 0; i < placed.Count; i++)
            {
                var b = placed[i];
                bool separated = (X <= b.x) || (b.X <= x) || (Y <= b.y) || (b.Y <= y) || (Z <= b.z) || (b.Z <= z);
                if (!separated) return false;
            }

            if (z == 0) return true;

            if (!CornerSupported(x, y, z, placed) ||
                !CornerSupported(X, y, z, placed) ||
                !CornerSupported(x, Y, z, placed) ||
                !CornerSupported(X, Y, z, placed))
            {
                return false;
            }

            long footprintArea = (long)L * W;
            long supportedArea = SupportArea(x, y, X, Y, z, placed);
            if (footprintArea <= 0 || supportedArea / (double)footprintArea < MinSupportAreaRatio)
                return false;

            if (!IsPointSupported((x + X) / 2.0, (y + Y) / 2.0, z, placed))
                return false;

            return SupportsCanCarry(box, x, y, X, Y, z, placed);
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

        private static long SupportArea(int x, int y, int X, int Y, int z, IReadOnlyList<PlacedBox> placed, int excludeIndex = -1)
        {
            long area = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                if (i == excludeIndex) continue;

                var b = placed[i];
                if (b.Z != z) continue;

                area += OverlapArea(x, y, X, Y, b.x, b.y, b.X, b.Y);
            }

            return area;
        }

        private static bool IsPointSupported(double cx, double cy, int z, IReadOnlyList<PlacedBox> placed, int excludeIndex = -1)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                if (i == excludeIndex) continue;

                var b = placed[i];
                if (b.Z != z) continue;
                if (cx >= b.x && cx <= b.X && cy >= b.y && cy <= b.Y)
                    return true;
            }

            return false;
        }

        private static bool SupportsCanCarry(PackBox top, int x, int y, int X, int Y, int z, IReadOnlyList<PlacedBox> placed)
        {
            long supportedArea = SupportArea(x, y, X, Y, z, placed);
            if (supportedArea <= 0)
                return false;

            for (int i = 0; i < placed.Count; i++)
            {
                var support = placed[i];
                if (support.Z != z) continue;

                long overlapArea = OverlapArea(x, y, X, Y, support.x, support.y, support.X, support.Y);
                if (overlapArea <= 0) continue;

                if (!CanStackOn(top, support.Source))
                    return false;

                if (top.WeightGrams <= 0)
                    continue;

                long addedLoad = ProportionalLoad(top.WeightGrams, overlapArea, supportedArea);
                if (addedLoad > SupportCapacityGrams(support.Source))
                    return false;
            }

            return true;
        }

        private static bool CanStackOn(PackBox top, PackBox support)
        {
            if (top.Caustic != support.Caustic)
                return false;

            if (top.WeightGrams > 0 && support.WeightGrams > 0 && support.Strength <= 2)
            {
                long allowedSingleTop = support.WeightGrams * 4L;
                if (top.WeightGrams > allowedSingleTop)
                    return false;
            }

            return true;
        }

        private static long SupportCapacityGrams(PackBox support)
        {
            if (support.WeightGrams <= 0)
                return long.MaxValue / 4;

            int strength = Math.Max(1, support.Strength);
            long byStrength = support.WeightGrams * (2L + 2L * strength);
            return Math.Max(MinSupportCapacityGrams, byStrength);
        }

        private static long ProportionalLoad(int weightGrams, long overlapArea, long totalArea)
        {
            if (weightGrams <= 0 || overlapArea <= 0 || totalArea <= 0)
                return 0;

            return (long)Math.Ceiling(weightGrams * (overlapArea / (double)totalArea));
        }

        private static long OverlapArea(int ax1, int ay1, int ax2, int ay2, int bx1, int by1, int bx2, int by2)
        {
            int dx = Math.Min(ax2, bx2) - Math.Max(ax1, bx1);
            int dy = Math.Min(ay2, by2) - Math.Max(ay1, by1);
            return dx > 0 && dy > 0 ? (long)dx * dy : 0;
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

    }
}
