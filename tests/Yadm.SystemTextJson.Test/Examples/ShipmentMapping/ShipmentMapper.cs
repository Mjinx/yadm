using Yadm;

namespace Yadm.SystemTextJson.Test;

public sealed class ShipmentMapper : IMapper<ShipmentInput, Shipment>
{
    public Shipment? Map(ShipmentInput source, MappingContext context)
    {
        var reference = context.Required(source.Reference);
        var recipient = context.Map(source.Recipient);
        var parcels = context.MapEach(source.Parcels);
        if (source.Parcels is { Count: 0 })
            context.Reject(source.Parcels, "EmptyShipment", "Include at least one parcel.");

        return context.HasErrors ? null : new Shipment(reference, recipient, parcels);
    }
}

public sealed record ShipmentInput(
    string? Reference, ShipmentRecipientInput? Recipient, IReadOnlyList<ParcelInput>? Parcels);

public sealed record ShipmentRecipientInput(string? Name, ShipmentAddressInput? Address) : IMappableObject<ShipmentRecipient>
{
    public ShipmentRecipient? Map(MappingContext context)
    {
        var name = context.Required(Name);
        var address = context.Map(Address);
        return context.HasErrors ? null : new ShipmentRecipient(name, address);
    }
}

public sealed record ShipmentAddressInput(string? Street, string? Postcode) : IMappableObject<ShipmentAddress>
{
    public ShipmentAddress? Map(MappingContext context)
    {
        var street = context.Required(Street);
        var postcode = context.Required(Postcode);
        return context.HasErrors ? null : new ShipmentAddress(street, postcode);
    }
}

public sealed record ParcelInput(string? Label, string? WeightKg) : IMappableObject<Parcel>
{
    public Parcel? Map(MappingContext context)
    {
        var label = context.Required(Label);
        if (context.TryParse(WeightKg, out decimal weight) && weight <= 0)
            context.Reject(WeightKg, "OutOfRange", "Parcel weight must be greater than zero.");

        return context.HasErrors ? null : new Parcel(label, weight);
    }
}

public sealed record Shipment(string Reference, ShipmentRecipient Recipient, IReadOnlyList<Parcel> Parcels);

public sealed record ShipmentRecipient(string Name, ShipmentAddress Address);

public sealed record ShipmentAddress(string Street, string Postcode);

public sealed record Parcel(string Label, decimal WeightKg);
