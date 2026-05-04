using System;
using System.Collections.Generic;
using System.Linq;
using Palletes.Models;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        private static List<PackedPallet> PackAcrossPallets(
            IReadOnlyList<PackBox> boxes,
            PalletSpec pallet,
            int seed,
            FitnessWeights weights,
            OrientationFallbackMode orientationMode)
        {
            var remaining = boxes
                .OrderByDescending(b => b.Volume)
                .ThenByDescending(b => b.WeightGrams)
                .ThenByDescending(b => b.Strength)
                .ThenByDescending(b => Math.Max(b.L, Math.Max(b.W, b.H)))
                .ToList();

            var pallets = new List<PackedPallet>();
            int palletIndex = 1;

            while (remaining.Count > 0)
            {
                var placed = PackSinglePallet(remaining, pallet, seed + palletIndex * 104729, weights, orientationMode);
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
                .ThenByDescending(p => p.TotalWeightGrams)
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

    }
}
