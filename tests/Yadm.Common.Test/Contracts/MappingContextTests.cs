using Shouldly;

namespace Yadm.Common.Test;

public class MappingContextTests
{
    [Fact]
    public void PathsDescribeWhereEachContextIs()
    {
        TestMapping.Run(context =>
        {
            context.Path.ShouldBe("");
            context.For("ShipTo").Path.ShouldBe("ShipTo");
            context.For("Customer").For("Address").Path.ShouldBe("Customer.Address");
            context.For("Lines", 2).For("Sku").Path.ShouldBe("Lines[2].Sku");
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AddErrorIsLocatedAtTheContext()
    {
        var errors = TestMapping.Errors(context => context.For("Lines", 1).AddError("Duplicate", "Repeats line 0."));

        errors.ShouldHaveSingleItem().ShouldBe(new MappingError("Lines[1]", "Duplicate", "Repeats line 0."));
    }

    [Fact]
    public void RejectIsLocatedAtTheMemberTheArgumentNames()
    {
        var line = new { Quantity = 11 };

        var errors = TestMapping.Errors(context =>
        {
            var lineContext = context.For("Lines", 0);
            lineContext.Reject(line.Quantity, "OutOfRange", "Too many.");
            lineContext.Reject(line.Quantity, "OutOfRange", "Too many.", name: "Amount");
        });

        errors.ShouldBe(
        [
            new MappingError("Lines[0].Quantity", "OutOfRange", "Too many.", 11),
            new MappingError("Lines[0].Amount", "OutOfRange", "Too many.", 11)
        ]);
    }

    [Fact]
    public void ErrorsCountForTheContextAndItsParentsButNotItsSiblings()
    {
        TestMapping.Run(context =>
        {
            var line = context.For("Lines", 0);
            var sku = line.For("Sku");
            var shipTo = context.For("ShipTo");

            sku.AddError("Code", "Message.");

            sku.HasErrors.ShouldBeTrue();
            line.HasErrors.ShouldBeTrue();
            context.HasErrors.ShouldBeTrue();
            shipTo.HasErrors.ShouldBeFalse();
        });
    }

    [Fact]
    public void MaxErrorsCapsTheListButMappingContinuesAndSaysHowManyWereLeftOut()
    {
        var result = TestMapping.Run(context =>
        {
            context.AddError("A", "First.");
            context.AddError("B", "Second.");
            var late = context.For("Late");
            late.AddError("C", "Counted but not kept.");
            late.HasErrors.ShouldBeTrue();
            context.IsStopped.ShouldBeFalse();
            context.AddError("D", "Counted but not kept.");
        }, new MappingOptions { MaxErrors = 2 });

        result.Errors.Select(error => error.Code).ShouldBe(["A", "B", MappingErrorCodes.TooManyErrors]);
        result.Errors[^1].ShouldBe(new MappingError("", MappingErrorCodes.TooManyErrors,
            "Only the first 2 errors are listed; 2 more were found."));
        result.ErrorCount.ShouldBe(4);
        result.HasPartialValue.ShouldBeFalse();
        result.ToString().ShouldBe("Failure: 4 error(s)");
        Should.Throw<MappingException>(() => result.Value).Message.ShouldStartWith("Mapping failed with 4 error(s):");
    }

    [Fact]
    public void ForIndexLocatesItemsOfTheCollectionTheContextDescribes()
    {
        TestMapping.Run(context =>
        {
            context.For(1).Path.ShouldBe("[1]");
            context.For("Lines").For(2).For("Sku").Path.ShouldBe("Lines[2].Sku");
            Should.Throw<ArgumentOutOfRangeException>(() => context.For(-1));
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void FailFastStopsAtTheFirstErrorAndThenEveryContextReportsErrors()
    {
        var src = new { First = "x", Second = "y" };

        var errors = TestMapping.Errors(context =>
        {
            var other = context.For("Other");
            context.Parse<int>(src.First);
            context.IsStopped.ShouldBeTrue();
            other.HasErrors.ShouldBeTrue();
            context.Parse<int>(src.Second);
        }, MappingOptions.FailFast);

        errors.ShouldHaveSingleItem().Path.ShouldBe("First");
    }

    [Fact]
    public void FailFastMetadataReportsTheActualSingleErrorWithoutAPartial()
    {
        var result = TestMapping.Run(context =>
        {
            context.Parse<int>("bad");
            context.Parse<int>("also bad");
        }, MappingOptions.FailFast);

        result.ErrorCount.ShouldBe(1);
        result.Errors.ShouldHaveSingleItem();
        result.HasPartialValue.ShouldBeFalse();
    }

    [Fact]
    public void SkippingAnItemKeepsItsErrorsButClearsThemFromItsParents()
    {
        var result = TestMapping.Run(context =>
        {
            var lines = context.For("Order").For("Lines");
            var item = lines.For("Rows", 1);
            item.For("Sku").AddError("Code", "Message.");

            lines.Skip(item);
            lines.Skip(item);

            item.HasErrors.ShouldBeTrue();
            lines.HasErrors.ShouldBeFalse();
            context.HasErrors.ShouldBeFalse();

            item.AddError("Code", "Reported after skipping.");
            context.HasErrors.ShouldBeFalse();
        });

        result.Errors.Select(error => error.Path).ShouldBe(["Order.Lines.Rows[1].Sku", "Order.Lines.Rows[1]"]);
        result.PartialValue.ShouldBeTrue();
        result.HasPartialValue.ShouldBeTrue();
        result.ErrorCount.ShouldBe(2);
        Should.Throw<NotSupportedException>(() => ((ICollection<MappingError>)result.Errors).Clear());
    }

    [Fact]
    public void NestedSkipsKeepSiblingErrorsAndDoNotLeakLateErrorsToAncestors()
    {
        var result = TestMapping.Run(context =>
        {
            var group = context.For("Group");
            var skipped = group.For("Rows", 0);
            var nested = skipped.For("Nested");
            var sibling = group.For("Rows", 1);
            nested.AddError("Nested", "First.");
            sibling.AddError("Sibling", "Second.");

            context.Skip(group);
            nested.AddError("Late", "Third.");
            skipped.Skip(nested);

            nested.HasErrors.ShouldBeTrue();
            skipped.HasErrors.ShouldBeFalse();
            sibling.HasErrors.ShouldBeTrue();
            group.HasErrors.ShouldBeTrue();
            context.HasErrors.ShouldBeFalse();
        }, new MappingOptions { MaxErrors = 1 });

        result.HasPartialValue.ShouldBeTrue();
        result.ErrorCount.ShouldBe(3);
        result.Errors.Select(error => error.Path).ShouldBe(["Group.Rows[0].Nested", ""]);
        result.Errors[1].Code.ShouldBe(MappingErrorCodes.TooManyErrors);
    }

    [Fact]
    public void ProviderChangesAreSharedWithinOneRunButNotAcrossRuns()
    {
        var service = new object();
        var replacement = new object();
        TestMapping.Run(context =>
        {
            var child = context.For("Child");
            context.InitializeServiceProvider(_ => service);
            child.GetService(typeof(object)).ShouldBeSameAs(service);
            child.InitializeServiceProvider(_ => replacement);
            context.GetService(typeof(object)).ShouldBeSameAs(replacement);
            context.For("Sibling").GetService(typeof(object)).ShouldBeSameAs(replacement);
        }).IsSuccess.ShouldBeTrue();

        TestMapping.Run(context => context.GetService(typeof(object)).ShouldBeNull()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AFailedResultCanRetainAZeroOrNullPartialValue()
    {
        var zero = Mapper.Create((int _, MappingContext context) =>
        {
            var skipped = context.For("Rows", 0);
            skipped.AddError("Code", "Skipped.");
            context.Skip(skipped);
            return 0;
        }).Map(0);
        var absent = Mapper.Create((int _, MappingContext context) =>
        {
            var skipped = context.For("Rows", 0);
            skipped.AddError("Code", "Skipped.");
            context.Skip(skipped);
            return (int?)null;
        }).Map(0);

        zero.IsSuccess.ShouldBeFalse();
        zero.HasPartialValue.ShouldBeTrue();
        zero.PartialValue.ShouldBe(0);
        zero.ErrorCount.ShouldBe(1);
        Should.Throw<MappingException>(() => zero.Value);

        absent.HasPartialValue.ShouldBeTrue();
        absent.PartialValue.ShouldBeNull();
        absent.ErrorCount.ShouldBe(1);
        Should.Throw<MappingException>(() => absent.Value);
    }

    [Fact]
    public void OnlyAContextsOwnChildrenCanBeSkipped()
    {
        TestMapping.Run(context =>
        {
            var grandchild = context.For("Lines", 0).For("Sku");
            Should.Throw<ArgumentException>(() => context.Skip(grandchild));
            Should.Throw<ArgumentException>(() => context.Skip(context));
        });
    }

    [Fact]
    public void HelpersCanReadTheOptions()
    {
        var options = new MappingOptions { MaxErrors = 5 };

        TestMapping.Run(context =>
        {
            context.Options.ShouldBeSameAs(options);
            context.For("Child").Options.ShouldBeSameAs(options);
        }, options);
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        TestMapping.Run(context =>
        {
            Should.Throw<ArgumentException>(() => context.For(" "));
            Should.Throw<ArgumentOutOfRangeException>(() => context.For("Lines", -1));
            Should.Throw<ArgumentException>(() => context.AddError("", "Message."));
            Should.Throw<ArgumentException>(() => context.AddError("Code", " "));
            Should.Throw<ArgumentException>(() => context.Reject(1, "Code", ""));
        }).IsSuccess.ShouldBeTrue();
    }
}
