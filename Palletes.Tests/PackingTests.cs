using System.Globalization;
using Palletes.Core;
using Palletes.Generation;
using Palletes.Models;
using Palletes.Utils;
using Xunit;

namespace Palletes.Tests;

public sealed class PackingTests
{
    private readonly record struct TestSku(
        int Sku,
        int Quantity,
        int Length,
        int Width,
        int Height,
        int Weight = 0,
        int Strength = 0,
        int Aisle = 0,
        int Caustic = 0);

    private sealed class TestWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Palletes.Tests", Guid.NewGuid().ToString("N"));

        public TestWorkspace()
        {
            Directory.CreateDirectory(Root);
        }

        public string PathFor(params string[] parts)
        {
            var path = parts.Aggregate(Root, Path.Combine);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for local test artifacts.
            }
        }
    }

    private static PalletSpec DefaultPallet() => new()
    {
        PalletType = "EUR",
        Length = 1200,
        Width = 800,
        MaxHeight = 2000
    };

    private static PalletMeta ToMeta(PalletSpec p) => new()
    {
        PalletId = p.PalletType,
        ContainerId = "",
        OriginX = 0,
        OriginY = 0,
        OriginZ = 0,
        SizeL = p.Length,
        SizeW = p.Width,
        SizeH = p.MaxHeight
    };

    private static ContainerSpec Uk3Container() => new()
    {
        ContainerType = "UK-3",
        Length = 1930,
        Width = 1225,
        Height = 2128
    };

    private static void WriteOrderCsv(string path, params TestSku[] rows)
    {
        var lines = new List<string>
        {
            "1",
            "SKU,Quantity,Length,Width,Height,Weight,Strength,Aisle,Caustic"
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(",",
                row.Sku.ToString(CultureInfo.InvariantCulture),
                row.Quantity.ToString(CultureInfo.InvariantCulture),
                row.Length.ToString(CultureInfo.InvariantCulture),
                row.Width.ToString(CultureInfo.InvariantCulture),
                row.Height.ToString(CultureInfo.InvariantCulture),
                row.Weight.ToString(CultureInfo.InvariantCulture),
                row.Strength.ToString(CultureInfo.InvariantCulture),
                row.Aisle.ToString(CultureInfo.InvariantCulture),
                row.Caustic.ToString(CultureInfo.InvariantCulture),
                ""));
        }

        File.WriteAllLines(path, lines);
    }

    private static void AssertValidPacking(IReadOnlyList<BoxPlacement> placements, PalletSpec pallet)
    {
        var report = new ValidationReport();

        PackingCsvValidator.ValidateBasic(placements, report);
        PackingCsvValidator.ValidateInsidePallet(placements, ToMeta(pallet), report);
        PackingCsvValidator.ValidateNoOverlap(placements, report, touchIsOk: true);

        Assert.True(report.ErrorCount == 0, string.Join("\n", report.Errors));
    }

    private static void AssertValidPacking(ValidationResult result)
    {
        var report = new ValidationReport();

        PackingCsvValidator.ValidateBasic(result.Boxes, report);
        PackingCsvValidator.ValidatePalletsInsideContainers(result.Pallets, result.Containers, report);
        if (result.Pallets.Count > 0)
        {
            PackingCsvValidator.ValidateInsidePallet(result.Boxes, result.Pallets, report);
        }
        else if (result.Pallet is not null)
        {
            PackingCsvValidator.ValidateInsidePallet(result.Boxes, result.Pallet, report);
        }

        PackingCsvValidator.ValidateNoOverlap(result.Boxes, report, touchIsOk: true);
        Assert.True(report.ErrorCount == 0, string.Join("\n", report.Errors));
    }

    [Fact]
    public void Pack_SimpleBoxes_NoOverlap_InsidePallet()
    {
        var pallet = DefaultPallet();

        var boxes = new List<(string Id, int L, int W, int H)>
        {
            ("a", 400, 200, 200),
            ("b", 400, 200, 200),
            ("c", 400, 200, 200),
            ("d", 400, 200, 200),
            ("e", 400, 200, 200),
            ("f", 400, 200, 200),
        };

        var placements = GeneticPalletPacker.Pack(boxes, pallet, seed: 1);

        Assert.Equal(boxes.Count, placements.Count);
        AssertValidPacking(placements, pallet);
    }

    [Fact]
    public void Pack_Cubes50_ShouldStayLowHeight()
    {
        var pallet = DefaultPallet();

        var boxes = new List<(string Id, int L, int W, int H)>(capacity: 50);
        for (int i = 1; i <= 50; i++)
        {
            boxes.Add(($"c{i}", 200, 200, 200));
        }

        var placements = GeneticPalletPacker.Pack(boxes, pallet, seed: 12345);

        Assert.Equal(boxes.Count, placements.Count);
        AssertValidPacking(placements, pallet);

        var height = placements.Max(p => p.Z);
        Assert.True(height <= 800, $"Expected packing height <= 800mm, got {height}mm");
    }

    [Fact]
    public void PackCsv_WritesValidCsv_FromDatasetGeneratorInput()
    {
        var pallet = DefaultPallet();
        using var workspace = new TestWorkspace();

        var generator = new DatasetGenerator(Profile.DefaultRetailLike(), pallet, new Rng(20240504));
        generator.GenerateAll(workspace.Root);

        var inPath = workspace.PathFor("group1", "1", "1.csv");
        var outPath = workspace.PathFor("generated-order-out.csv");
        var expectedBoxes = ItemRow.ParseSimple(inPath).Sum(row => row.Quantity);

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, seed: 7);

        var result = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.NotNull(result.Pallet);
        Assert.Equal(expectedBoxes, result.Boxes.Count);
        AssertValidPacking(result);
    }

    [Fact]
    public void PackCsv_CanSplitAcrossMultiplePallets()
    {
        var pallet = DefaultPallet();
        pallet.MaxHeight = 200;
        using var workspace = new TestWorkspace();

        var inPath = workspace.PathFor("in.csv");
        var outPath = workspace.PathFor("out.csv");
        WriteOrderCsv(inPath, new TestSku(700003, 26, 200, 200, 200));

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, seed: 11);

        var result = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.Equal(26, result.Boxes.Count);
        Assert.Equal(2, result.Pallets.Count);
        AssertValidPacking(result);
    }

    [Fact]
    public void PackCsv_CanPlacePalletsIntoMultipleContainers()
    {
        var pallet = DefaultPallet();
        pallet.MaxHeight = 200;
        var container = Uk3Container();
        using var workspace = new TestWorkspace();

        var inPath = workspace.PathFor("in.csv");
        var outPath = workspace.PathFor("out.csv");
        WriteOrderCsv(inPath, new TestSku(700004, 50, 200, 200, 200));

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, container, seed: 17);

        var result = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.Equal(50, result.Boxes.Count);
        Assert.Equal(3, result.Pallets.Count);
        Assert.Equal(2, result.Containers.Count);
        AssertValidPacking(result);
    }

    [Fact]
    public void PackCsv_RespectsPalletMaxWeight()
    {
        var pallet = DefaultPallet();
        pallet.MaxHeight = 200;
        pallet.MaxWeight = 3000;
        using var workspace = new TestWorkspace();

        var inPath = workspace.PathFor("in.csv");
        var outPath = workspace.PathFor("out.csv");
        WriteOrderCsv(inPath, new TestSku(700005, 4, 200, 200, 200, Weight: 2000, Strength: 5, Aisle: 1));

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, seed: 23);

        var result = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.Equal(4, result.Boxes.Count);
        Assert.Equal(4, result.Pallets.Count);
        AssertValidPacking(result);
    }

    [Fact]
    public void PackCsv_DoesNotStackCausticAndRegularItemsTogether()
    {
        var pallet = DefaultPallet();
        pallet.MaxHeight = 300;
        using var workspace = new TestWorkspace();

        var inPath = workspace.PathFor("in.csv");
        var outPath = workspace.PathFor("out.csv");
        WriteOrderCsv(
            inPath,
            new TestSku(700006, 1, 1200, 800, 100, Weight: 1000, Strength: 5, Aisle: 1),
            new TestSku(700007, 1, 1200, 800, 100, Weight: 1000, Strength: 5, Aisle: 1, Caustic: 1));

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, seed: 29);

        var result = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.Equal(2, result.Boxes.Count);
        Assert.Equal(2, result.Pallets.Count);
        AssertValidPacking(result);
    }
}
