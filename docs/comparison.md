# Yadm and the alternatives

External data is not always ready to become an application object. An API request or file row might
contain a missing name, an age written as text, or an ID that does not exist. You need to check the
input, convert it, and tell the caller where it went wrong.

Yadm brings those steps together in an explicit C# mapper. It returns either the object or errors with
paths such as `Age` or `Lines[2].Sku`. For lookup-dependent mappings, your mapper can also call a
repository before building the object.

## A simple example

Suppose a registration request has a required name and an age as text. The application needs a
`Registration` with an integer age between 18 and 120.

**With Yadm**, the mapper checks and converts the input before creating the object:

```csharp
public Registration? Map(RegistrationInput source, MappingContext context)
{
    var name = context.Required(source.Name);
    if (context.TryParse(source.Age, out int age) && (age < 18 || age > 120))
        context.Reject(age, "OutOfRange", "Age must be between 18 and 120.", nameof(source.Age));

    return context.HasErrors ? null : new Registration(name, age);
}
```

Calling the mapper returns a result with the registration on success, or errors such as `Age: Invalid`.
See the [complete Yadm and alternative implementations](../benchmarks/Yadm.Benchmark.Scenarios/Registration/Registration.cs).

**With FluentValidation and Mapperly**, a validator first checks the request, then generated mapping
code converts it into the registration. The caller coordinates those steps:

```csharp
var validation = validator.Validate(input);
if (!validation.IsValid)
    return validation.Errors;

var registration = mapper.Map(input);
```

That snippet shows the flow, not a complete method. The validator and mapper must agree on how to
parse age; the linked example includes both. FluentValidation can also perform asynchronous checks,
including lookups, but mapping and coordination remain separate steps.

## Choosing an approach

- **Yadm:** useful when converting untrusted input, checking it, and reporting mapping errors belong
  in one explicit flow, especially with nested data or lookups.
- **FluentValidation:** useful when you want a dedicated, feature-rich validation library. Pair it
  with manual mapping or a mapper such as Mapperly when you also need a different object.
- **Mapperly:** useful for generated, reflection-free object mapping; validation is separate.
- **DataAnnotations:** useful for simple validation attributes built into .NET; mapping is separate.

For a larger example with batched lookups, see the [order-import implementations](../benchmarks/Yadm.Benchmark.Scenarios/OrderImport/YadmOrderImporter.cs).
The [README](../README.md) contains instructions for running the comparisons. Benchmarks measure
specific implementations and workloads, not every use of these libraries.
