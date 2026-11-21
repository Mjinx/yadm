using Shouldly;

namespace Yadm.Common.Test;

public class AsyncSelfMappingTests
{
    [Fact]
    public void AsyncSelfMappingDoesNotInheritSynchronousSelfMapping()
    {
        typeof(IAsyncMappableObject<Target>).GetInterfaces()
            .ShouldNotContain(typeof(IMappableObject<Target>));
    }

    [Fact]
    public async Task AsyncOnlyObjectsMapAtTheRootWithOptionsAndToken()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new AsyncSelfInput("root", (context, token) =>
            ValueTask.FromResult<Target?>(new Target(context.Path, context.Options, token)));

        var result = await source.MapAsync(MappingOptions.FailFast, cancellation.Token);

        result.Value.ShouldBe(new Target("", MappingOptions.FailFast, cancellation.Token));
        source.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task AnAsyncRunnerDoesNotCallTheSynchronousContract()
    {
        var source = new DualSelfInput();

        var result = await source.MapAsync();

        result.Value.Name.ShouldBe("async");
        source.SyncCalls.ShouldBe(0);
        source.AsyncCalls.ShouldBe(1);
    }

    [Fact]
    public async Task SelfMappingsSupportNestedOptionalAndCollectionPaths()
    {
        var src = new
        {
            Child = Item("child"),
            Missing = (AsyncSelfInput?)null,
            Items = new[] { Item("first"), Item("bad"), Item("third") }
        };

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            var child = await context.MapAsync(src.Child, token);
            (await context.MapOptionalAsync(src.Missing, token)).ShouldBeNull();
            var items = await context.MapEachAsync(src.Items, cancellationToken: token);
            return new Batch(child, items);
        }).MapAsync(null);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Items[1].Value");
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public async Task MissingRequiredNestedAndCollectionValuesAreReportedWithoutCalls()
    {
        var src = new
        {
            Child = (AsyncSelfInput?)null,
            Optional = (AsyncSelfInput?)null,
            Items = (IEnumerable<AsyncSelfInput>?)null
        };

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            await context.MapAsync(src.Child, token);
            await context.MapOptionalAsync(src.Optional, token);
            await context.MapEachAsync(src.Items, cancellationToken: token);
            return true;
        }).MapAsync(null);

        result.Errors.Select(error => error.Path).ShouldBe(["Child", "Items"]);
    }

    [Fact]
    public async Task RequiredAndRootNullProducersAreRejectedButOptionalNullIsAllowed()
    {
        var nullSource = Item(null);

        await Should.ThrowAsync<InvalidOperationException>(() => nullSource.MapAsync().AsTask());

        await Should.ThrowAsync<InvalidOperationException>(() => Mapper.Create(
                async (object? _, MappingContext context, CancellationToken token) =>
                {
                    await context.MapAsync(nullSource, token);
                    return true;
                }).MapAsync(null).AsTask());

        var optional = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            (await context.MapOptionalAsync(nullSource, token)).ShouldBeNull();
            return true;
        }).MapAsync(null);
        optional.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ReportingToTheParentInsteadOfTheChildIsRejected()
    {
        var source = new AsyncSelfInput("wrong", (_, token) =>
            ValueTask.FromResult<Target?>(new Target("wrong", MappingOptions.Default, token)));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => Mapper.Create(
                async (object? _, MappingContext context, CancellationToken token) =>
                {
                    source.Map = (child, cancellationToken) =>
                    {
                        context.AddError("WrongContext", "Reported to the parent.");
                        return ValueTask.FromResult<Target?>(new Target(child.Path, child.Options, cancellationToken));
                    };
                    await context.MapAsync(source, token);
                    return true;
                }).MapAsync(null).AsTask());

        exception.Message.ShouldStartWith("AsyncSelfInput reported 1 error(s) to a different MappingContext.");
    }

    [Fact]
    public async Task SkipInvalidKeepsACollectionAsAPartialValue()
    {
        var src = new { Items = new[] { Item("first"), Item("bad"), Item("third") } };

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
            new Batch(new Target("root", context.Options, token),
                await context.MapEachAsync(src.Items, skipInvalid: true, cancellationToken: token)))
            .MapAsync(null);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Items[1].Value");
        result.PartialValue.ShouldNotBeNull().Items.Select(item => item.Name)
            .ShouldBe(["first", "third"]);
    }

    [Fact]
    public async Task FailFastStopsCollectionWorkAfterTheFirstError()
    {
        var items = new[] { Item("first"), Item("bad"), Item("third") };

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
            {
                await context.MapEachAsync(items, cancellationToken: token);
                return true;
            })
            .MapAsync(null, MappingOptions.FailFast);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("[1].Value");
        items.Select(item => item.Calls).ShouldBe([1, 1, 0]);
    }

    [Fact]
    public async Task ErrorOutputIsCappedWhileSelfMappingsContinue()
    {
        var items = Enumerable.Range(0, 5).Select(_ => Item("bad")).ToArray();

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
            {
                await context.MapEachAsync(items, skipInvalid: true, cancellationToken: token);
                return true;
            })
            .MapAsync(null, new MappingOptions { MaxErrors = 2 });

        result.Errors.Select(error => error.Path).ShouldBe(["[0].Value", "[1].Value", ""]);
        result.Errors[^1].Code.ShouldBe(MappingErrorCodes.TooManyErrors);
        items.ShouldAllBe(item => item.Calls == 1);
    }

    [Fact]
    public async Task TokensAreForwardedByEveryAsyncSelfMappingEntryPoint()
    {
        using var cancellation = new CancellationTokenSource();
        var child = Item("child");
        var optional = Item("optional");
        var items = new[] { Item("item") };

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            await context.MapAsync(child, "NamedChild", token);
            await context.MapOptionalAsync(optional, "NamedOptional", token);
            await context.MapEachAsync(items, name: "NamedItems", cancellationToken: token);
            return true;
        }).MapAsync(null, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        child.LastToken.ShouldBe(cancellation.Token);
        optional.LastToken.ShouldBe(cancellation.Token);
        items[0].LastToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task CancellationFromAnAsyncSelfMappingIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        var source = Item("root");
        source.Map = async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return null;
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => source.MapAsync(cancellation.Token).AsTask());
        source.LastToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task PreCanceledAsyncSelfMappingDoesNotInvokeTheObject()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var source = Item("root");

        var exception = await Should.ThrowAsync<OperationCanceledException>(
            () => source.MapAsync(cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
        source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task AsyncSelfCollectionDefaultUsesTheSingleCanonicalOptionsOverload()
    {
        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken _) =>
        {
            var mapped = await context.MapEachAsync([Item("root")], default);
            return mapped.Single();
        }).MapAsync(null);

        result.Value.Name.ShouldBe("root");
    }

    private static AsyncSelfInput Item(string? value) =>
        new(value, (context, token) =>
        {
            if (value is null)
                return ValueTask.FromResult<Target?>(null);

            var mapped = value == "bad"
                ? ThrowInvalid(context, value)
                : context.Required(value, nameof(AsyncSelfInput.Value));
            return ValueTask.FromResult<Target?>(context.HasErrors
                ? null
                : new Target(mapped, context.Options, token));
        });

    private static string ThrowInvalid(MappingContext context, string value)
    {
        context.Reject(value, MappingErrorCodes.Invalid, "Invalid value.", nameof(AsyncSelfInput.Value));
        return null!;
    }

    private sealed class AsyncSelfInput : IAsyncMappableObject<Target>
    {
        public AsyncSelfInput(string? value,
            Func<MappingContext, CancellationToken, ValueTask<Target?>>? map = null)
        {
            Value = value;
            Map = map ?? ((context, token) =>
            {
                var mapped = context.Required(Value, nameof(Value));
                return ValueTask.FromResult<Target?>(context.HasErrors
                    ? null
                    : new Target(mapped, context.Options, token));
            });
        }

        public string? Value { get; }
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Func<MappingContext, CancellationToken, ValueTask<Target?>> Map { get; set; }

        public ValueTask<Target?> MapAsync(MappingContext context, CancellationToken cancellationToken)
        {
            Calls++;
            LastToken = cancellationToken;
            return Map(context, cancellationToken);
        }
    }

    private sealed class DualSelfInput : IMappableObject<Target>, IAsyncMappableObject<Target>
    {
        public int SyncCalls { get; private set; }
        public int AsyncCalls { get; private set; }

        public Target Map(MappingContext context)
        {
            SyncCalls++;
            return new Target("sync", context.Options, CancellationToken.None);
        }

        public ValueTask<Target?> MapAsync(MappingContext context, CancellationToken cancellationToken)
        {
            AsyncCalls++;
            return ValueTask.FromResult<Target?>(new Target("async", context.Options, cancellationToken));
        }
    }

    private sealed record Target(string Name, MappingOptions Options, CancellationToken Token);

    private sealed record Batch(Target Child, IReadOnlyList<Target> Items);
}
