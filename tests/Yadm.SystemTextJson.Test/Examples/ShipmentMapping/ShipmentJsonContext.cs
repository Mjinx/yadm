using System.Text.Json.Serialization;

namespace Yadm.SystemTextJson.Test;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShipmentInput))]
internal sealed partial class ShipmentJsonContext : JsonSerializerContext;
