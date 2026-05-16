using System;
using System.Collections.Generic;
using System.Linq;
using Palletes.Generation;
using Palletes.Models;

namespace Palletes.Core
{
    public static partial class PalletPacker
    {
        private static List<PlacedBox> PackSinglePalletByMode(
            IReadOnlyList<PackBox> boxes,
            PalletSpec pallet,
            int seed,
            FitnessWeights weights,
            OrientationFallbackMode orientationMode,
            PackingSearchMode searchMode,
            int multiStartRandomStarts)
        {
            return searchMode switch
            {
                PackingSearchMode.MultiStartGreedy => PackSinglePalletMultiStartGreedy(
                    boxes,
                    pallet,
                    seed,
                    weights,
                    orientationMode,
                    multiStartRandomStarts,
                    DecoderPlacementMode.FirstFit),
                PackingSearchMode.MultiStartGreedyBestFit => PackSinglePalletMultiStartGreedy(
                    boxes,
                    pallet,
                    seed,
                    weights,
                    orientationMode,
                    multiStartRandomStarts,
                    DecoderPlacementMode.BestFitLite),
                _ => throw new ArgumentOutOfRangeException(nameof(searchMode), searchMode, "Unknown packing search mode.")
            };
        }

        private static List<PlacedBox> PackSinglePalletMultiStartGreedy(
            IReadOnlyList<PackBox> boxes,
            PalletSpec pallet,
            int seed,
            FitnessWeights weights,
            OrientationFallbackMode orientationMode,
            int randomStarts,
            DecoderPlacementMode placementMode = DecoderPlacementMode.FirstFit)
        {
            int n = boxes.Count;
            if (n == 0) return new List<PlacedBox>();

            var rng = new Rng(seed);
            int safeRandomStarts = Math.Clamp(randomStarts, 0, 500);
            PackingCandidate? best = null;

            void Consider(PackingCandidate candidate)
            {
                Evaluate(candidate, boxes, pallet, weights, orientationMode, placementMode);
                if (best is null || IsBetter(candidate, best))
                {
                    best = candidate.Clone();
                }
            }

            foreach (var candidate in CreateDeterministicGreedyStarts(boxes))
            {
                Consider(candidate);
            }

            for (int i = 0; i < safeRandomStarts; i++)
            {
                PackingCandidate candidate = i % 3 == 0
                    ? CreateRandomGreedyStart(boxes, rng)
                    : CreateBiasedRandomGreedyStart(boxes, rng, i % 3);

                Consider(candidate);
            }

            return Decode(best!, boxes, pallet, orientationMode, placementMode);
        }

        private static IEnumerable<PackingCandidate> CreateDeterministicGreedyStarts(IReadOnlyList<PackBox> boxes)
        {
            int n = boxes.Count;
            var indices = Enumerable.Range(0, n);

            yield return CreateHeuristicPackingCandidate(boxes, descendingVolume: true);
            yield return CreateHeuristicPackingCandidate(boxes, descendingVolume: false);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => boxes[i].Volume)
                    .ThenByDescending(i => boxes[i].WeightGrams)
                    .ThenByDescending(i => boxes[i].Strength)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => (long)boxes[i].L * boxes[i].W)
                    .ThenByDescending(i => boxes[i].H)
                    .ThenByDescending(i => boxes[i].WeightGrams)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => boxes[i].WeightGrams)
                    .ThenByDescending(i => boxes[i].Volume)
                    .ThenByDescending(i => boxes[i].Strength)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => boxes[i].H)
                    .ThenByDescending(i => boxes[i].Volume)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => Math.Max(boxes[i].L, Math.Max(boxes[i].W, boxes[i].H)))
                    .ThenByDescending(i => boxes[i].Volume)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => boxes[i].Strength)
                    .ThenByDescending(i => boxes[i].WeightGrams)
                    .ThenByDescending(i => boxes[i].Volume)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderBy(i => boxes[i].TypeKey, StringComparer.Ordinal)
                    .ThenByDescending(i => boxes[i].Volume)
                    .ThenByDescending(i => boxes[i].WeightGrams)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderBy(i => boxes[i].Volume)
                    .ThenBy(i => boxes[i].WeightGrams)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                ChooseHeuristicOrientation);

            yield return CreateGreedyStart(
                boxes,
                indices
                    .OrderByDescending(i => boxes[i].Volume)
                    .ThenByDescending(i => boxes[i].WeightGrams)
                    .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                    .ToArray(),
                static _ => (byte)0);
        }

        private static PackingCandidate CreateGreedyStart(
            IReadOnlyList<PackBox> boxes,
            int[] order,
            Func<PackBox, byte> chooseOrientation)
        {
            var c = new PackingCandidate(boxes.Count);
            for (int pos = 0; pos < order.Length; pos++)
            {
                int boxIndex = order[pos];
                c.Order[pos] = boxIndex;
                c.Orientation[pos] = chooseOrientation(boxes[boxIndex]);
            }

            return c;
        }

        private static PackingCandidate CreateRandomGreedyStart(IReadOnlyList<PackBox> boxes, Rng rng)
        {
            var order = Enumerable.Range(0, boxes.Count).ToArray();
            rng.Shuffle(order);

            return CreateGreedyStart(
                boxes,
                order,
                b => rng.Bool(0.75) ? ChooseHeuristicOrientation(b) : (byte)rng.Int(0, 5));
        }

        private static PackingCandidate CreateBiasedRandomGreedyStart(IReadOnlyList<PackBox> boxes, Rng rng, int variant)
        {
            long maxVolume = Math.Max(1L, boxes.Max(static b => b.Volume));
            long maxBaseArea = Math.Max(1L, boxes.Max(static b => (long)b.L * b.W));
            int maxWeight = Math.Max(1, boxes.Max(static b => b.WeightGrams));
            int maxSide = Math.Max(1, boxes.Max(static b => Math.Max(b.L, Math.Max(b.W, b.H))));
            int maxHeight = Math.Max(1, boxes.Max(static b => b.H));

            double WeightFor(PackBox b)
            {
                double volume = b.Volume / (double)maxVolume;
                double baseArea = ((long)b.L * b.W) / (double)maxBaseArea;
                double weight = b.WeightGrams / (double)maxWeight;
                double side = Math.Max(b.L, Math.Max(b.W, b.H)) / (double)maxSide;
                double height = b.H / (double)maxHeight;

                return variant switch
                {
                    1 => 1.0 + weight * 5.0 + volume * 2.0 + b.Strength * 0.15,
                    _ => 1.0 + volume * 4.0 + baseArea * 2.0 + side * 2.0 + height
                };
            }

            var order = Enumerable.Range(0, boxes.Count)
                .Select(i =>
                {
                    double u = Math.Max(1e-12, rng.Double());
                    double key = -Math.Log(u) / WeightFor(boxes[i]);
                    return (Index: i, Key: key);
                })
                .OrderBy(x => x.Key)
                .ThenBy(x => boxes[x.Index].Id, StringComparer.Ordinal)
                .Select(x => x.Index)
                .ToArray();

            return CreateGreedyStart(
                boxes,
                order,
                b => rng.Bool(0.85) ? ChooseHeuristicOrientation(b) : (byte)rng.Int(0, 5));
        }

        private static bool IsBetter(PackingCandidate candidate, PackingCandidate incumbent)
        {
            if (candidate.Fitness != incumbent.Fitness)
                return candidate.Fitness > incumbent.Fitness;

            if (candidate.PlacedCount != incumbent.PlacedCount)
                return candidate.PlacedCount > incumbent.PlacedCount;

            if (candidate.Height != incumbent.Height)
                return candidate.Height < incumbent.Height;

            return candidate.EmptyVolume < incumbent.EmptyVolume;
        }

        private static PackingCandidate CreateHeuristicPackingCandidate(IReadOnlyList<PackBox> boxes, bool descendingVolume)
        {
            var order = Enumerable.Range(0, boxes.Count)
                .OrderBy(i => descendingVolume ? 0 : 1)
                .ThenByDescending(i => descendingVolume ? boxes[i].Volume : (long)boxes[i].L * boxes[i].W)
                .ThenByDescending(i => boxes[i].WeightGrams)
                .ThenByDescending(i => boxes[i].Strength)
                .ThenByDescending(i => boxes[i].H)
                .ThenBy(i => boxes[i].Id, StringComparer.Ordinal)
                .ToArray();

            if (!descendingVolume)
            {
                Array.Reverse(order);
            }

            var candidate = new PackingCandidate(boxes.Count);
            for (int i = 0; i < boxes.Count; i++)
            {
                candidate.Order[i] = order[i];
                candidate.Orientation[i] = ChooseHeuristicOrientation(boxes[order[i]]);
            }

            return candidate;
        }

        private static byte ChooseHeuristicOrientation(PackBox box)
        {
            byte bestOrientation = 0;
            long bestScore = long.MinValue;

            for (byte orientation = 0; orientation < 6; orientation++)
            {
                var (l, w, h) = OrientedDims(box, orientation);
                long score = (long)l * w * 10 - h;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOrientation = orientation;
                }
            }

            return bestOrientation;
        }
    }
}
