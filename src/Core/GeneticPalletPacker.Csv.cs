using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Palletes.Models;
using Palletes.Utils;

namespace Palletes.Core
{
    public static partial class GeneticPalletPacker
    {
        private static List<PackBox> ExpandBoxes(List<ItemRow> items)
        {
            var boxes = new List<PackBox>();

            foreach (var it in items)
            {
                if (it.Quantity <= 0) continue;

                for (int k = 1; k <= it.Quantity; k++)
                {
                    string id = it.Quantity == 1 ? it.SKU.ToString(CultureInfo.InvariantCulture) : $"{it.SKU}_{k}";
                    string sku = it.SKU.ToString(CultureInfo.InvariantCulture);
                    boxes.Add(new PackBox(
                        id,
                        sku,
                        it.Length,
                        it.Width,
                        it.Height,
                        it.Weight,
                        it.Strength,
                        it.Aisle,
                        it.Caustic != 0));
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
