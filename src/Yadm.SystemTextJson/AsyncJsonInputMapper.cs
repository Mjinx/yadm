using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Yadm.Common;

namespace Yadm.SystemTextJson;

/// <summary>Deserializes JSON with System.Text.Json, then runs an asynchronous mapper in the same context.</summary>
/// <typeparam name="TInput">The deserialized input model.</typeparam>
/// <typeparam name="TTarget">The mapped target.</typeparam>
public sealed class AsyncJsonInputMapper<TInput, TTarget> : IAsyncMapper<string?, TTarget> where TInput : class
{
    private readonly JsonInput<TInput> _input;
    private readonly IAsyncMapper<TInput, TTarget> _mapper;

    /// <summary>Uses the supplied metadata unchanged, including its serializer options and customizations.</summary>
    /// <param name="jsonTypeInfo">Metadata configured with the desired serializer options.</param>
    /// <param name="mapper">Maps the deserialized input asynchronously.</param>
    public AsyncJsonInputMapper(JsonTypeInfo<TInput> jsonTypeInfo, IAsyncMapper<TInput, TTarget> mapper)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ArgumentNullException.ThrowIfNull(mapper);
        _input = new JsonInput<TInput>(jsonTypeInfo);
        _mapper = mapper;
    }

    /// <summary>Uses reflection-based System.Text.Json serialization; not trimming or Native AOT safe.</summary>
    /// <param name="mapper">Maps the deserialized input asynchronously.</param>
    /// <param name="options">Serializer options, copied before use. Defaults to web-style JSON.</param>
    [RequiresDynamicCode("Reflection-based JSON serialization may require runtime code generation.")]
    [RequiresUnreferencedCode(
        "Reflection-based JSON serialization requires the input model's members to be preserved.")]
    public AsyncJsonInputMapper(IAsyncMapper<TInput, TTarget> mapper, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _input = new JsonInput<TInput>(JsonInput<TInput>.CreateReflectionTypeInfo(options));
        _mapper = mapper;
    }

    /// <summary>Deserializes, then awaits the mapper. Cancellation and mapper exceptions propagate unchanged.</summary>
    /// <param name="source">The raw JSON document.</param>
    /// <param name="context">Receives deserialization and mapping errors.</param>
    /// <param name="cancellationToken">Cancels the operation and is forwarded to the inner mapper.</param>
    /// <returns>The mapped target, or default when deserialization fails.</returns>
    public ValueTask<TTarget?> MapAsync(string? source, MappingContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return _input.TryRead(source, context, out var input)
            ? _mapper.MapAsync(input, context, cancellationToken)
            : ValueTask.FromResult<TTarget?>(default);
    }
}
