using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Shouldly;
using Yadm.Common;

namespace Yadm.SystemTextJson.Test;

public class JsonMapperTests
{
    private static readonly IMapper<MessageInput, string> Inner =
        Mapper.Create((MessageInput input, MappingContext context) => context.Required(input.Name));

    [Fact]
    public void ConstructorsRejectNullDependencies()
    {
        Should.Throw<ArgumentNullException>(() => new JsonInputMapper<MessageInput, string>(null!, Inner));
        Should.Throw<ArgumentNullException>(() =>
            new JsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, null!));
        Should.Throw<ArgumentNullException>(() => new JsonInputMapper<MessageInput, string>(null!));

        var asyncInner = CreateAsyncInner();
        Should.Throw<ArgumentNullException>(() => new AsyncJsonInputMapper<MessageInput, string>(null!, asyncInner));
        Should.Throw<ArgumentNullException>(() =>
            new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, null!));
        Should.Throw<ArgumentNullException>(() => new AsyncJsonInputMapper<MessageInput, string>(null!));
    }

    [Fact]
    public void SuppliedMetadataCustomizationsAreNotReResolved()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = MessageJsonContext.Default.WithAddedModifier(info =>
            {
                if (info.Type == typeof(MessageInput))
                    info.Properties[0].Name = "display_name";
            })
        };
#if NET11_0_OR_GREATER
        var metadata = options.GetTypeInfo<MessageInput>();
#else
        var metadata = (JsonTypeInfo<MessageInput>)options.GetTypeInfo(typeof(MessageInput));
#endif
        var mapper = new JsonInputMapper<MessageInput, string>(metadata, Inner);

        mapper.Map("""{"display_name":"Receiving desk"}""").Value.ShouldBe("Receiving desk");
    }

    [Theory]
    [InlineData("{", "Payload")]
    [InlineData("""{"name":42}""", "Payload.name")]
    public void JsonErrorsAreRelativeToTheContainingContext(string json, string expectedPath)
    {
        var adapter = new JsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, Inner);
        var outer = Mapper.Create((Envelope input, MappingContext context) => context.Map(input.Payload, adapter));

        var result = outer.Map(new Envelope(json));

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe(expectedPath);
        error.Code.ShouldBe("InvalidJson");
        error.AttemptedValue.ShouldBeNull();
    }

    [Fact]
    public void DomainErrorsUseDtoNamesWithinTheContainingContext()
    {
        var adapter = new JsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, Inner);
        var outer = Mapper.Create((Envelope input, MappingContext context) => context.Map(input.Payload, adapter));

        outer.Map(new Envelope("{}")).Errors.ShouldHaveSingleItem().Path.ShouldBe("Payload.Name");
    }

    [Fact]
    public void InvalidJsonDoesNotInvokeTheInnerMapper()
    {
        var inner = Mapper.Create<MessageInput, string>((_, _) =>
            throw new InvalidOperationException("Should not run."));
        var adapter = new JsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);

        adapter.Map("{").Errors.ShouldHaveSingleItem().Code.ShouldBe("InvalidJson");
    }

    [Fact]
    public void AStoppedContextDoesNotDeserializeOrInvokeTheInnerMapper()
    {
        var adapter = new JsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput,
            Mapper.Create<MessageInput, string>((_, _) => throw new InvalidOperationException("Should not run.")));
        var outer = Mapper.Create((string json, MappingContext context) =>
        {
            context.AddError("Earlier", "An earlier error.");
            return adapter.Map(json, context);
        });

        outer.Map("{}", MappingOptions.FailFast).Errors.ShouldHaveSingleItem().Code.ShouldBe("Earlier");
    }

    [Fact]
    public void ValueTypeTargetsAreSupported()
    {
        var adapter = new JsonInputMapper<MessageInput, int>(MessageJsonContext.Default.MessageInput,
            Mapper.Create((MessageInput input, MappingContext context) => context.Parse<int>(input.Name)));

        adapter.Map("""{"name":"42"}""").Value.ShouldBe(42);
        adapter.Map("{").IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncAdaptersSupportMetadataAndReflection(bool reflection)
    {
        var inner = CreateAsyncInner();
        var adapter = reflection
            ? new AsyncJsonInputMapper<MessageInput, string>(inner)
            : new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);

        var result = await adapter.MapAsync("""{"name":"Receiving desk"}""");

        result.Value.ShouldBe("Receiving desk");
    }

    [Fact]
    public async Task AsyncReflectionOptionsAreCopiedAndApplied()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var adapter = new AsyncJsonInputMapper<MessageInput, string>(CreateAsyncInner(), options);

        var result = await adapter.MapAsync("""{"NAME":"Receiving desk"}""");

        result.Value.ShouldBe("Receiving desk");
        options.IsReadOnly.ShouldBeFalse();
        options.TypeInfoResolver.ShouldBeNull();
    }

    [Theory]
    [InlineData("{", "InvalidJson")]
    [InlineData("null", MappingErrorCodes.Required)]
    [InlineData(" ", MappingErrorCodes.Required)]
    public async Task AsyncInvalidInputSkipsTheInnerMapper(string json, string code)
    {
        var inner = Mapper.Create<MessageInput, string>((_, _, _) =>
            throw new InvalidOperationException("Should not run."));
        var adapter = new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);

        var result = await adapter.MapAsync(json);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public async Task AsyncMappingUsesTheSameContextAndForwardsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = Mapper.Create<MessageInput, string>(async (input, context, token) =>
        {
            token.ShouldBe(cancellation.Token);
            await Task.Yield();
            return context.Required(input.Name);
        });
        var adapter = new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);
        var outer = Mapper.Create<Envelope, string>(async (input, context, token) =>
            await context.MapAsync(input.Payload, adapter, cancellationToken: token));

        var result = await outer.MapAsync(new Envelope("{}"), cancellation.Token);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Payload.Name");
    }

    [Fact]
    public async Task PreCanceledRequestsDoNotInvokeTheInnerMapper()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var inner = Mapper.Create<MessageInput, string>((_, _, _) =>
            throw new InvalidOperationException("Should not run."));
        var adapter = new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            adapter.MapAsync("{", cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task AsyncMapperExceptionsAreNotConvertedToJsonErrors()
    {
        var fault = new JsonException("Downstream failure.");
        var inner = Mapper.Create<MessageInput, string>(async (_, _, _) =>
        {
            await Task.Yield();
            throw fault;
        });
        var adapter = new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);

        var exception = await Should.ThrowAsync<JsonException>(() => adapter.MapAsync("{}").AsTask());

        exception.ShouldBeSameAs(fault);
    }

    [Fact]
    public async Task InnerCancellationPreservesTheToken()
    {
        using var cancellation = new CancellationTokenSource();
        var fault = new OperationCanceledException(cancellation.Token);
        var inner = Mapper.Create<MessageInput, string>((_, _, _) => throw fault);
        var adapter = new AsyncJsonInputMapper<MessageInput, string>(MessageJsonContext.Default.MessageInput, inner);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            adapter.MapAsync("{}", cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
    }

    private static IAsyncMapper<MessageInput, string> CreateAsyncInner() =>
        Mapper.Create<MessageInput, string>(async (input, context, _) =>
        {
            await Task.Yield();
            return context.Required(input.Name);
        });
}

public sealed record MessageInput(string? Name);

public sealed record Envelope(string? Payload);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MessageInput))]
internal sealed partial class MessageJsonContext : JsonSerializerContext;
