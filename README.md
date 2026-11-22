# Yadm — Yet Another Data Mapper

Yadm turns input (a request DTO, a file row, a message) into your model and reports **every** problem with its
path, such as `Lines[2].Quantity`, the way validation does. Mapping and validating happen in one step, in plain C#.

Why use it:

- **One small contract.** Implement `IMapper<TSource, TTarget>` and write ordinary code. No conventions or
  configuration to learn.
- **No exceptions for bad input.** Problems become `MappingError`s with a path, a code and a message.
- **Ready for APIs.** `result.ToDictionary()` is the shape `Results.ValidationProblem` expects.
- **Handles real imports.** Look things up asynchronously, keep the valid rows and report the rest.

For JSON input, the separate **Yadm.SystemTextJson** package adds adapters built on System.Text.Json.

See [Yadm and the alternatives](https://github.com/Mjinx/yadm/blob/main/docs/comparison.md) for a short comparison.

```powershell
dotnet add package Yadm
# Optional System.Text.Json adapters:
dotnet add package Yadm.SystemTextJson
```

> **Status:** Yadm is pre-1.0; its API is still being established and may change between 0.x releases.
> The libraries currently target .NET 10 and .NET 11. Bug reports and questions go through
> [GitHub Issues](https://github.com/Mjinx/yadm/issues); support is best-effort, with no response-time promise.

## Quick start

```csharp
using Yadm;

// 1. Write a mapper: read the values, check for errors, then build.
public sealed class AddressMapper : IMapper<AddressDto, Address>
{
    public Address? Map(AddressDto src, MappingContext ctx)
    {
        var street = ctx.Required(src.Street);
        var city = ctx.Required(src.City);
        var postcode = ctx.Required(src.Postcode);
        if (ctx.HasErrors)
            return null;

        return new Address(street, city, postcode);
    }
}

// 2. Run it.
var result = new AddressMapper().Map(dto);

// 3. Use the value, or return the errors.
if (!result.IsSuccess)
    return Results.ValidationProblem(result.ToDictionary()); // { "City": ["A value is required."] }
Address address = result.Value;
```

## Writing mappers

There are three ways to write a mapping. They all receive a `MappingContext` and can be mixed freely.

```csharp
// A class, for mappings you inject and reuse.
public sealed class PetMapper : IMapper<PetDto, Pet>
{
    public Pet? Map(PetDto src, MappingContext ctx)
    {
        var name = ctx.Required(src.Name);
        var age = ctx.Parse<int>(src.Age);
        return ctx.HasErrors ? null : new Pet(name, age);
    }
}

// The source maps itself.
public sealed record AddressDto(string? Street, string? City, string? Postcode) : IMappableObject<Address>
{
    public Address? Map(MappingContext ctx)
    {
        var street = ctx.Required(Street);
        var city = ctx.Required(City);
        var postcode = ctx.Required(Postcode);
        return ctx.HasErrors ? null : new Address(street, city, postcode);
    }
}

// A function, for small or one-off mappings.
var tagMapper = Mapper.Create((string? tag, MappingContext ctx) => ctx.Parse<Tag>(tag));
```

### The one rule: read, check, then build

A helper that finds a problem reports it and returns `default`. So read values into locals, check
`ctx.HasErrors`, and only then use them: before calling methods on them, before I/O, and before calling a
constructor that validates its arguments. Once you have reported an error, return `null`.

### Reading values

| Call | Absent (null, empty, whitespace) | Present but wrong | Returns |
| --- | --- | --- | --- |
| `ctx.Required(src.Name)` | `Required` | — | the value |
| `ctx.Parse<int>(src.Qty)` | `Required` | `Invalid` | `T` |
| `ctx.ParseOptional<int>(src.Qty)` | no error | `Invalid` | `T?` |
| `ctx.Parse(src.Date, (string s, out DateOnly d) => DateOnly.TryParseExact(s, "dd/MM/yyyy", out d))` | `Required` | `Invalid` | `T` |
| `ctx.Lookup(line.Sku, productsBySku)` | `Required` | `NotFound` | the entity |
| `ctx.Reject(line.Quantity, "OutOfRange", "At most 10.")` | — | your code | — |
| `ctx.AddError("EmptyOrder", "An order needs a line.")` | — | your code, at the context's own path | — |

`Parse<T>` works for every `IParsable<T>` type (numbers, `Guid`, dates, your own value objects) and for enums.
Parsing is strict where .NET's defaults would silently misread input: `"1,5"` is not a decimal, and dates must be
ISO 8601. For any other format, pass your own parser, as in the table above. The culture is set with
`MappingOptions.FormatProvider` (invariant by default).

When a rule needs a value before you check `HasErrors`, use the `Try` twin. `TryRequired`, `TryParse` and
`TryLookup` report the same problems and also tell you whether the value is usable:

```csharp
if (ctx.TryParse(src.Quantity, out int quantity) && quantity < 1)
    ctx.Reject(quantity, "OutOfRange", "Order at least one.", nameof(src.Quantity));
```

### Nested objects and collections

```csharp
var shipTo = ctx.Map(src.ShipTo);                         // AddressDto : IMappableObject<Address>
var pet = ctx.Map(src.Pet, petMapper);                    // with a mapper
var billTo = ctx.MapOptional(src.BillTo);                 // null when absent, no error
var lines = ctx.MapEach(src.Lines, (line, c) => new LineRequest(c.Required(line.Sku), c.Parse<int>(line.Quantity)));
```

`Map` and `MapEach` treat a missing source as `Required`. An error inside a nested object or item also counts as
an error of its parent. Inside a function, report to the context it receives (`c`), not to the parent.

### Error paths

The path comes from the expression you pass. Members appear in the path; local variables do not:

| You write | The error's path |
| --- | --- |
| `ctx.Required(src.Street)` | `Street` |
| `ctx.Required(src.Customer.Email)` | `Customer.Email` |
| `item.Parse<int>(line.Quantity)` inside `ctx.MapEach(src.Lines, ...)` | `Lines[2].Quantity` |
| `ctx.Lookup(customerId, customers, nameof(src.CustomerId))` | `CustomerId` (a name you pass is used as given) |
| `ctx.For("ShipTo").Required(src.Street)` | `ShipTo.Street` |

If a value comes from a local variable, pass a `name` (as in the `Lookup` row) or use `ctx.Map(...)` /
`ctx.For(...)` so the parent stays in the path.

## Lookups: asynchronous mappers

When mapping needs data, implement `IAsyncMapper` and write three plain sections: check the input, look up what
it refers to, then build. Shortened from `tests\Yadm.Test\Examples\OrderMapping\OrderMapper.cs`:

```csharp
public sealed class OrderMapper(ICustomerRepository customers, IProductRepository products)
    : IAsyncMapper<OrderInput, Order>
{
    public async ValueTask<Order?> MapAsync(OrderInput src, MappingContext ctx, CancellationToken cancellationToken)
    {
        // 1. Check the input. Every problem is collected with its path.
        var customerId = ctx.Parse<Guid>(src.CustomerId);
        var lines = ctx.MapEach(src.Lines, (line, c) => new LineRequest(c.Required(line.Sku), c.Parse<int>(line.Quantity)));
        if (ctx.HasErrors)
            return null; // Invalid input causes no lookups.

        // 2. Look up what the order refers to, one batch per repository.
        var customerMatches = await customers.FindByIdsAsync([customerId], cancellationToken);
        var productMatches = await products.FindBySkusAsync(lines.Select(l => l.Sku).ToHashSet(), cancellationToken);

        // 3. Build the order.
        var customer = ctx.Lookup(customerId, customerMatches, nameof(src.CustomerId));
        var orderLines = ctx.MapEach(lines, (line, c) =>
        {
            if (!c.TryLookup(line.Sku, productMatches, out var product))
                return null;
            if (line.Quantity > product.MaxPerOrder)
            {
                c.Reject(line.Quantity, "OutOfRange", $"Quantity must be at most {product.MaxPerOrder}.");
                return null;
            }
            return new OrderLine(line.Sku, line.Quantity, Pricing.UnitPrice(product, customer.Tier));
        }, name: nameof(src.Lines));

        return ctx.HasErrors ? null : new Order(customerId, customer, orderLines);
    }
}
```

Exceptions from repositories, including cancellation, propagate unchanged; they are never turned into errors.

The async mapper's dependencies come from its constructor, as above, so register it with your DI container.
There is also `IAsyncMappableObject<TTarget>`, the async twin of `IMappableObject`: the source implements
`ValueTask<TTarget?> MapAsync(MappingContext ctx, CancellationToken ct)` and is run with `source.MapAsync(ct)`.
`MappingContext` is an `IServiceProvider`, so such a source can reach its dependencies
through `ctx.GetService(typeof(IRoomRepository))`, or `ctx.GetRequiredService<IRoomRepository>()` with
`Microsoft.Extensions.DependencyInjection`. The provider is optional and is passed when running:
`source.MapAsync(MappingOptions.Default, serviceProvider, ct)`; without one, `GetService` returns `null`. Nested
contexts share it. For DTOs from a request or file, `IAsyncMapper` with constructor injection is still the simpler fit.

## Running mappings and reading results

```csharp
MappingResult<Pet> pet = petMapper.Map(petDto);                                          // IMapper
MappingResult<Order> order = await orderMapper.MapAsync(orderInput, cancellationToken);  // IAsyncMapper
MappingResult<Address> address = addressDto.Map();                                       // IMappableObject
MappingResult<Pet> first = petMapper.Map(petDto, MappingOptions.FailFast);               // with options
```

A `MappingResult<T>` has `IsSuccess`, `Value` (throws `MappingException` when the mapping failed), `Errors`
(each with `Path`, `Code` and `Message`), `ToDictionary()` for `Results.ValidationProblem`, and `ToErrorDetails()` for an
ordered `Field`/`Code`/`Message` array that omits attempted values.
`MappingOptions` lets you stop at the first error (`FailFast`), cap the error list (200 by default) and set the
parsing culture.

### Keeping the valid items (imports)

By default one failed item fails the whole mapping. To import what you can, skip invalid items: their errors are
still reported, and `PartialValue` holds the result built from the valid ones.

```csharp
var import = Mapper.Create((List<PetDto> rows, MappingContext ctx) => new Shelter(ctx.MapEach(rows, petMapper, skipInvalid: true)));
var result = import.Map(rows);
Log(result.Errors);                    // e.g. [1].Age: The value is not a valid Int32.
Save(result.PartialValue?.Pets ?? []);
```

## Extending: write your own helpers

Helpers are extension methods on `MappingContext`. Forward `name` and `expression` to get the same path rules as
the built-in helpers:

```csharp
public static string Email(this MappingContext context, string? text, string? name = null,
    [CallerArgumentExpression(nameof(text))] string expression = "")
{
    if (!context.TryRequired(text, out var email, name, expression) || email.Contains('@'))
        return email!;

    context.Reject(email, "Email", $"'{email}' is not an email address.", name, expression);
    return null!;
}
```

For a value type you own, implementing `IParsable<T>` is enough: `ctx.Parse<SkuCode>(src.Sku)` just works.

## Dependency injection and testing

Register mappers with the lifetime their dependencies need: a stateless mapper can be a singleton, and one that
uses request-scoped repositories should be scoped:

```csharp
services.AddSingleton<IMapper<AddressInput, Address>, AddressMapper>();
services.AddScoped<IAsyncMapper<OrderInput, Order>, OrderMapper>();
```

In tests, run the mapper and assert on `Errors` by `Path` and `Code`. To stub a service that returns results, use
`MappingResult<T>.Success(value)` or `MappingResult<T>.Failure(errors)`.

## JSON input (Yadm.SystemTextJson)

The adapters take a JSON string, deserialize it with System.Text.Json and pass the result to your mapper:

```csharp
using Yadm.SystemTextJson;

var mapper = new JsonInputMapper<ShipmentInput, Shipment>(
    new ShipmentMapper(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
var result = mapper.Map(json);

// With source-generated metadata instead of reflection:
var generated = new JsonInputMapper<ShipmentInput, Shipment>(context.ShipmentInput, new ShipmentMapper());
```

For mappings that need lookups, use `AsyncJsonInputMapper<TInput, TTarget>` with an `IAsyncMapper`:

```csharp
var mapper = new AsyncJsonInputMapper<ReservationInput, RoomReservation>(
    ReservationJsonContext.Default.ReservationInput, new ReservationMapper(rooms));
var result = await mapper.MapAsync(json, cancellationToken);
```

Malformed JSON becomes an `InvalidJson` error; missing text and JSON `null` report `Required`. Valid JSON goes
through the usual multi-error mapping. Errors never include the raw payload. Runnable examples live in
`tests\Yadm.SystemTextJson.Test\Examples\`.

## More examples

The test projects contain complete, runnable scenarios: `tests\Yadm.Test\Examples\` (orders with batched
lookups, expense forms, stock imports that keep valid rows, parent-dependent dispatch plans, custom helpers) and
`tests\Yadm.SystemTextJson.Test\Examples\`.

## Contributing

Build, test, benchmark and release instructions are in [CONTRIBUTING.md](CONTRIBUTING.md).
