using System.Text.Json.Serialization;

namespace Yadm.SystemTextJson.Test;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReservationInput))]
internal sealed partial class ReservationJsonContext : JsonSerializerContext;
