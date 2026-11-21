using Shouldly;
using Yadm.Common;

namespace Yadm.SystemTextJson.Test;

public class ReflectionShipmentTests
{
    [Fact]
    public void MapsAShipmentWithoutASerializerContext()
    {
        var mapper = new JsonInputMapper<ShipmentInput, Shipment>(new ShipmentMapper());

        var result = mapper.Map("""
            {
              "reference": "SHIP-1042",
              "recipient": {
                "name": "Receiving desk",
                "address": {"street": "10 Market Road", "postcode": "SW1A 1AA"}
              },
              "parcels": [{"label": "Equipment", "weightKg": "4.25"}]
            }
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Reference.ShouldBe("SHIP-1042");
        result.Value.Recipient.ShouldBe(new ShipmentRecipient("Receiving desk",
            new ShipmentAddress("10 Market Road", "SW1A 1AA")));
        result.Value.Parcels.ShouldHaveSingleItem().ShouldBe(new Parcel("Equipment", 4.25m));
    }

    [Theory]
    [InlineData("{", "", "InvalidJson")]
    [InlineData("null", "", MappingErrorCodes.Required)]
    [InlineData("""{"recipient":{"address":{}},"parcels":[]}""", "Reference", MappingErrorCodes.Required)]
    public void PreservesJsonAndDomainErrorHandling(string json, string firstPath, string firstCode)
    {
        var mapper = new JsonInputMapper<ShipmentInput, Shipment>(new ShipmentMapper());

        var result = mapper.Map(json);

        result.IsSuccess.ShouldBeFalse();
        result.PartialValue.ShouldBeNull();
        result.Errors[0].Path.ShouldBe(firstPath);
        result.Errors[0].Code.ShouldBe(firstCode);
    }
}
