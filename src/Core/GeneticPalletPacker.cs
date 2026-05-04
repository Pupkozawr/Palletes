using System;
using System.Collections.Generic;
using System.Linq;
using Palletes.Models;
using Palletes.Utils;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        public static void PackCsv(string inPath, string outPath, PalletSpec pallet, int seed = 12345)
            => PackCsv(inPath, outPath, pallet, container: null, seed);

        public static void PackCsv(string inPath, string outPath, PalletSpec pallet, ContainerSpec? container, int seed = 12345)
            => PackCsvCore(inPath, outPath, pallet, container, seed, DefaultWeights, OrientationFallbackMode.Fallback);

        internal static void PackCsvForExperiment(
            string inPath,
            string outPath,
            PalletSpec pallet,
            ContainerSpec? container,
            int seed,
            FitnessWeights weights)
            => PackCsvCore(inPath, outPath, pallet, container, seed, NormalizeWeights(weights), OrientationFallbackMode.Fallback);

        internal static void PackCsvForOrientationExperiment(
            string inPath,
            string outPath,
            PalletSpec pallet,
            ContainerSpec? container,
            int seed,
            OrientationFallbackMode orientationMode)
            => PackCsvCore(inPath, outPath, pallet, container, seed, DefaultWeights, orientationMode);

        private static void PackCsvCore(
            string inPath,
            string outPath,
            PalletSpec pallet,
            ContainerSpec? container,
            int seed,
            FitnessWeights weights,
            OrientationFallbackMode orientationMode)
        {
            if (pallet.Length <= 0 || pallet.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(pallet), "Pallet base dimensions must be positive.");

            var items = ItemRow.ParseSimple(inPath);
            var boxes = ExpandBoxes(items);
            var effectivePallet = NormalizePallet(pallet);

            var packedPallets = PackAcrossPallets(boxes, effectivePallet, seed, weights, orientationMode);
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
            var packBoxes = boxes.Select(b => new PackBox(b.Id, b.Id, b.L, b.W, b.H)).ToList();
            var effectivePallet = NormalizePallet(pallet);
            var placed = PackSinglePallet(packBoxes, effectivePallet, seed, DefaultWeights, OrientationFallbackMode.Fallback);

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
                MaxHeight = pallet.MaxHeight > 0 ? pallet.MaxHeight : 2000,
                MaxWeight = Math.Max(0, pallet.MaxWeight)
            };
        }

        private static FitnessWeights NormalizeWeights(FitnessWeights weights)
        {
            string name = string.IsNullOrWhiteSpace(weights.Name) ? "custom" : weights.Name;
            double density = Math.Max(0.0, weights.Density);
            double placedShare = Math.Max(0.0, weights.PlacedShare);
            double grouping = Math.Max(0.0, weights.Grouping);
            double palletVolumeUse = Math.Max(0.0, weights.PalletVolumeUse);
            double stability = Math.Max(0.0, weights.Stability);
            double heavyLow = Math.Max(0.0, weights.HeavyLow);
            double heightPenalty = Math.Max(0.0, weights.HeightPenalty);

            double sum =
                density +
                placedShare +
                grouping +
                palletVolumeUse +
                stability +
                heavyLow;

            if (sum <= 0.0)
                return DefaultWeights;

            return new FitnessWeights(
                name,
                density / sum,
                placedShare / sum,
                grouping / sum,
                palletVolumeUse / sum,
                stability / sum,
                heavyLow / sum,
                heightPenalty);
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

    }
}
