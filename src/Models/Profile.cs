using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Palletes.Models
{
    public sealed class Profile
    {
        public List<BoxFamily> Families { get; init; } = new();

        public double KSlope { get; init; } = 0.35;
        public double KBias { get; init; } = 2.0;
        public double KNoiseStd { get; init; } = 2.0;

        public static Profile DefaultRetailLike() => new()
        {
            Families = new List<BoxFamily>
        {
            new("small_cube", 0.35, new(120,260), new(120,260), new(80,200),  ShapeHint.CubeLike),
            new("medium",     0.35, new(250,500), new(180,420), new(120,350), ShapeHint.Mixed),
            new("flat",       0.12, new(300,800), new(200,700), new(30,120),  ShapeHint.Flat),
            new("tall",       0.08, new(180,450), new(180,450), new(400,900), ShapeHint.Tall),
            new("large",      0.10, new(500,1100),new(350,800), new(200,700), ShapeHint.Mixed),
        }
        };
    }

    public enum ShapeHint { CubeLike, Flat, Tall, Mixed }

    public readonly record struct IntRange(int Min, int Max);

    public sealed class BoxFamily
    {
        public string Name { get; }
        public double Weight { get; }
        public IntRange L { get; }
        public IntRange W { get; }
        public IntRange H { get; }
        public ShapeHint Shape { get; }

        public BoxFamily(string name, double weight, IntRange l, IntRange w, IntRange h, ShapeHint shape)
        {
            Name = name; Weight = weight; L = l; W = w; H = h; Shape = shape;
        }
    }
}
