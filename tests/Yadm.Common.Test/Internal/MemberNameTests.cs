using Shouldly;

namespace Yadm.Common.Test;

public class MemberNameTests
{
    [Theory]
    [InlineData("src.Street", "Street")]
    [InlineData("src.ShipTo.Street", "ShipTo.Street")]
    [InlineData("this.Street", "Street")]
    [InlineData("Street", "Street")]
    [InlineData("ShipTo.Street", "ShipTo.Street")]
    [InlineData("src!.Lines!", "Lines")]
    [InlineData("src?.ShipTo?.Street", "ShipTo.Street")]
    [InlineData("src.Lines[0].Sku", "Lines[0].Sku")]
    [InlineData("src.Lines?[0]!.Sku", "Lines[0].Sku")]
    [InlineData("row[1]", "[1]")]
    [InlineData("items[0].Name", "[0].Name")]
    [InlineData("src\r\n    .Customer!\r\n    .Email", "Customer.Email")]
    [InlineData("src.Note?.Trim()", "Note")]
    [InlineData("src.Name.Trim().ToUpperInvariant()", "Name")]
    [InlineData("src.Note ?? \"\"", "Note")]
    [InlineData("src.Note?.Trim() ?? \"\"", "Note")]
    [InlineData("  src.Street  ", "Street")]
    public void UsesMembersAndDropsVariables(string expression, string expected) =>
        MemberName.From(null, expression).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("customerId")]
    [InlineData("tag")]
    [InlineData("_street")]
    [InlineData("this")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("default")]
    [InlineData("new Address()")]
    [InlineData("GetName()")]
    [InlineData("src.Get(\"a.b\")")]
    [InlineData("Parse<int>(text)")]
    [InlineData("(int?)src.Age")]
    public void VariablesLiteralsAndCallsAddNothing(string expression) =>
        MemberName.From(null, expression).ShouldBeNull();

    [Theory]
    [InlineData("src.Lines.First().Sku")]
    [InlineData("src.Lines[i].Sku")]
    [InlineData("src.Find(\"x\")[0]")]
    [InlineData("src.HasWork ? src.Work : src.Home")]
    [InlineData("src.Count + 1")]
    [InlineData("src.Age != null")]
    public void UnknownPathsAddNothingRatherThanAWrongMember(string expression) =>
        MemberName.From(null, expression).ShouldBeNull();

    [Theory]
    [InlineData("src.Rows", "Rows")]
    [InlineData("src.Rows.ToList()", "Rows")]
    [InlineData("src.Rows!.ToArray()", "Rows")]
    [InlineData("src.Rows.Skip(1)", null)]
    [InlineData("src.Rows.Where(row => row.Ok)", null)]
    [InlineData("src.Rows.OrderBy(row => row.Name).ToList()", null)]
    public void ACollectionIsNamedOnlyWhileItsPositionsAreSourcePositions(string expression, string? expected) =>
        MemberName.ForCollection(null, expression).ShouldBe(expected);

    [Theory]
    [InlineData("ShipTo.Street")]
    [InlineData("Lines[0]")]
    [InlineData("first-name")]
    public void ExplicitNamesAreUsedAsGiven(string name) =>
        MemberName.From(name, "src.Other").ShouldBe(name);

    [Fact]
    public void AnEmptyExplicitNameMeansTheContextItself() =>
        MemberName.From("", "src.Other").ShouldBeNull();

    [Fact]
    public void AMessagePassedAsTheNameIsRejected() =>
        Should.Throw<ArgumentException>(() => MemberName.From("Email is required.", "src.Email"))
            .ParamName.ShouldBe("name");
}
