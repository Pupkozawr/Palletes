using System.Globalization;
using Palletes;
using Xunit;

namespace Palletes.Tests;

public sealed class PackingTests
{
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

    private static void AssertValidPacking(IReadOnlyList<BoxPlacement> placements, PalletSpec pallet)
    {
        var report = new ValidationReport();

        PackingCsvValidator.ValidateBasic(placements, report);
        PackingCsvValidator.ValidateInsidePallet(placements, ToMeta(pallet), report);
        PackingCsvValidator.ValidateNoOverlap(placements, report, touchIsOk: true);

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

        // 50 cubes 200x200x200: should fit in <= 4 layers (<= 800mm height) on 1200x800.
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
    public void PackCsv_WritesValidCsv_FromGeneratedInput()
    {
        var pallet = DefaultPallet();

        var tempDir = Path.Combine(Path.GetTempPath(), "Palletes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var inPath = Path.Combine(tempDir, "in.csv");
        var outPath = Path.Combine(tempDir, "out.csv");

        // Format compatible with ItemRow.ParseSimple.
        var lines = new List<string>
        {
            "1",
            "SKU,Quantity,Length,Width,Height,Weight,Strength,Aisle,Caustic",
            // 12 cubes fill 1 layer exactly: 6x4 on 1200x800.
            string.Join(",", 700001, 12, 200, 200, 200, 0, 0, 0, 0, ""),
            // plus a few more cubes for extra layers.
            string.Join(",", 700002, 10, 200, 200, 200, 0, 0, 0, 0, ""),
        };

        File.WriteAllLines(inPath, lines);

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, seed: 7);

        Assert.True(File.Exists(outPath));

        var vr = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.NotNull(vr.Pallet);
        Assert.Equal(22, vr.Boxes.Count);

        var report = new ValidationReport();
        PackingCsvValidator.ValidateBasic(vr.Boxes, report);
        PackingCsvValidator.ValidateInsidePallet(vr.Boxes, vr.Pallet!, report);
        PackingCsvValidator.ValidateNoOverlap(vr.Boxes, report, touchIsOk: true);
        Assert.True(report.ErrorCount == 0, string.Join("\n", report.Errors));
    }

    [Fact]
    public void PackCsv_CanSplitAcrossMultiplePallets()
    {
        var pallet = DefaultPallet();
        pallet.MaxHeight = 200;

        var tempDir = Path.Combine(Path.GetTempPath(), "Palletes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var inPath = Path.Combine(tempDir, "in.csv");
        var outPath = Path.Combine(tempDir, "out.csv");

        var lines = new List<string>
        {
            "1",
            "SKU,Quantity,Length,Width,Height,Weight,Strength,Aisle,Caustic",
            string.Join(",", 700003, 26, 200, 200, 200, 0, 0, 0, 0, ""),
        };

        File.WriteAllLines(inPath, lines);

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, seed: 11);

        var vr = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.Equal(26, vr.Boxes.Count);
        Assert.Equal(2, vr.Pallets.Count);

        var report = new ValidationReport();
        PackingCsvValidator.ValidateBasic(vr.Boxes, report);
        PackingCsvValidator.ValidateInsidePallet(vr.Boxes, vr.Pallets, report);
        PackingCsvValidator.ValidateNoOverlap(vr.Boxes, report, touchIsOk: true);
        Assert.True(report.ErrorCount == 0, string.Join("\n", report.Errors));
    }

    [Fact]
    public void PackCsv_CanPlacePalletsIntoMultipleContainers()
    {
        var pallet = DefaultPallet();
        pallet.MaxHeight = 200;
        var container = Uk3Container();

        var tempDir = Path.Combine(Path.GetTempPath(), "Palletes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var inPath = Path.Combine(tempDir, "in.csv");
        var outPath = Path.Combine(tempDir, "out.csv");

        var lines = new List<string>
        {
            "1",
            "SKU,Quantity,Length,Width,Height,Weight,Strength,Aisle,Caustic",
            string.Join(",", 700004, 50, 200, 200, 200, 0, 0, 0, 0, ""),
        };

        File.WriteAllLines(inPath, lines);

        GeneticPalletPacker.PackCsv(inPath, outPath, pallet, container, seed: 17);

        var vr = PackingCsvValidator.ReadPackingCsv(outPath);
        Assert.Equal(50, vr.Boxes.Count);
        Assert.Equal(3, vr.Pallets.Count);
        Assert.Equal(2, vr.Containers.Count);

        var report = new ValidationReport();
        PackingCsvValidator.ValidateBasic(vr.Boxes, report);
        PackingCsvValidator.ValidatePalletsInsideContainers(vr.Pallets, vr.Containers, report);
        PackingCsvValidator.ValidateInsidePallet(vr.Boxes, vr.Pallets, report);
        PackingCsvValidator.ValidateNoOverlap(vr.Boxes, report, touchIsOk: true);
        Assert.True(report.ErrorCount == 0, string.Join("\n", report.Errors));
    }
}
