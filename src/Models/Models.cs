using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Palletes.Models
{
    public sealed class PalletSpec
    {
        public string PalletType { get; set; } = "EUR";
        public int Length { get; set; } = 1200;
        public int Width { get; set; } = 800;
        public int MaxHeight { get; set; } = 2000;
        public int MaxWeight { get; set; } = 0;
    }

    public sealed class ContainerSpec
    {
        public string ContainerType { get; set; } = "UK-3";
        public int Length { get; set; } = 1930;
        public int Width { get; set; } = 1225;
        public int Height { get; set; } = 2128;
    }

    public sealed class SkuLine
    {
        public int SKU { get; set; }
        public int Quantity { get; set; }
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Weight { get; set; }
        public int Strength { get; set; }
        public int Aisle { get; set; }
        public int Caustic { get; set; }
    }

    public sealed class BoxRow
    {
        public string OrderId { get; set; } = "";
        public string BoxId { get; set; } = "";
        public string SkuId { get; set; } = "";
        public int CategoryId { get; set; }
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Weight { get; set; }
    }

    public sealed class OrderMeta
    {
        public string OrderId { get; set; } = "";
        public string Group { get; set; } = "";
        public string Scenario { get; set; } = "";
        public PalletSpec Pallet { get; set; } = new();
        public int TotalBoxes { get; set; }
        public int TotalSkus { get; set; }
        public int TotalCategories { get; set; }
        public int? TargetPallets { get; set; }
        public string Notes { get; set; } = "";
    }


    public readonly struct BoxDims
    {
        public int L { get; }
        public int W { get; }
        public int H { get; }
        public BoxDims(int l, int w, int h) { L = l; W = w; H = h; }
    }

}
