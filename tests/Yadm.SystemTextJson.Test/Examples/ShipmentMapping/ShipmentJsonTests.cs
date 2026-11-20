using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Yadm.Common;

namespace Yadm.SystemTextJson.Test;

public class ShipmentJsonTests
{
    private static readonly IMapper<string?, Shipment> Shipments = new JsonInputMapper
        <ShipmentInput, Shipment>(
            ShipmentJsonContext.Default.ShipmentInput,
            new ShipmentMapper());

    [Fact]
    public void CallerOptionsApplyToNestedObjectsToo()
    {
        var mapper = new JsonInputMapper<ShipmentInput, Shipment>(
            new ShipmentJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }).ShipmentInput, new ShipmentMapper());

        var result = mapper.Map("""
            {
              "reference": "SHIP-1042",
              "recipient": {
                "name": "Receiving desk",
                "address": {"street": "10 Market Road", "postcode": "SW1A 1AA", "unexpected": true}
              },
              "parcels": []
            }
            """);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("InvalidJson");
        error.Path.ShouldBe("recipient.address.unexpected");
    }

    [Fact]
    public void MapsALayeredShipmentWithARecipientAddressAndMultipleParcels()
    {
        const string Json = """
                            {
                              "reference": "SHIP-1042",
                              "recipient": {
                                "name": "Receiving desk",
                                "address": {"street": "10 Market Road", "postcode": "SW1A 1AA"}
                              },
                              "parcels": [
                                {"label": "Equipment", "weightKg": "4.25"},
                                {"label": "Accessories", "weightKg": "0.75"}
                              ]
                            }
                            """;

        var result = Shipments.Map(Json);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Reference.ShouldBe("SHIP-1042");
        result.Value.Recipient.ShouldBe(new ShipmentRecipient("Receiving desk",
            new ShipmentAddress("10 Market Road", "SW1A 1AA")));
        result.Value.Parcels.ShouldBe([new Parcel("Equipment", 4.25m), new Parcel("Accessories", 0.75m)]);
    }

    [Fact]
    public void CollectsErrorsAcrossTheDeserializedHierarchy()
    {
        const string Json = """
                            {
                              "reference": "SHIP-1042",
                              "recipient": {"name": "", "address": {"street": "10 Market Road"}},
                              "parcels": [
                                {"label": "Equipment", "weightKg": "heavy"},
                                {"label": "Accessories", "weightKg": "0"}
                              ]
                            }
                            """;

        var result = Shipments.Map(Json);

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
        [
            ("Recipient.Name", MappingErrorCodes.Required),
            ("Recipient.Address.Postcode", MappingErrorCodes.Required),
            ("Parcels[0].WeightKg", MappingErrorCodes.Invalid),
            ("Parcels[1].WeightKg", "OutOfRange")
        ]);
        result.PartialValue.ShouldBeNull();
    }

    [Fact]
    public void ReportsMissingNestedObjectsAndCollections()
    {
        var result = Shipments.Map("""{"reference":"SHIP-1042"}""");

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Recipient", MappingErrorCodes.Required), ("Parcels", MappingErrorCodes.Required)]);
    }

    [Fact]
    public void AnEmptyParcelListIsNotAValidShipment()
    {
        var result = Shipments.Map("""
                                   {
                                     "reference": "SHIP-1042",
                                     "recipient": {
                                       "name": "Receiving desk",
                                       "address": {"street": "10 Market Road", "postcode": "SW1A 1AA"}
                                     },
                                     "parcels": []
                                   }
                                   """);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("Parcels");
        error.Code.ShouldBe("EmptyShipment");
    }

    [Fact]
    public void ReportsTheSerializersPathForAnIncompatibleNestedPropertyType()
    {
        var result = Shipments.Map("""{"parcels":[{"weightKg":4.25}]}""");

        var error = result.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("parcels[0].weightKg");
        error.Code.ShouldBe("InvalidJson");
        error.AttemptedValue.ShouldBeNull();
    }
}
