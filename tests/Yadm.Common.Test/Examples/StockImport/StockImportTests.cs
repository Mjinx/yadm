using Shouldly;

namespace Yadm.Common.Test;

/// <summary>Maps already-decoded stock-file rows; file parsing and persistence belong to the caller.</summary>
public class StockImportTests
{
    private static readonly IMapper<StockFile, StockSnapshot> Import = Mapper.Create(
        (StockFile file, MappingContext context) =>
        {
            var warehouse = context.Required(file.Warehouse);
            var counts = context.MapEach(file.Rows, (row, rowCtx) =>
            {
                var sku = rowCtx.Required(row.Sku);
                if (rowCtx.TryParse(row.OnHand, out int onHand) && onHand < 0)
                    rowCtx.Reject(row.OnHand, "OutOfRange", "Stock on hand cannot be negative.");

                return rowCtx.HasErrors ? null : new StockCount(sku, onHand);
            }, skipInvalid: true);

            return context.HasErrors ? null : new StockSnapshot(warehouse, counts);
        });

    [Fact]
    public void MapsAStockSnapshotIncludingZeroStock()
    {
        var result = Import.Map(new StockFile("North depot",
            [new StockRow("SKU-1", "12"), new StockRow("SKU-2", "0")]));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warehouse.ShouldBe("North depot");
        result.Value.Counts.ShouldBe([new StockCount("SKU-1", 12), new StockCount("SKU-2", 0)]);
    }

    [Fact]
    public void KeepsValidCountsAndReportsRejectedRowsAtTheirOriginalPositions()
    {
        var file = new StockFile("North depot",
        [
            new StockRow("SKU-1", "12"),
            new StockRow("SKU-2", "twelve"),
            new StockRow("SKU-3", "0"),
            new StockRow(null, "-1")
        ]);

        var result = Import.Map(file);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
        [
            ("Rows[1].OnHand", MappingErrorCodes.Invalid),
            ("Rows[3].Sku", MappingErrorCodes.Required),
            ("Rows[3].OnHand", "OutOfRange")
        ]);
        var accepted = result.PartialValue.ShouldNotBeNull();
        accepted.Warehouse.ShouldBe("North depot");
        accepted.Counts.ShouldBe([new StockCount("SKU-1", 12), new StockCount("SKU-3", 0)]);
        Should.Throw<MappingException>(() => result.Value);
    }

    [Fact]
    public void RejectsTheWholeFileWhenTheWarehouseOrRowsAreMissing()
    {
        var result = Import.Map(new StockFile(null, null));

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Warehouse", MappingErrorCodes.Required), ("Rows", MappingErrorCodes.Required)]);
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void AFileWithNoUsableRowsStillReportsFailure()
    {
        var result = Import.Map(new StockFile("North depot", [new StockRow("SKU-1", "-1")]));

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Rows[0].OnHand");
        result.PartialValue.ShouldNotBeNull().Counts.ShouldBeEmpty();
    }
}

public sealed record StockFile(string? Warehouse, IReadOnlyList<StockRow>? Rows);

public sealed record StockRow(string? Sku, string? OnHand);

public sealed record StockSnapshot(string Warehouse, IReadOnlyList<StockCount> Counts);

public sealed record StockCount(string Sku, int OnHand);
