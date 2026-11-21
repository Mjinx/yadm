# Yadm — Yet Another Data Mapper

Contract-first mapping for .NET 10 and .NET 11. A mapper turns input (a request DTO, a file row, a message) into
your model and reports **every** problem with its path, such as `Lines[2].Quantity`, the way validation does.

- **One contract.** Implement `IMapper<TSource, TTarget>` and write plain C#.
- **No reflection or conventions.** The library is marked `IsAotCompatible`, so the compiler checks this.
- **No exceptions for bad input.** Problems become `MappingError`s; exceptions mean a bug or an outage.
- **Ready for APIs.** `result.ToDictionary()` is the shape `Results.ValidationProblem` expects.

For JSON input, the separate **Yadm.SystemTextJson** package provides adapters implemented specifically
with **System.Text.Json**. It does not use Newtonsoft.Json or an interchangeable serializer abstraction;
the core `Yadm.Common` library remains independent of serialization.

See [Yadm and the alternatives](docs/comparison.md) for a short example and comparison.

## Quick start

```csharp
using Yadm.Common;

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
constructor that validates its arguments. `HasErrors` covers the context and everything nested in it. Once
you have reported an error, return `null`; the return value is ignored anyway.

(Passing the values straight into a plain record, as in `new LineRequest(c.Required(...), c.Parse<int>(...))`,
is also fine: an object built in a context that has errors is discarded.)

### Reading values

| Call                                                                                                | Absent (null, empty, whitespace) | Present but wrong                    | Returns    |
|-----------------------------------------------------------------------------------------------------|----------------------------------|--------------------------------------|------------|
| `ctx.Required(src.Name)`                                                                            | `Required`                       | —                                    | the value  |
| `ctx.Parse<int>(src.Qty)`                                                                           | `Required`                       | `Invalid`                            | `T`        |
| `ctx.ParseOptional<int>(src.Qty)`                                                                   | no error                         | `Invalid`                            | `T?`       |
| `ctx.Parse(src.Date, (string s, out DateOnly d) => DateOnly.TryParseExact(s, "dd/MM/yyyy", out d))` | `Required`                       | `Invalid`                            | `T`        |
| `ctx.Lookup(line.Sku, productsBySku)`                                                               | `Required`                       | `NotFound`                           | the entity |
| `ctx.Reject(line.Quantity, "OutOfRange", "At most 10.")`                                            | —                                | your code                            | —          |
| `ctx.AddError("EmptyOrder", "An order needs a line.")`                                              | —                                | your code, at the context's own path | —          |

`Parse<T>` and `ParseOptional<T>` work for every `IParsable<T>` type (numbers, `Guid`, dates, your own value
objects) and for enums. Enums are matched ignoring case, must be defined members, and invalid text lists the
accepted names. Parsing uses `MappingOptions.FormatProvider` (invariant culture by default) and is strict where
.NET's defaults would silently misread input:

- Fractional numbers (`decimal`, `double`, `float`) accept no thousands separators, so `"1,5"` is `Invalid`
  rather than 15, and must be finite (`"NaN"` is `Invalid`).
- Dates (`DateTimeOffset`, `DateTime`, `DateOnly`) must be ISO 8601, as in `System.Text.Json`, because
  `"05/11/2026"` means different days in different countries. Without an offset they are read as UTC.

For any other format, pass your own parser, as in the table above.

When a rule needs a value before you check `HasErrors`, use the `Try` twin. `TryRequired`, `TryParse` and
`TryLookup` report the same problems and also tell you whether the value is usable, so a failed parse is not
reported twice:

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
an error of its parent, and a failed item is left out of the returned list. Inside a function, report to the
context it receives (`c`), not to the parent. To nest a mapper, call `ctx.Map(src.Child, mapper)`; calling
`mapper.Map(child, ctx)` yourself maps in the parent's context, without the `Child` path or the missing check.

### Argument order

Every helper takes its arguments in the same order, so you can guess a call you have not seen:

1. The value being mapped. Its expression names the error path: `src.Street`, `line.Sku`, `src.Lines`.
2. How to map it, if the helper needs that: a parser, a dictionary to look up in, a mapper.
3. The `out` result, for the `Try` twins.
4. Options: `skipInvalid`, then `name`.
5. For asynchronous helpers, the `CancellationToken` is last, as in .NET's own async APIs. Pass it right after the
   mapper for a single value (`ctx.MapAsync(src.Pet, petMapper, ct)`), or name it on collection mappings:
   `ctx.MapEachAsync(rows, rowMapper, cancellationToken: ct)`. Use named options for `skipInvalid` and `name`:
   `ctx.MapEachAsync(rows, rowMapper, skipInvalid: true, name: "Rows", cancellationToken: ct)`.

So `ctx.Lookup(line.Sku, products)` reads "look up the SKU in the products", and
`ctx.TryParse(src.Qty, out int qty)` matches `int.TryParse(text, out int result)`. Your own helpers should follow
the same order, with the compiler-filled `expression` parameter last.

### Paths

A path is built from the argument you pass. Names that start with an uppercase letter are members and appear in
the path; variables and parameters do not:

| You write | The error's path |
| --- | --- |
| `ctx.Required(src.Street)` | `Street` (the variable `src` is dropped) |
| `ctx.Required(src.Customer.Email)` | `Customer.Email` |
| `ctx.Required(Street)` inside `IMappableObject.Map` | `Street` |
| `item.Parse<int>(line.Quantity)` inside `ctx.MapEach(src.Lines, ...)` | `Lines[2].Quantity` |
| `ctx.Parse<int>(src.Lines[0].Quantity)` | `Lines[0].Quantity` |
| `ctx.Lookup(customerId, customers)` | the context's own path: `customerId` is a variable |
| `ctx.Lookup(customerId, customers, nameof(src.CustomerId))` | `CustomerId` (a name you pass is used as given) |
| `ctx.For("ShipTo").Required(src.Street)` | `ShipTo.Street` |

Trailing method calls are ignored (`src.Note?.Trim()` is `Note`). Literals, operators such as `a ? b : c`, and
members read from a method's result or at a computed index have no path of their own, so they report at the
context's path. A few consequences to know:

- A collection keeps its name only while its positions are source positions: `ctx.MapEach(src.Rows.Skip(1), ...)`
  reports at `[0]`, `[1]`, ... rather than claiming `Rows[1]` is the second source row.
- Paths come from the text you write: after `var shipTo = src.ShipTo;`, `ctx.Required(shipTo.Street)` reports at
  `Street`. Use `ctx.Map(src.ShipTo, ...)` or `ctx.For("ShipTo")` to keep the parent in the path.
- A `name` containing spaces is rejected with `ArgumentException`: it is usually a message passed by mistake.
  To set a message, use `Reject`.

## Lookups: asynchronous mappers

When mapping needs data, implement `IAsyncMapper` and write three plain sections. Shortened from the tested
example in `tests\Yadm.Common.Test\Examples\OrderMapping\OrderMapper.cs`:

```csharp
public sealed class OrderMapper(ICustomerRepository customers, IProductRepository products)
    : IAsyncMapper<OrderInput, Order>
{
    public async ValueTask<Order?> MapAsync(OrderInput src, MappingContext ctx, CancellationToken cancellationToken)
    {
        // 1. Check the input. Every problem is collected with its path.
        var customerId = ctx.Parse<Guid>(src.CustomerId);
        var placedAt = ctx.Parse<DateTimeOffset>(src.PlacedAt);
        var orderType = ctx.Parse<OrderType>(src.OrderType);
        var shipTo = ctx.Map(src.ShipTo);
        var lines = ctx.MapEach(src.Lines, (line, c) => new LineRequest(c.Required(line.Sku), c.Parse<int>(line.Quantity)));
        if (ctx.HasErrors)
            return null; // Invalid input causes no lookups.

        // 2. Look up what the order refers to, one batch per repository.
        var customerMatches = await customers.FindByIdsAsync([customerId], cancellationToken);
        var productMatches = await products.FindBySkusAsync(lines.Select(l => l.Sku).ToHashSet(), cancellationToken);

        // 3. Build the order.
        var customer = ctx.Lookup(customerId, customerMatches, nameof(src.CustomerId));
        if (ctx.HasErrors)
            return null; // Prices depend on the customer.

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
        if (ctx.HasErrors)
            return null;

        return new Order(customerId, placedAt, orderType, shipTo, orderLines);
    }
}
```

Exceptions from repositories, including cancellation, propagate unchanged; they are never turned into errors.

## Running mappings

```csharp
MappingResult<Pet> pet = petMapper.Map(petDto);                                          // IMapper
MappingResult<Order> order = await orderMapper.MapAsync(orderInput, cancellationToken);  // IAsyncMapper
MappingResult<Address> address = addressDto.Map();                                       // IMappableObject
MappingResult<Pet> first = petMapper.Map(petDto, MappingOptions.FailFast);               // with options
```

| `MappingResult<T>` member | Meaning |
| --- | --- |
| `IsSuccess` | No errors were found. |
| `ErrorCount` | The number of errors found, before the error-list cap. |
| `Value` | The mapped value; throws `MappingException` (listing the errors) when the mapping failed. |
| `Errors` | A read-only snapshot of every retained `MappingError`, in the order found. |
| `ToDictionary()` | Messages grouped by path, for `Results.ValidationProblem` / `ValidationProblemDetails`. |
| `PartialValue` | The value even when the mapping failed: built from the valid items when invalid ones were skipped. |
| `HasPartialValue` | Whether a failed result retained a produced partial value; the value can itself be null or default. |

| `MappingOptions` | Default | Meaning |
| --- | --- | --- |
| `StopOnFirstError` | `false` | Stop at the first error. `MappingOptions.FailFast` sets it. |
| `MaxErrors` | 200 (as in ASP.NET Core) | Errors a result lists. Mapping goes on; a final `TooManyErrors` error says how many more were found. |
| `FormatProvider` | `InvariantCulture` | The culture used to parse numbers and dates. |

### Keeping the valid items (imports)

By default one failed item fails the whole mapping. To import what you can, skip invalid items: their errors are
still reported, and `PartialValue` holds the result built from the valid ones.

```csharp
var import = Mapper.Create((List<PetDto> rows, MappingContext ctx) => new Shelter(ctx.MapEach(rows, petMapper, skipInvalid: true)));
var result = import.Map(rows);
Log(result.Errors);                    // e.g. [1].Age: The value is not a valid Int32.
Save(result.PartialValue?.Pets ?? []);
```

When an import checks rows first and looks them up afterwards, keep each row's position from the first step and
report the second step at that position (`ctx.For(index)` when the rows are the whole source). In your own loop,
call `ctx.Skip(row)` to leave a failed row out (tested in
`NestedMappingTests.ATwoStepImportKeepsTheSourcePositionsOfItsErrors`):

```csharp
var names = ctx.MapEach(src.Rows, (row, index, c) => (Index: index, Name: c.Required(row.Name)), skipInvalid: true);
foreach (var (index, name) in names)
{
    var row = ctx.For(nameof(src.Rows), index);
    if (row.TryLookup(name, known, out var pet, nameof(PetDto.Name)))
        pets.Add(pet);
    else
        ctx.Skip(row);
}
```

## Extending: write your own helpers

Helpers are extension methods on `MappingContext`, built only from its public members (`Reject`, `AddError`,
`For`, `Skip`, `HasErrors`) and the other helpers. Forward `name` and `expression` to get the same path rules as
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

For a value type you own, implementing `IParsable<T>` is enough: `ctx.Parse<SkuCode>(src.Sku)` then just works.

## Dependency injection and testing

Choose a mapper's dependency-injection lifetime from its dependencies. A dependency-free stateless mapper, or
one whose dependencies are safe to share, can be a singleton:
`services.AddSingleton<IMapper<AddressInput, Address>, AddressMapper>()`. A mapper that depends on
request-scoped repositories should have a matching scoped lifetime:
`services.AddScoped<IAsyncMapper<OrderInput, Order>, OrderMapper>()`. Stateless mapper code does not make
scoped or non-thread-safe dependencies safe to share.

In tests, run the mapper and assert on `Errors` by `Path` and `Code`. To test a helper of your own, run it inside a function mapper:
`Mapper.Create((Src src, MappingContext ctx) => ctx.Email(src.Contact)).Map(src)`. To stub a service that returns
results, use `MappingResult<T>.Success(value)` or `MappingResult<T>.Failure(errors)`.

## Guarantees

- A mapping that reported an error never yields `IsSuccess`, and values built in a context with errors are
  discarded.
- A mapper returning `null` for a required value without reporting an error is a bug and throws
  `InvalidOperationException` (`MapOptional` accepts `null`). So does a nested function that reports to its
  parent's context instead of the one it receives.
- With `StopOnFirstError`, built-in helpers stop before further parsing, lookup, or validation work, and no
  further collection items are read. Mapper code and argument evaluation still need explicit `IsStopped` or
  `HasErrors` checks where they perform their own work.
- Cancellation is checked before async mapping starts and before the library advances asynchronous collection
  enumeration. Arbitrary mapper code and blocking source enumerators still need to cooperate with the token.
- A `MappingContext` belongs to one mapping and is not thread-safe.

## Layout

```text
src\Yadm.Common\
  IMapper.cs, IAsyncMapper.cs,
  IMappableObject.cs, IAsyncMappableObject.cs The contracts
  MappingContext.cs, MappingOptions.cs         The state of one mapping
  MappingResult.cs, MappingError.cs,
  MappingException.cs                          The outcome
  Mapper.cs                                    Entry points and Mapper.Create
  Extensions\                                  Required, Parse, Lookup, Map, MapEach and their Try twins
  Internal\                                    Path naming and shared rules
src\Yadm.SystemTextJson\
  JsonInputMapper.cs                           System.Text.Json input followed by synchronous mapping
  AsyncJsonInputMapper.cs                      System.Text.Json input followed by asynchronous mapping
tests\Yadm.SystemTextJson.Test\
  Contracts\                                  Adapter, options, path and cancellation tests
  Examples\CalendarWebhook\                    Flat JSON with generated metadata and reflection options
  Examples\ShipmentMapping\                    Layered JSON with generated metadata and reflection
  Examples\RoomReservation\                    Async mapping with a meeting-room lookup
tests\Yadm.Common.Test\
  Contracts\, Extensions\, Internal\           Library tests
  Examples\OrderMapping\                       An async mapper with batched lookups
  Examples\CalendarMapping\                    An external DTO mapper with a cross-field date rule
  Examples\ExpenseMapping\                     A self-mapping form with all-or-nothing expense lines
  Examples\StockImport\                        A function mapper retaining valid rows and reporting rejects
  Examples\DispatchMapping\                    Keyed routes with stops depending on a mapped parent date
  Examples\CustomHelpers\                      A helper written outside the library
```

### System.Text.Json integration and examples

`tests\Yadm.SystemTextJson.Test\Examples\CalendarWebhook\` demonstrates a flat calendar webhook.
`Examples\ShipmentMapping\` demonstrates a shipment with a recipient, address, and parcel collection.
Each scenario keeps its mapper, serializer context, and tests together, independently of the common
library's examples. The adapters live in the `Yadm.SystemTextJson` namespace:

```csharp
using System.Text.Json;
using Yadm.Common;
using Yadm.SystemTextJson;

var context = new ShipmentJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
var mapper = new JsonInputMapper<ShipmentInput, Shipment>(context.ShipmentInput, new ShipmentMapper());
var result = mapper.Map(json);
```

The metadata overload uses the supplied `JsonTypeInfo<T>` unchanged, preserving customizations. Configure
optional serializer settings when creating your serializer context, rather than overriding metadata with
different options in the adapter. Generated contexts use their own configured defaults.

Only deserialization's `JsonException` becomes `JsonMappingErrorCodes.InvalidJson`; configuration errors
and exceptions from domain mapping still propagate. JSON error paths retain wire property names but omit
the document's `$` prefix, so nesting produces `Payload.parcels[0].weightKg`. Domain errors retain DTO
member paths, such as `Payload.Parcels[0].WeightKg`: naming policies and custom converters mean these are
not necessarily interchangeable. Root JSON errors use the containing context's path.
Deserialization stops at its first error; successfully deserialized input goes through the usual
multi-error mapping. Missing text and JSON `null` report `Required`. Errors do not retain the raw payload
or serializer exception messages.

`Examples\ShipmentMapping\ReflectionShipmentTests.cs` shows the reflection-based alternative,
without a `JsonSerializerContext`:

```csharp
var mapper = new JsonInputMapper<ShipmentInput, Shipment>(
    new ShipmentMapper(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
var result = mapper.Map(json);
```

The reflection overload's options are optional, defaulting to `JsonSerializerOptions.Web`, and are copied
without mutating or freezing the caller's instance. Both constructors share the same mapping and error
handling. The reflection overload is annotated as not trimming/Native AOT safe; use source-generated
metadata for those deployments.

For mappings that need repository lookups, use `AsyncJsonInputMapper<TInput, TTarget>` with an
`IAsyncMapper<TInput, TTarget>`. It offers the same metadata and optional-options reflection constructors:

```csharp
var mapper = new AsyncJsonInputMapper<ReservationInput, RoomReservation>(
    ReservationJsonContext.Default.ReservationInput, new ReservationMapper(rooms));
var result = await mapper.MapAsync(json, cancellationToken);
```

`Examples\RoomReservation\ReservationJsonTests.cs` demonstrates a reservation
message with a meeting-room lookup. Each test project owns its example models and mappers, with no
test-to-test references or linked sources.
Deserialization is synchronous; the inner mapper does the asynchronous work. The cancellation token is
checked before deserialization and forwarded to the inner mapper; cancellation is never an input error.
All adapters work with the existing mapping options and nested mapping helpers.

### Parent-dependent mappings

`Examples\DispatchMapping\DispatchPlanMapper.cs` maps a dictionary of routes containing stop collections.
It parses the plan's dispatch date first, then captures that mapped value in the nested mapping functions
to check each stop's delivery date. No shared mutable mapper state or context data bag is needed.
Each dictionary value is mapped with an explicit name such as `Routes[north]`, producing errors like
`Routes[north].Stops[0].DeliverOn`. Invalid parent input stops dependent mapping; a failed route rejects
the whole plan rather than silently accepting a partial dispatch.

## Order-import comparison

`benchmarks\Yadm.Benchmark.Scenarios\OrderImport\` contains two implementations of the same batch import: one uses Yadm;
the other uses FluentValidation and the Mapperly source generator. Each order parses a customer ID, date,
and line quantities; looks up the customer and a batch of distinct product SKUs; checks product-specific
quantity limits; and calculates prices. Invalid orders are rejected while valid orders are kept. Errors
carry the original order and line positions.

`OrderImportComparisonTests` checks both implementations' output, error paths and codes, lookup behavior,
and fault propagation. The comparison exposes paths and codes, not the libraries' original error messages.

Run the BenchmarkDotNet comparison in Release mode:

```powershell
dotnet run --project .\benchmarks\Yadm.OrderImport.Benchmarks -c Release -- --filter '*'
```

It measures time and allocations for batches of 10 and 100 orders, with valid, invalid-input,
invalid-lookup, and mixed cases. Setup verifies that both implementations produce equivalent orders and
error paths/codes for each case. Reports are written to `BenchmarkDotNet.Artifacts\results\`.

The same benchmark project also compares a single, flat registration request: a required name and an
age parsed as an integer and checked against the range 18–120. It includes valid, invalid, and out-of-range
inputs. Unlike the order import, it has no collections, asynchronous work, or lookups. Run only this
comparison with `--filter '*RegistrationBenchmarks*'`. Both pipelines expose the same value or error
paths and codes; their original error messages can differ.

The benchmark uses prebuilt input and an in-memory lookup that completes synchronously; it does not measure
file parsing, database/network latency, persistence, or startup. Product lookups are batched per order,
not across the whole file. Mapperly maps line inputs after FluentValidation checks them; the surrounding
orchestration is handwritten, as it is for Yadm. These results compare these particular implementations
and workloads, not the libraries in every application.

## Build and test

Install the .NET 11 SDK selected by `global.json` (`11.0.100` or a compatible later feature band) and a
.NET 10 targeting pack, then run from the repository root. The libraries multi-target `net10.0` and
`net11.0`; tests and benchmarks run on `net11.0`.

```powershell
dotnet format .\Yadm.slnx --verify-no-changes --no-restore
dotnet build .\Yadm.slnx
dotnet test --solution .\Yadm.slnx
```

Tests run on Microsoft.Testing.Platform (MTP), not VSTest: `global.json` switches `dotnet test` to MTP mode, and
each test project is an xUnit.net v3 executable, so `dotnet run --project .\tests\Yadm.Common.Test` runs it too.
To collect code coverage with coverlet, as Cobertura and JSON reports in `TestResults\`, run:

```powershell
dotnet test --solution .\Yadm.slnx --coverlet
```

`Directory.Build.props` documents the shared build policy: nullable reference types, the latest C# version the
pinned SDK supports, analyzer level `10.0`, build-time code-style diagnostics, and warnings as errors. The
analyzer level is intentionally configured separately from the library target frameworks.
Compiler output is deterministic; CI source-path normalization is enabled for `CI=true`, GitHub Actions,
and Azure Pipelines. Other CI systems should pass `-p:ContinuousIntegrationBuild=true`.

`.editorconfig` holds the formatting, code-style and naming conventions, and gives every code-style (IDE) and
code-quality (CA) rule an explicit severity, with the reason for each rule that is turned off. A rule at
`warning` fails the build, so the IDE and CI enforce the same rules.

NuGet auditing runs during restore for direct and transitive dependencies at every severity. Vulnerability
findings and audit-feed errors fail restore rather than being suppressed. To refresh the audit even when
dependencies have not changed, run:

```powershell
dotnet restore .\Yadm.slnx --force-evaluate
```

`src\Directory.Build.props` imports the shared root defaults and adds source-only XML documentation and
packaging settings, including the README item. Test projects do not inherit these source-only settings.
Only explicitly opted-in libraries are packable; tests are not. Library builds do not create packages.
Packages include this README, library XML documentation, and a separate `.snupkg` symbol package. Before
publishing, keep the owner, version, license, and repository metadata in `src\Directory.Build.props` aligned
with the repository. SDK-provided Source Link uses the configured Git remote. The release workflow uses the
previous stable package version as the package-validation baseline for patch and minor releases to detect API
compatibility problems. Major releases intentionally permit breaking API changes.

```powershell
dotnet pack .\src\Yadm.Common\Yadm.Common.csproj -c Release -p:Version=0.1.0
dotnet pack .\src\Yadm.SystemTextJson\Yadm.SystemTextJson.csproj -c Release -p:Version=0.1.0
```

## Continuous integration and releases

`ci.yml` runs on pull requests and `main`. It routes by changed files: workflow changes are linted with
actionlint; code changes get a format check, Release build (warnings as errors), tests on `net10.0` and
`net11.0` with coverage (floor in `.github/coverage-baseline.json`) and a vulnerability audit; package-impacting
changes also pack and validate (SourceLink, `.snupkg`, README, license, repository URL). Label a PR
`ci:benchmark` to get a base-vs-head benchmark comment (advisory). Require the check **`CI / required`** in
branch protection (a manual repository setting).

Releases are tag-driven and versioned by MinVer: `git tag vX.Y.Z && git push --tags` on `main`. The release
workflow rebuilds, tests, packs, pushes to the NuGet feed and creates the GitHub release with generated notes.
It publishes to GitHub Packages by default. To publish publicly, set the repository variable `NUGET_SOURCE`
to `https://api.nuget.org/v3/index.json` and the secret `NUGET_API_KEY`; nothing else changes.
Dependabot updates NuGet packages and actions weekly.