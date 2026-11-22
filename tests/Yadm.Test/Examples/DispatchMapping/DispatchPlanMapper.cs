namespace Yadm.Test;

/// <summary>Maps keyed routes whose nested stops depend on the plan's already-mapped dispatch date.</summary>
public sealed class DispatchPlanMapper : IMapper<DispatchPlanInput, DispatchPlan>
{
    public DispatchPlan? Map(DispatchPlanInput source, MappingContext context)
    {
        var dispatchOn = context.Parse<DateOnly>(source.DispatchOn);
        var routes = context.Required(source.Routes);
        if (context.HasErrors)
            return null; // Do not validate dependent dates against a failed parent parse.

        var mappedRoutes = new Dictionary<string, DeliveryRoute>(StringComparer.Ordinal);
        foreach (var (key, route) in routes)
        {
            if (context.IsStopped)
                break;

            var mapped = context.Map(route, (input, routeContext) =>
            {
                var driver = routeContext.Required(input.Driver);
                var stops = routeContext.MapEach(input.Stops, (stop, stopContext) =>
                {
                    var destination = stopContext.Required(stop.Destination);
                    if (stopContext.TryParse(stop.DeliverOn, out DateOnly deliverOn)
                        && deliverOn < dispatchOn)
                    {
                        stopContext.Reject(stop.DeliverOn, "BeforeDispatch",
                            "Delivery cannot be before the plan's dispatch date.");
                    }

                    return stopContext.HasErrors ? null : new DeliveryStop(destination, deliverOn);
                });

                return routeContext.HasErrors ? null : new DeliveryRoute(driver, stops);
            }, name: $"{nameof(source.Routes)}[{key}]");

            if (mapped is not null)
                mappedRoutes.Add(key, mapped);
        }

        return context.HasErrors ? null : new DispatchPlan(dispatchOn, mappedRoutes);
    }
}

public sealed record DispatchPlanInput(
    string? DispatchOn, IReadOnlyDictionary<string, DeliveryRouteInput?>? Routes);

public sealed record DeliveryRouteInput(string? Driver, IReadOnlyList<DeliveryStopInput>? Stops);

public sealed record DeliveryStopInput(string? Destination, string? DeliverOn);

public sealed record DispatchPlan(DateOnly DispatchOn, IReadOnlyDictionary<string, DeliveryRoute> Routes);

public sealed record DeliveryRoute(string Driver, IReadOnlyList<DeliveryStop> Stops);

public sealed record DeliveryStop(string Destination, DateOnly DeliverOn);
