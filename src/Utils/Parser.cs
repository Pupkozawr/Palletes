using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Palletes.Utils
{
    public class ItemRow
    {
        public int SKU { get; set; }
        public int Quantity { get; set; }
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Weight { get; set; }
        public int Strength { get; set; }
        public int Caustic { get; set; }

        public static List<ItemRow> ParseSimple(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.ASCII);

            int i = 0;
            if (lines.Length == 0) return new();

            if (int.TryParse(lines[0].Trim(), out _)) i++;

            var header = i < lines.Length
                ? lines[i].Trim().TrimEnd(',').Split(',').Select(x => x.Trim()).ToArray()
                : Array.Empty<string>();
            if (i < lines.Length) i++;

            int IndexOf(string name, int fallback)
            {
                for (int h = 0; h < header.Length; h++)
                {
                    if (string.Equals(header[h], name, StringComparison.OrdinalIgnoreCase))
                        return h;
                }

                return fallback;
            }

            int skuIndex = IndexOf("SKU", 0);
            int quantityIndex = IndexOf("Quantity", 1);
            int lengthIndex = IndexOf("Length", 2);
            int widthIndex = IndexOf("Width", 3);
            int heightIndex = IndexOf("Height", 4);
            int weightIndex = IndexOf("Weight", 5);
            int strengthIndex = IndexOf("Strength", 6);
            int causticIndex = IndexOf("Caustic", header.Any(x => string.Equals(x, "Aisle", StringComparison.OrdinalIgnoreCase)) ? 8 : 7);
            int requiredColumns = new[]
            {
                skuIndex,
                quantityIndex,
                lengthIndex,
                widthIndex,
                heightIndex,
                weightIndex,
                strengthIndex,
                causticIndex
            }.Max() + 1;

            var result = new List<ItemRow>();

            for (; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.EndsWith(",")) line = line.TrimEnd(',');

                var p = line.Split(',');

                if (p.Length < requiredColumns) continue;

                result.Add(new ItemRow
                {
                    SKU = int.Parse(p[skuIndex]),
                    Quantity = int.Parse(p[quantityIndex]),
                    Length = int.Parse(p[lengthIndex]),
                    Width = int.Parse(p[widthIndex]),
                    Height = int.Parse(p[heightIndex]),
                    Weight = int.Parse(p[weightIndex]),
                    Strength = int.Parse(p[strengthIndex]),
                    Caustic = int.Parse(p[causticIndex]),
                });
            }

            return result;
        }
    }
}
