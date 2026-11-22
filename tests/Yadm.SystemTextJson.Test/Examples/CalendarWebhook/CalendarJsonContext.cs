using System.Text.Json.Serialization;

namespace Yadm.SystemTextJson.Test;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CalendarEventInput))]
internal sealed partial class CalendarJsonContext : JsonSerializerContext;
