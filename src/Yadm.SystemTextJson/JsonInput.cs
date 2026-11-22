using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Yadm.SystemTextJson;

internal sealed class JsonInput<TInput>(JsonTypeInfo<TInput> jsonTypeInfo) where TInput : class
{
    [RequiresDynamicCode("Reflection-based JSON serialization may require runtime code generation.")]
    [RequiresUnreferencedCode(
        "Reflection-based JSON serialization requires the input model's members to be preserved.")]
    internal static JsonTypeInfo<TInput> CreateReflectionTypeInfo(JsonSerializerOptions? options)
    {
        var configured = new JsonSerializerOptions(options ?? JsonSerializerOptions.Web)
        {
            TypeInfoResolver = options?.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()
        };
        configured.MakeReadOnly();
#if NET11_0_OR_GREATER
        return configured.GetTypeInfo<TInput>();
#else
        return (JsonTypeInfo<TInput>)configured.GetTypeInfo(typeof(TInput));
#endif
    }

    internal bool TryRead(string? source, MappingContext context, [NotNullWhen(true)] out TInput? input)
    {
        input = null;
        if (context.IsStopped)
            return false;

        if (string.IsNullOrWhiteSpace(source))
        {
            context.AddError(MappingErrorCodes.Required, "A JSON document is required.");
            return false;
        }

        try
        {
            input = JsonSerializer.Deserialize(source, jsonTypeInfo);
        }
        catch (JsonException exception)
        {
            // JSON paths retain wire names; remove only the document root so nesting composes correctly.
            var path = exception.Path;
            if (path?.StartsWith("$.", StringComparison.Ordinal) == true)
                path = path[2..];
            else if (path?.StartsWith('$') == true)
                path = path[1..];

            var errorContext = string.IsNullOrEmpty(path) ? context : context.For(path);
            errorContext.AddError(JsonMappingErrorCodes.InvalidJson, "The JSON document could not be read.");
            return false;
        }

        if (input is not null)
            return true;

        context.AddError(MappingErrorCodes.Required, "A non-null JSON value is required.");
        return false;
    }
}
