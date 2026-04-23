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
        public string PalletId { get; init; } = "";
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
        public string ContainerId { get; init; } = "";
        public double OriginX { get; init; }
        public double OriginY { get; init; }
        public double OriginZ { get; init; }
        public double SizeL { get; init; }
        public double SizeW { get; init; }
        public double SizeH { get; init; }
    }

    public sealed class ContainerMeta
    {
        public string ContainerId { get; init; } = "";
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
        public IReadOnlyDictionary<string, PalletMeta> Pallets { get; init; } = new Dictionary<string, PalletMeta>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, ContainerMeta> Containers { get; init; } = new Dictionary<string, ContainerMeta>(StringComparer.Ordinal);
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
            var pallets = new Dictionary<string, PalletMeta>(StringComparer.Ordinal);
            var containers = new Dictionary<string, ContainerMeta>(StringComparer.Ordinal);
            int dataStartLineIndex = 0;

            while (dataStartLineIndex < lines.Count)
            {
                var cols = SplitCsvLoose(lines[dataStartLineIndex]);
                if (cols.Count == 0) break;

                if (string.Equals(cols[0], "CONTAINER", StringComparison.OrdinalIgnoreCase))
                {
                    var parsedContainer = ParseContainerMetaFromContainerLine(cols);
                    containers[parsedContainer.ContainerId] = parsedContainer;
                    dataStartLineIndex++;
                    continue;
                }

                if (!string.Equals(cols[0], "PALLET", StringComparison.OrdinalIgnoreCase))
                    break;

                var parsed = ParsePalletMetaFromPalletLine(cols);
                pallets[parsed.PalletId] = parsed;
                dataStartLineIndex++;
            }

            if (dataStartLineIndex == 0 && string.Equals(first[0], "ID", StringComparison.OrdinalIgnoreCase))
            {
                first = FixMergedZToken(first);

                if (first.Count >= 14)
                {
                    pallet = ParsePalletMetaFromHeaderLine(first);
                    pallets[pallet.PalletId] = pallet;
                }

                dataStartLineIndex = 1;
            }

            if (dataStartLineIndex < lines.Count)
            {
                var maybeHeader = SplitCsvLoose(lines[dataStartLineIndex]);
                if (maybeHeader.Count > 0 && string.Equals(maybeHeader[0], "ID", StringComparison.OrdinalIgnoreCase))
                {
                    dataStartLineIndex++;
                }
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
                if (string.IsNullOrWhiteSpace(box.PalletId) && pallets.Count == 1)
                {
                    var onlyPalletId = pallets.Keys.First();
                    box = new BoxPlacement
                    {
                        Id = box.Id,
                        PalletId = onlyPalletId,
                        x = box.x,
                        y = box.y,
                        z = box.z,
                        X = box.X,
                        Y = box.Y,
                        Z = box.Z
                    };
                }

                if (!seenIds.Add(box.Id))
                    throw new InvalidOperationException($"Duplicate box ID '{box.Id}' at line {i + 1}.");

                boxes.Add(box);
            }

            if (pallet is null && pallets.Count == 1)
            {
                pallet = pallets.Values.First();
            }

            return new ValidationResult { Pallet = pallet, Pallets = pallets, Containers = containers, Boxes = boxes };
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

        public static void ValidateInsidePallet(IEnumerable<BoxPlacement> boxes, IReadOnlyDictionary<string, PalletMeta> pallets, ValidationReport report)
        {
            foreach (var b in boxes)
            {
                if (string.IsNullOrWhiteSpace(b.PalletId))
                {
                    if (pallets.Count == 1)
                    {
                        ValidateInsidePallet(new[] { b }, pallets.Values.First(), report);
                    }
                    else
                    {
                        report.AddError($"[Bounds] ID={b.Id}: pallet id is missing for multi-pallet packing.");
                    }

                    continue;
                }

                if (!pallets.TryGetValue(b.PalletId, out var pallet))
                {
                    report.AddError($"[Bounds] ID={b.Id}: unknown pallet '{b.PalletId}'.");
                    continue;
                }

                ValidateInsidePallet(new[] { b }, pallet, report);
            }
        }

        public static void ValidatePalletsInsideContainers(
            IReadOnlyDictionary<string, PalletMeta> pallets,
            IReadOnlyDictionary<string, ContainerMeta> containers,
            ValidationReport report)
        {
            if (containers.Count == 0) return;

            foreach (var pallet in pallets.Values)
            {
                if (string.IsNullOrWhiteSpace(pallet.ContainerId))
                {
                    report.AddError($"[ContainerBounds] pallet '{pallet.PalletId}' has no container id.");
                    continue;
                }

                if (!containers.TryGetValue(pallet.ContainerId, out var container))
                {
                    report.AddError($"[ContainerBounds] pallet '{pallet.PalletId}' refers to unknown container '{pallet.ContainerId}'.");
                    continue;
                }

                if (pallet.OriginX < container.OriginX || pallet.OriginY < container.OriginY || pallet.OriginZ < container.OriginZ)
                {
                    report.AddError($"[ContainerBounds] pallet '{pallet.PalletId}' starts outside container '{container.ContainerId}'.");
                }

                if (pallet.OriginX + pallet.SizeL > container.OriginX + container.SizeL ||
                    pallet.OriginY + pallet.SizeW > container.OriginY + container.SizeW ||
                    pallet.OriginZ + pallet.SizeH > container.OriginZ + container.SizeH)
                {
                    report.AddError($"[ContainerBounds] pallet '{pallet.PalletId}' exceeds container '{container.ContainerId}' bounds.");
                }
            }

            var groups = pallets.Values
                .Where(p => !string.IsNullOrWhiteSpace(p.ContainerId))
                .GroupBy(p => p.ContainerId, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                var list = group.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        var b = list[j];
                        bool separated =
                            (a.OriginX + a.SizeL <= b.OriginX) || (b.OriginX + b.SizeL <= a.OriginX) ||
                            (a.OriginY + a.SizeW <= b.OriginY) || (b.OriginY + b.SizeW <= a.OriginY) ||
                            (a.OriginZ + a.SizeH <= b.OriginZ) || (b.OriginZ + b.SizeH <= a.OriginZ);

                        if (!separated)
                        {
                            report.AddError($"[ContainerOverlap] pallets '{a.PalletId}' and '{b.PalletId}' overlap in container '{group.Key}'.");
                        }
                    }
                }
            }
        }
        public static void ValidateNoOverlap(IReadOnlyList<BoxPlacement> boxes, ValidationReport report, bool touchIsOk = true)
        {
            var groups = boxes.GroupBy(b => string.IsNullOrWhiteSpace(b.PalletId) ? "\0" : b.PalletId, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var bucket = group.ToList();
                for (int i = 0; i < bucket.Count; i++)
                {
                    var a = bucket[i];
                    for (int j = i + 1; j < bucket.Count; j++)
                    {
                        var b = bucket[j];

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
        }

        private static BoxPlacement ParseBoxRow(List<string> cols, int lineNo)
        {
            if (cols.Count < 7)
                throw new InvalidOperationException($"Line {lineNo}: expected at least 7 columns, got {cols.Count}.");

            string id = cols[0].Trim();
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"Line {lineNo}: empty ID.");

            if (cols.Count >= 8)
            {
                string palletId = cols[1].Trim();
                double x = ParseDouble(cols[2], lineNo, "x");
                double y = ParseDouble(cols[3], lineNo, "y");
                double z = ParseDouble(cols[4], lineNo, "z");
                double X = ParseDouble(cols[5], lineNo, "X");
                double Y = ParseDouble(cols[6], lineNo, "Y");
                double Z = ParseDouble(cols[7], lineNo, "Z");

                return new BoxPlacement { Id = id, PalletId = palletId, x = x, y = y, z = z, X = X, Y = Y, Z = Z };
            }
            else
            {
                double x = ParseDouble(cols[1], lineNo, "x");
                double y = ParseDouble(cols[2], lineNo, "y");
                double z = ParseDouble(cols[3], lineNo, "z");
                double X = ParseDouble(cols[4], lineNo, "X");
                double Y = ParseDouble(cols[5], lineNo, "Y");
                double Z = ParseDouble(cols[6], lineNo, "Z");

                return new BoxPlacement { Id = id, x = x, y = y, z = z, X = X, Y = Y, Z = Z };
            }
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
                ContainerId = cols.Count >= 9 ? cols[8].Trim() : "",
                OriginX = ParseDouble(cols[2], 1, "originX"),
                OriginY = ParseDouble(cols[3], 1, "originY"),
                OriginZ = ParseDouble(cols[4], 1, "originZ"),
                SizeL = ParseDouble(cols[5], 1, "palletL"),
                SizeW = ParseDouble(cols[6], 1, "palletW"),
                SizeH = ParseDouble(cols[7], 1, "palletH")
            };
        }

        private static ContainerMeta ParseContainerMetaFromContainerLine(List<string> cols)
        {
            if (cols.Count < 8)
                throw new InvalidOperationException("CONTAINER line expects 8 columns: CONTAINER,containerId,ox,oy,oz,L,W,H");

            return new ContainerMeta
            {
                ContainerId = cols[1].Trim(),
                OriginX = ParseDouble(cols[2], 1, "originX"),
                OriginY = ParseDouble(cols[3], 1, "originY"),
                OriginZ = ParseDouble(cols[4], 1, "originZ"),
                SizeL = ParseDouble(cols[5], 1, "containerL"),
                SizeW = ParseDouble(cols[6], 1, "containerW"),
                SizeH = ParseDouble(cols[7], 1, "containerH")
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

