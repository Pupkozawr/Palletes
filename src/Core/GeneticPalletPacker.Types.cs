using System;
using System.Collections.Generic;
using System.Linq;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        private readonly record struct Int3(int X, int Y, int Z);

        internal readonly record struct FitnessWeights(
            string Name,
            double Density,
            double PlacedShare,
            double Grouping,
            double PalletVolumeUse,
            double Stability,
            double HeavyLow,
            double HeightPenalty);

        internal enum OrientationFallbackMode
        {
            GeneOnly,
            Fallback
        }

        private const double Q1Density = 0.18;
        private const double Q2PlacedShare = 0.52;
        private const double Q3Grouping = 0.06;
        private const double Q4PalletVolumeUse = 0.14;
        private const double Q5Stability = 0.06;
        private const double Q6HeavyLow = 0.04;
        private const double HeightPenaltyWeight = 0.02;

        private static readonly FitnessWeights DefaultWeights = new(
            "placed_first",
            Q1Density,
            Q2PlacedShare,
            Q3Grouping,
            Q4PalletVolumeUse,
            Q5Stability,
            Q6HeavyLow,
            HeightPenaltyWeight);

        private sealed class PackBox
        {
            public string Id { get; }
            public string Sku { get; }
            public int L { get; }
            public int W { get; }
            public int H { get; }
            public int WeightGrams { get; }
            public int Strength { get; }
            public int Aisle { get; }
            public bool Caustic { get; }
            public long Volume => (long)L * W * H;
            public string SizeKey { get; }
            public string TypeKey => string.IsNullOrWhiteSpace(Sku) ? SizeKey : Sku;

            public PackBox(string id, string sku, int l, int w, int h, int weightGrams = 0, int strength = 5, int aisle = 0, bool caustic = false)
            {
                Id = id;
                Sku = sku;
                L = l;
                W = w;
                H = h;
                WeightGrams = Math.Max(0, weightGrams);
                Strength = Math.Clamp(strength, 0, 5);
                Aisle = aisle;
                Caustic = caustic;

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
            public string TypeKey => Source.TypeKey;
            public int WeightGrams => Source.WeightGrams;
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
            public long TotalWeightGrams => Boxes.Sum(static b => (long)b.WeightGrams);

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
            int SameTypeTouchingCount,
            int SameAisleTouchingCount,
            double StabilityScore,
            double HeavyLowScore);

        private const double MinSupportAreaRatio = 0.65;
        private const int MinSupportCapacityGrams = 25_000;

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

    }
}
