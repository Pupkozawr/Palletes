using System;
using System.Collections.Generic;
using System.Linq;
using Palletes.Models;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        private static void Evaluate(
            Chromosome c,
            IReadOnlyList<PackBox> boxes,
            PalletSpec pallet,
            FitnessWeights weights,
            OrientationFallbackMode orientationMode)
        {
            var placed = Decode(c, boxes, pallet, orientationMode);
            var metrics = Measure(placed, boxes.Count, pallet);

            c.PlacedCount = metrics.PlacedCount;
            c.Height = metrics.Height;
            c.EmptyVolume = Math.Max(0, metrics.BoundingVolume - metrics.PlacedVolume);
            c.Fitness = ComputeFitness(metrics, boxes.Count, pallet, weights);
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
                CountSameTypeTouchingRuns(placed),
                CountSameAisleTouchingRuns(placed),
                AverageSupportScore(placed),
                ComputeHeavyLowScore(placed, pallet));
        }

        private static double ComputeFitness(DecodeMetrics metrics, int totalBoxes, PalletSpec pallet, FitnessWeights weights)
        {
            double p1 = metrics.BoundingVolume > 0
                ? (double)metrics.PlacedVolume / metrics.BoundingVolume
                : 0.0;

            double p2 = totalBoxes > 0
                ? (double)metrics.PlacedCount / totalBoxes
                : 0.0;

            double p3 = metrics.PlacedCount > 1
                ? Math.Min(1.0, (metrics.SameTypeTouchingCount + 0.35 * metrics.SameAisleTouchingCount) / (metrics.PlacedCount - 1.0))
                : 0.0;

            long palletVolume = (long)pallet.Length * pallet.Width * pallet.MaxHeight;
            double p4 = palletVolume > 0
                ? (double)metrics.PlacedVolume / palletVolume
                : p2;

            double p5 = metrics.StabilityScore;
            double p6 = metrics.HeavyLowScore;

            double heightPenalty = pallet.MaxHeight > 0 && metrics.Height > 0
                ? (double)metrics.Height / pallet.MaxHeight
                : 0.0;

            return
                weights.Density * p1 +
                weights.PlacedShare * p2 +
                weights.Grouping * p3 +
                weights.PalletVolumeUse * p4 +
                weights.Stability * p5 +
                weights.HeavyLow * p6 -
                weights.HeightPenalty * heightPenalty;
        }

        private static int CountSameTypeTouchingRuns(IReadOnlyList<PlacedBox> placed)
        {
            int count = 0;
            for (int i = 1; i < placed.Count; i++)
            {
                if (placed[i - 1].TypeKey == placed[i].TypeKey && TouchByFace(placed[i - 1], placed[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountSameAisleTouchingRuns(IReadOnlyList<PlacedBox> placed)
        {
            int count = 0;
            for (int i = 1; i < placed.Count; i++)
            {
                if (placed[i - 1].Source.Aisle > 0 &&
                    placed[i - 1].Source.Aisle == placed[i].Source.Aisle &&
                    TouchByFace(placed[i - 1], placed[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static double AverageSupportScore(IReadOnlyList<PlacedBox> placed)
        {
            if (placed.Count == 0) return 0.0;

            double sum = 0.0;
            for (int i = 0; i < placed.Count; i++)
            {
                var b = placed[i];
                if (b.z == 0)
                {
                    sum += 1.0;
                    continue;
                }

                long footprint = (long)b.Dx * b.Dy;
                if (footprint <= 0)
                    continue;

                long supportedArea = SupportArea(b.x, b.y, b.X, b.Y, b.z, placed, excludeIndex: i);
                double ratio = Math.Min(1.0, supportedArea / (double)footprint);
                bool centerSupported = IsPointSupported((b.x + b.X) / 2.0, (b.y + b.Y) / 2.0, b.z, placed, excludeIndex: i);
                sum += centerSupported ? ratio : ratio * 0.5;
            }

            return sum / placed.Count;
        }

        private static double ComputeHeavyLowScore(IReadOnlyList<PlacedBox> placed, PalletSpec pallet)
        {
            long totalWeight = placed.Sum(static b => (long)b.WeightGrams);
            if (placed.Count == 0 || totalWeight <= 0 || pallet.MaxHeight <= 0)
                return 1.0;

            double weightedCenterHeight = 0.0;
            for (int i = 0; i < placed.Count; i++)
            {
                var b = placed[i];
                double centerZ = (b.z + b.Z) / 2.0;
                weightedCenterHeight += centerZ * b.WeightGrams;
            }

            double normalized = weightedCenterHeight / totalWeight / pallet.MaxHeight;
            return Math.Clamp(1.0 - normalized, 0.0, 1.0);
        }

        private static bool TouchByFace(PlacedBox a, PlacedBox b)
        {
            bool touchX = (a.X == b.x || b.X == a.x) && OverlapStrict(a.y, a.Y, b.y, b.Y) && OverlapStrict(a.z, a.Z, b.z, b.Z);
            bool touchY = (a.Y == b.y || b.Y == a.y) && OverlapStrict(a.x, a.X, b.x, b.X) && OverlapStrict(a.z, a.Z, b.z, b.Z);
            bool touchZ = (a.Z == b.z || b.Z == a.z) && OverlapStrict(a.x, a.X, b.x, b.X) && OverlapStrict(a.y, a.Y, b.y, b.Y);
            return touchX || touchY || touchZ;
        }

        private static bool OverlapStrict(int a1, int a2, int b1, int b2) => Math.Min(a2, b2) > Math.Max(a1, b1);

    }
}
