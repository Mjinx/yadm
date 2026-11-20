using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Yadm.Common;

namespace Yadm.SystemTextJson;

/// <summary>Deserializes a JSON string with System.Text.Json, then maps the input in the same context.</summary>
/// <typeparam name="TInput">The deserialized input model.</typeparam>
/// <typeparam name="TTarget">The mapped target.</typeparam>
public sealed class JsonInputMapper<TInput, TTarget> : IMapper<string?, TTarget> where TInput : class
{
    private readonly JsonInput<TInput> _input;
    private readonly IMapper<TInput, TTarget> _mapper;

    /// <summary>Uses the supplied metadata unchanged, including its serializer options and customizations.</summary>
    /// <param name="jsonTypeInfo">Metadata configured with the desired serializer options.</param>
    /// <param name="mapper">Maps the deserialized input.</param>
    public JsonInputMapper(JsonTypeInfo<TInput> jsonTypeInfo, IMapper<TInput, TTarget> mapper)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ArgumentNullException.ThrowIfNull(mapper);
        _input = new JsonInput<TInput>(jsonTypeInfo);
        _mapper = mapper;
    }

    /// <summary>Uses reflection-based System.Text.Json serialization; not trimming or Native AOT safe.</summary>
    /// <param name="mapper">Maps the deserialized input.</param>
    /// <param name="options">Serializer options, copied before use. Defaults to web-style JSON.</param>
    [RequiresDynamicCode("Reflection-based JSON serialization may require runtime code generation.")]
    [RequiresUnreferencedCode(
        "Reflection-based JSON serialization requires the input model's members to be preserved.")]
    public JsonInputMapper(IMapper<TInput, TTarget> mapper, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _input = new JsonInput<TInput>(JsonInput<TInput>.CreateReflectionTypeInfo(options));
        _mapper = mapper;
    }

    /// <summary>Deserializes and maps, reporting bad input without retaining the JSON payload in errors.</summary>
    /// <param name="source">The raw JSON document.</param>
    /// <param name="context">Receives deserialization and mapping errors.</param>
    /// <returns>The mapped target, or default when deserialization fails.</returns>
    public TTarget? Map(string? source, MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _input.TryRead(source, context, out var input) ? _mapper.Map(input, context) : default;
    }
}
