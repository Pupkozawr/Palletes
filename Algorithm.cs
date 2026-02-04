using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Palletes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    public sealed class BoxPlacement
    {
        public string Id { get; init; } = "";
        public double x { get; init; }
        public double y { get; init; }
        public double z { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }

        public double Dx => X - x;
        public double Dy => Y - y;
        public double Dz => Z - z;
    }

    public sealed class PalletMeta
    {
        public string PalletId { get; init; } = "";
        public double OriginX { get; init; }
        public double OriginY { get; init; }
        public double OriginZ { get; init; }
        public double SizeL { get; init; }
        public double SizeW { get; init; }
        public double SizeH { get; init; }
    }

    public sealed class ValidationResult
    {
        public PalletMeta? Pallet { get; init; }
        public List<BoxPlacement> Boxes { get; init; } = new();
    }

    public static class PackingCsvValidator
    {
        public static ValidationResult ReadPackingCsv(string path)
        {
            var lines = File.ReadAllLines(path)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();

            if (lines.Count == 0)
                throw new InvalidOperationException("Empty file.");

            var first = SplitCsvLoose(lines[0]);
            if (first.Count == 0)
                throw new InvalidOperationException("Cannot parse first line.");

            PalletMeta? pallet = null;
            int dataStartLineIndex = 0;

            if (string.Equals(first[0], "ID", StringComparison.OrdinalIgnoreCase))
            {
                first = FixMergedZToken(first);

                if (first.Count >= 14)
                {
                    pallet = ParsePalletMetaFromHeaderLine(first);
                }

                dataStartLineIndex = 1;
            }
            else if (string.Equals(first[0], "PALLET", StringComparison.OrdinalIgnoreCase))
            {
                pallet = ParsePalletMetaFromPalletLine(first);
                dataStartLineIndex = 1;
            }
            else
            {
                dataStartLineIndex = 0;
            }

            var boxes = new List<BoxPlacement>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = dataStartLineIndex; i < lines.Count; i++)
            {
                var cols = SplitCsvLoose(lines[i]);
                if (cols.Count == 0) continue;

                if (string.Equals(cols[0], "ID", StringComparison.OrdinalIgnoreCase))
                    continue;

                var box = ParseBoxRow(cols, lineNo: i + 1);

                if (!seenIds.Add(box.Id))
                    throw new InvalidOperationException($"Duplicate box ID '{box.Id}' at line {i + 1}.");

                boxes.Add(box);
            }

            return new ValidationResult { Pallet = pallet, Boxes = boxes };
        }

        public static void ValidateBasic(IEnumerable<BoxPlacement> boxes, ValidationReport report)
        {
            foreach (var b in boxes)
            {
                if (!(b.x < b.X && b.y < b.Y && b.z < b.Z))
                {
                    report.AddError(
                        $"[Basic] ID={b.Id}: non-positive dimensions " +
                        $"x={b.x},X={b.X}; y={b.y},Y={b.Y}; z={b.z},Z={b.Z}");
                }
            }
        }

        public static void ValidateInsidePallet(IEnumerable<BoxPlacement> boxes, PalletMeta pallet)
        {
            foreach (var b in boxes)
            {
                if (b.x < pallet.OriginX || b.y < pallet.OriginY || b.z < pallet.OriginZ)
                    throw new InvalidOperationException(
                        $"Negative/outside origin coords for ID={b.Id}: (x,y,z)=({b.x},{b.y},{b.z}) " +
                        $"origin=({pallet.OriginX},{pallet.OriginY},{pallet.OriginZ}).");

                if (b.X > pallet.OriginX + pallet.SizeL ||
                    b.Y > pallet.OriginY + pallet.SizeW ||
                    b.Z > pallet.OriginZ + pallet.SizeH)
                {
                    throw new InvalidOperationException(
                        $"Out of pallet bounds for ID={b.Id}: (X,Y,Z)=({b.X},{b.Y},{b.Z}) " +
                        $"palletMax=({pallet.OriginX + pallet.SizeL},{pallet.OriginY + pallet.SizeW},{pallet.OriginZ + pallet.SizeH}).");
                }
            }
        }

        public static void ValidateInsidePallet(IEnumerable<BoxPlacement> boxes, PalletMeta pallet, ValidationReport report)
        {
            foreach (var b in boxes)
            {
                if (b.x < pallet.OriginX || b.y < pallet.OriginY || b.z < pallet.OriginZ)
                {
                    report.AddError(
                        $"[Bounds] ID={b.Id}: below origin. " +
                        $"(x,y,z)=({b.x},{b.y},{b.z}) origin=({pallet.OriginX},{pallet.OriginY},{pallet.OriginZ})");
                }

                double maxX = pallet.OriginX + pallet.SizeL;
                double maxY = pallet.OriginY + pallet.SizeW;
                double maxZ = pallet.OriginZ + pallet.SizeH;

                if (b.X > maxX || b.Y > maxY || b.Z > maxZ)
                {
                    report.AddError(
                        $"[Bounds] ID={b.Id}: out of pallet. " +
                        $"(X,Y,Z)=({b.X},{b.Y},{b.Z}) max=({maxX},{maxY},{maxZ})");
                }
            }
        }
        public static void ValidateNoOverlap(IReadOnlyList<BoxPlacement> boxes, ValidationReport report, bool touchIsOk = true)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                var a = boxes[i];
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var b = boxes[j];

                    bool separated =
                        touchIsOk
                            ? (a.X <= b.x) || (b.X <= a.x) || (a.Y <= b.y) || (b.Y <= a.y) || (a.Z <= b.z) || (b.Z <= a.z)
                            : (a.X < b.x) || (b.X < a.x) || (a.Y < b.y) || (b.Y < a.y) || (a.Z < b.z) || (b.Z < a.z);

                    if (!separated)
                    {
                        report.AddError($"[Overlap] '{a.Id}' intersects '{b.Id}'");
                    }
                }
            }
        }

        private static BoxPlacement ParseBoxRow(List<string> cols, int lineNo)
        {
            if (cols.Count < 7)
                throw new InvalidOperationException($"Line {lineNo}: expected at least 7 columns, got {cols.Count}.");

            string id = cols[0].Trim();
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"Line {lineNo}: empty ID.");

            double x = ParseDouble(cols[1], lineNo, "x");
            double y = ParseDouble(cols[2], lineNo, "y");
            double z = ParseDouble(cols[3], lineNo, "z");
            double X = ParseDouble(cols[4], lineNo, "X");
            double Y = ParseDouble(cols[5], lineNo, "Y");
            double Z = ParseDouble(cols[6], lineNo, "Z");

            return new BoxPlacement { Id = id, x = x, y = y, z = z, X = X, Y = Y, Z = Z };
        }

        private static PalletMeta ParsePalletMetaFromHeaderLine(List<string> cols)
        {
            var palletId = cols[7].Trim();

            return new PalletMeta
            {
                PalletId = palletId,
                OriginX = ParseDouble(cols[8], 1, "originX"),
                OriginY = ParseDouble(cols[9], 1, "originY"),
                OriginZ = ParseDouble(cols[10], 1, "originZ"),
                SizeL = ParseDouble(cols[11], 1, "palletL"),
                SizeW = ParseDouble(cols[12], 1, "palletW"),
                SizeH = ParseDouble(cols[13], 1, "palletH")
            };
        }

        private static PalletMeta ParsePalletMetaFromPalletLine(List<string> cols)
        {
            if (cols.Count < 8)
                throw new InvalidOperationException("PALLET line expects 8 columns: PALLET,palletId,ox,oy,oz,L,W,H");

            return new PalletMeta
            {
                PalletId = cols[1].Trim(),
                OriginX = ParseDouble(cols[2], 1, "originX"),
                OriginY = ParseDouble(cols[3], 1, "originY"),
                OriginZ = ParseDouble(cols[4], 1, "originZ"),
                SizeL = ParseDouble(cols[5], 1, "palletL"),
                SizeW = ParseDouble(cols[6], 1, "palletW"),
                SizeH = ParseDouble(cols[7], 1, "palletH")
            };
        }

        private static double ParseDouble(string s, int lineNo, string field)
        {
            if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new InvalidOperationException($"Line {lineNo}: cannot parse '{field}' value '{s}'. Use '.' as decimal separator.");
            return v;
        }
        private static List<string> SplitCsvLoose(string line)
        {
            return line.Split(',')
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 0 || true)
                       .ToList();
        }

        private static List<string> FixMergedZToken(List<string> cols)
        {
            if (cols.Count >= 7 && cols[6].Length > 1 && cols[6].StartsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                var tail = cols[6].Substring(1).Trim();
                if (tail.Length > 0 && tail.All(char.IsDigit))
                {
                    var fixedCols = new List<string>();
                    fixedCols.AddRange(cols.Take(6));
                    fixedCols.Add("Z");
                    fixedCols.Add(tail);
                    fixedCols.AddRange(cols.Skip(7));
                    return fixedCols;
                }
            }
            return cols;
        }
    }

}
