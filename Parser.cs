using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Palletes
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
        public int Aisle { get; set; }
        public int Caustic { get; set; }

        public static List<ItemRow> ParseSimple(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.ASCII);

            int i = 0;
            if (lines.Length == 0) return new();

            if (int.TryParse(lines[0].Trim(), out _)) i++;

            if (i < lines.Length) i++;

            var result = new List<ItemRow>();

            for (; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.EndsWith(",")) line = line.TrimEnd(',');

                var p = line.Split(',');

                if (p.Length < 9) continue;

                result.Add(new ItemRow
                {
                    SKU = int.Parse(p[0]),
                    Quantity = int.Parse(p[1]),
                    Length = int.Parse(p[2]),
                    Width = int.Parse(p[3]),
                    Height = int.Parse(p[4]),
                    Weight = int.Parse(p[5]),
                    Strength = int.Parse(p[6]),
                    Aisle = int.Parse(p[7]),
                    Caustic = int.Parse(p[8]),
                });
            }

            return result;
        }
    }
}
