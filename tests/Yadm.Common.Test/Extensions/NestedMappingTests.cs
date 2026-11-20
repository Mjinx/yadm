using Shouldly;
using static Yadm.Common.MappingErrorCodes;

namespace Yadm.Common.Test;

public class NestedMappingTests
{
    [Fact]
    public void MapLocatesTheChildsErrorsUnderItsMember()
    {
        var src = new { Pet = new PetInput("Rex", "old") };

        var errors = TestMapping.Errors(context => context.Map(src.Pet, new PetMapper()).ShouldBeNull());

        errors.ShouldHaveSingleItem().ShouldBe(new MappingError("Pet.Age", Invalid, "'old' is not a valid Int32.", "old"));
    }

    [Fact]
    public void MapReportsAMissingSourceAsRequiredWithoutCallingTheMapper()
    {
        var src = new { Pet = (PetInput?)null };
        var mapper = new PetMapper();

        var errors = TestMapping.Errors(context => context.Map(src.Pet, mapper));

        errors.ShouldHaveSingleItem().ShouldBe(new MappingError("Pet", Required, "A value is required."));
        mapper.Calls.ShouldBe(0);
    }

    [Fact]
    public void MapAcceptsAMapperAFunctionOrAnObjectThatMapsItself()
    {
        var src = new { Pet = new PetInput("Rex", "3"), Home = new AddressInput("1 High Street", "London", "SW1A 1AA") };

        TestMapping.Run(context =>
        {
            context.Map(src.Pet, new PetMapper()).ShouldBe(new Pet("Rex", 3));
            context.Map(src.Pet, (pet, c) => c.Required(pet.Name)).ShouldBe("Rex");
            context.Map(src.Home).ShouldBe(new Address("1 High Street", "London", "SW1A 1AA"));
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AChildsErrorFailsItsParentButNotItsSiblings()
    {
        var src = new { Home = new AddressInput(null, "London", "SW1A 1AA"), Pet = new PetInput("Rex", "3") };

        TestMapping.Run(context =>
        {
            var owner = context.For("Owner");
            owner.Map(src.Home);
            var pets = context.For("Pets");
            pets.Map(src.Pet, new PetMapper());

            owner.HasErrors.ShouldBeTrue();
            context.HasErrors.ShouldBeTrue();
            pets.HasErrors.ShouldBeFalse();
        }).Errors.ShouldHaveSingleItem().Path.ShouldBe("Owner.Home.Street");
    }

    [Fact]
    public void MapOptionalReturnsNullForAMissingSourceWithoutAnError()
    {
        var src = new { Work = (AddressInput?)null, Pet = (PetInput?)null };

        TestMapping.Run(context =>
        {
            context.MapOptional(src.Work).ShouldBeNull();
            context.MapOptional(src.Pet, new PetMapper()).ShouldBeNull();
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MapOptionalStillReportsAnInvalidSource()
    {
        var src = new { Work = new AddressInput("1 High Street", null, "SW1A 1AA") };

        var errors = TestMapping.Errors(context => context.MapOptional(src.Work).ShouldBeNull());

        errors.ShouldHaveSingleItem().Path.ShouldBe("Work.City");
    }

    [Fact]
    public void MapOptionalAllowsAMappingToProduceNoValue()
    {
        var src = new { Nickname = "  " };

        TestMapping.Run(context =>
                context.MapOptional(src, (s, _) => string.IsNullOrWhiteSpace(s.Nickname) ? null : s.Nickname).ShouldBeNull())
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MapEachLocatesErrorsByIndexAndKeepsTheItemsThatMapped()
    {
        var src = new { Pets = new List<PetInput?> { new("Rex", "3"), new("Tom", "x"), null, new("", "1") } };
        List<Pet> pets = [];

        var errors = TestMapping.Errors(context =>
        {
            pets = context.MapEach(src.Pets, new PetMapper());
            context.HasErrors.ShouldBeTrue();
        });

        pets.ShouldBe([new Pet("Rex", 3)]);
        errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Pets[1].Age", Invalid), ("Pets[2]", Required), ("Pets[3].Name", Required)]);
    }

    [Fact]
    public void MapEachReportsAMissingCollection()
    {
        var src = new { Pets = (List<PetInput>?)null };

        TestMapping.Errors(context => context.MapEach(src.Pets, new PetMapper()).ShouldBeEmpty())
            .ShouldHaveSingleItem().ShouldBe(new MappingError("Pets", Required, "A value is required."));
    }

    [Fact]
    public void MapEachAcceptsAFunctionOrObjectsThatMapThemselves()
    {
        var src = new
        {
            Names = new[] { "a", "b" },
            Homes = new List<AddressInput> { new("1 High Street", "London", "SW1A 1AA") }
        };

        TestMapping.Run(context =>
        {
            context.MapEach(src.Names, (name, _) => name.ToUpperInvariant()).ShouldBe(["A", "B"]);
            context.MapEach(src.Homes).ShouldHaveSingleItem().City.ShouldBe("London");
        }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void SkipInvalidKeepsTheValidItemsAsAPartialValue()
    {
        var import = Mapper.Create((List<PetInput> rows, MappingContext context) =>
            new Shelter(context.MapEach(rows, new PetMapper(), skipInvalid: true)));

        var result = import.Map([new("Rex", "3"), new("Tom", "x"), new("Kit", "1")]);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("[1].Age");
        result.PartialValue.ShouldNotBeNull().Pets.ShouldBe([new Pet("Rex", 3), new Pet("Kit", 1)]);
    }

    [Fact]
    public void AnImportPastMaxErrorsKeepsItsValidItemsAndSaysErrorsWereLeftOut()
    {
        var import = Mapper.Create((List<PetInput> rows, MappingContext context) =>
            new Shelter(context.MapEach(rows, new PetMapper(), skipInvalid: true)));
        var rows = Enumerable.Range(0, 10).Select(i => new PetInput($"Pet{i}", i % 2 == 0 ? "1" : "x")).ToList();

        var result = import.Map(rows, new MappingOptions { MaxErrors = 2 });

        result.Errors.Select(error => error.Path).ShouldBe(["[1].Age", "[3].Age", ""]);
        result.Errors[^1].Code.ShouldBe(MappingErrorCodes.TooManyErrors);
        result.PartialValue.ShouldNotBeNull().Pets.Count.ShouldBe(5);
    }

    [Fact]
    public void MapEachCanPassEachItemsPositionToTheFunction()
    {
        var src = new { Names = new[] { "a", "b" } };

        TestMapping.Run(context =>
                context.MapEach(src.Names, (name, index, _) => $"{index}:{name}").ShouldBe(["0:a", "1:b"]))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ATwoStepImportKeepsTheSourcePositionsOfItsErrors()
    {
        var known = new Dictionary<string, Pet> { ["Rex"] = new("Rex", 3), ["Kit"] = new("Kit", 1) };
        var import = Mapper.Create((ShelterInput src, MappingContext ctx) =>
        {
            // Step 1: check every row, keeping each valid row's position.
            var names = ctx.MapEach(src.Rows, (row, index, c) => (Index: index, Name: c.Required(row.Name)),
                skipInvalid: true);

            // Step 2: look the names up, reporting at each row's source position.
            List<Pet> pets = [];
            foreach (var (index, name) in names)
            {
                var row = ctx.For(nameof(src.Rows), index);
                if (row.TryLookup(name, known, out var pet, nameof(PetInput.Name)))
                    pets.Add(pet);
                else
                    ctx.Skip(row);
            }

            return new Shelter(pets);
        });

        var result = import.Map(new ShelterInput([new("Rex", "3"), new(null, "1"), new("Zed", "1"), new("Kit", "1")]));

        result.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("Rows[1].Name", MappingErrorCodes.Required), ("Rows[2].Name", MappingErrorCodes.NotFound)]);
        result.PartialValue.ShouldNotBeNull().Pets.ShouldBe([known["Rex"], known["Kit"]]);
    }

    [Fact]
    public void AStoppedMappingDoesNotStartEnumerating()
    {
        var src = new { First = "x", Pets = new UnusableSource() };

        var errors = TestMapping.Errors(context =>
        {
            context.Parse<int>(src.First);
            context.MapEach(src.Pets, new PetMapper()).ShouldBeEmpty();
        }, MappingOptions.FailFast);

        errors.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapEachEnumeratesOnceAndDisposesTheSource(bool failFast)
    {
        var source = new SingleUsePets();
        List<Pet> pets = [];

        var result = TestMapping.Run(context =>
            pets = context.MapEach(source, new PetMapper(), name: "Rows"),
            failFast ? MappingOptions.FailFast : MappingOptions.Default);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Rows[1].Age");
        pets.ShouldBe(failFast ? [new Pet("Rex", 3)] : [new Pet("Rex", 3), new Pet("Kit", 1)]);
        source.Enumerations.ShouldBe(1);
        source.ItemsRead.ShouldBe(failFast ? 2 : 3);
        source.Disposed.ShouldBeTrue();
    }

    [Fact]
    public void MapEachDisposesTheEnumeratorWhenAMapperThrows()
    {
        var disposed = false;
        IEnumerable<PetInput> Pets()
        {
            try
            {
                yield return new PetInput("Rex", "3");
            }
            finally
            {
                disposed = true;
            }
        }
        var fault = new InvalidOperationException("Mapper fault");

        Should.Throw<InvalidOperationException>(() =>
            TestMapping.Run(context => context.MapEach(Pets(), (Func<PetInput, MappingContext, Pet?>)((_, _) => throw fault))))
            .ShouldBeSameAs(fault);
        disposed.ShouldBeTrue();
    }

    [Fact]
    public void AMappingThatReturnsNullWithoutAnErrorIsABug()
    {
        var src = new { Pet = new PetInput("Rex", "3") };

        var exception = Should.Throw<InvalidOperationException>(() =>
            TestMapping.Run(context => context.Map(src.Pet, new NullMapper())));

        exception.Message.ShouldStartWith("NullMapper returned null without reporting an error.");
    }

    [Fact]
    public void ReportingToTheParentInsteadOfTheChildIsCaughtWhateverTheFunctionReturns()
    {
        var src = new { Pet = new PetInput(null, "3"), Pets = new List<PetInput> { new(null, "1") } };

        Should.Throw<InvalidOperationException>(() => TestMapping.Run(context =>
                context.Map(src.Pet, (pet, _) => context.TryRequired(pet.Name, out var name) ? new Pet(name, 3) : null)))
            .Message.ShouldStartWith("The mapping function reported 1 error(s) to a different MappingContext.");
        Should.Throw<InvalidOperationException>(() => TestMapping.Run(context =>
                context.MapEach(src.Pets, (pet, _) => new Pet(context.Required(pet.Name), 1))))
            .Message.ShouldStartWith("The mapping function reported 1 error(s) to a different MappingContext.");
    }

    [Fact]
    public void ReturningNullAfterSkippingEveryItemIsExplained()
    {
        var src = new { Shelter = new ShelterInput([new("Tom", "x")]) };

        var exception = Should.Throw<InvalidOperationException>(() => TestMapping.Run(context =>
            context.Map(src.Shelter, (shelter, c) =>
            {
                var pets = c.MapEach(shelter.Rows, new PetMapper(), skipInvalid: true);
                return pets.Count == 0 ? null : new Shelter(pets);
            })));

        exception.Message.ShouldStartWith(
            "The mapping function returned null although its only errors were in items it skipped.");
    }

    [Fact]
    public void RootMappingReturningNullAfterSkippingEveryItemIsExplained()
    {
        var mapper = Mapper.Create((List<PetInput> rows, MappingContext context) =>
        {
            context.MapEach(rows, new PetMapper(), skipInvalid: true);
            return (Shelter?)null;
        });

        Should.Throw<InvalidOperationException>(() => mapper.Map([new("Tom", "x")]))
            .Message.ShouldStartWith(
                "The mapping function returned null although its only errors were in items it skipped.");
    }

    [Fact]
    public void AFilteredCollectionIsNotReportedAsTheSourceCollection()
    {
        var src = new { Rows = new List<PetInput> { new("Header", "0"), new("Rex", "3"), new("Tom", "x") } };

        var errors = TestMapping.Errors(context =>
        {
            context.MapEach(src.Rows.Skip(1), new PetMapper());
            context.MapEach(src.Rows.ToList(), new PetMapper());
        });

        errors.Select(error => error.Path).ShouldBe(["[1].Age", "Rows[2].Age"]);
    }

    [Fact]
    public async Task MapOptionalAsyncReturnsNullForAMissingOrInvalidSource()
    {
        var src = new { Missing = (PetInput?)null, Invalid = new PetInput("Tom", "x"), Valid = new PetInput("Rex", "3") };
        var mapper = new AsyncPetMapper();
        Pet?[] pets = [];

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            pets =
            [
                await context.MapOptionalAsync(src.Missing, mapper, token),
                await context.MapOptionalAsync(src.Invalid, mapper, token),
                await context.MapOptionalAsync(src.Valid, mapper, token)
            ];
            return true;
        }).MapAsync(null);

        pets.ShouldBe([null, null, new Pet("Rex", 3)]);
        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Invalid.Age");
    }

    [Fact]
    public void DeeplyNestedErrorsCarryTheFullPath()
    {
        var src = new { Owner = new { Pets = new[] { new { Toys = new[] { "ball" } }, new { Toys = new[] { "ok", " " } } } } };

        var errors = TestMapping.Errors(context =>
            context.Map(src.Owner, (owner, c) =>
                c.MapEach(owner.Pets, (pet, p) =>
                    p.MapEach(pet.Toys, (toy, t) => toy))));

        errors.ShouldHaveSingleItem().Path.ShouldBe("Owner.Pets[1].Toys[1]");
    }

    [Fact]
    public async Task AsynchronousMappersNestWithTheirPathsAndToken()
    {
        var src = new { Pet = new PetInput("Rex", "3"), Pets = new[] { new PetInput("Tom", "x"), new PetInput("Kit", "1") } };
        using var cancellation = new CancellationTokenSource();
        var mapper = new AsyncPetMapper();
        List<Pet> pets = [];

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            var pet = await context.MapAsync(src.Pet, mapper, token);
            pets = await context.MapEachAsync(src.Pets, mapper, token);
            return pet;
        }).MapAsync(null, cancellation.Token);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Pets[0].Age");
        pets.ShouldBe([new Pet("Kit", 1)]);
        mapper.Tokens.ShouldAllBe(token => token == cancellation.Token);
        mapper.Tokens.Count.ShouldBe(3);
    }

    [Fact]
    public async Task MapEachAsyncCanSkipInvalidItems()
    {
        var src = new { Pets = new[] { new PetInput("Tom", "x"), new PetInput("Kit", "1") } };

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
            new Shelter(await context.MapEachAsync(src.Pets, new AsyncPetMapper(), skipInvalid: true,
                cancellationToken: token))).MapAsync(null);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Pets[0].Age");
        result.PartialValue.ShouldNotBeNull().Pets.ShouldBe([new Pet("Kit", 1)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapEachAsyncEnumeratesOnceAndDisposesTheSource(bool failFast)
    {
        var source = new SingleUsePets();
        List<Pet> pets = [];

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            pets = await context.MapEachAsync(source, new AsyncPetMapper(), name: "Rows", cancellationToken: token);
            return true;
        }).MapAsync(null, failFast ? MappingOptions.FailFast : MappingOptions.Default);

        result.Errors.ShouldHaveSingleItem().Path.ShouldBe("Rows[1].Age");
        pets.ShouldBe(failFast ? [new Pet("Rex", 3)] : [new Pet("Rex", 3), new Pet("Kit", 1)]);
        source.Enumerations.ShouldBe(1);
        source.ItemsRead.ShouldBe(failFast ? 2 : 3);
        source.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task MapEachAsyncDisposesTheEnumeratorWhenAMapperThrows()
    {
        var source = new SingleUsePets();
        var fault = new InvalidOperationException("Mapper fault");
        var mapper = Mapper.Create((PetInput _, MappingContext _, CancellationToken _) =>
            ValueTask.FromException<Pet?>(fault));

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
            {
                await context.MapEachAsync(source, mapper, token);
                return true;
            }).MapAsync(null));

        exception.ShouldBeSameAs(fault);
        source.Enumerations.ShouldBe(1);
        source.ItemsRead.ShouldBe(1);
        source.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncHelpersTakeTheTokenLastAndAcceptAnExplicitName()
    {
        var src = new { Pet = new PetInput("Tom", "x"), Pets = new[] { new PetInput("Kit", "y") } };
        using var cancellation = new CancellationTokenSource();
        var mapper = new AsyncPetMapper();

        var result = await Mapper.Create(async (object? _, MappingContext context, CancellationToken token) =>
        {
            await context.MapAsync(src.Pet, mapper, "Cat", token);
            await context.MapOptionalAsync(src.Pet, mapper, "Kitten", token);
            await context.MapEachAsync(src.Pets, mapper, "Cats", token);
            await context.MapEachAsync(src.Pets, mapper, skipInvalid: true, name: "Strays", cancellationToken: token);
            return true;
        }).MapAsync(null, cancellation.Token);

        result.Errors.Select(error => error.Path).ShouldBe(["Cat.Age", "Kitten.Age", "Cats[0].Age", "Strays[0].Age"]);
        mapper.Tokens.ShouldAllBe(token => token == cancellation.Token);
    }

    private sealed record PetInput(string? Name, string? Age);

    private sealed record ShelterInput(List<PetInput> Rows);

    private sealed record Pet(string Name, int Age);

    private sealed record Shelter(List<Pet> Pets);

    private sealed class PetMapper : IMapper<PetInput, Pet>
    {
        public int Calls { get; private set; }

        public Pet Map(PetInput source, MappingContext context)
        {
            Calls++;
            return new Pet(context.Required(source.Name), context.Parse<int>(source.Age));
        }
    }

    private sealed class AsyncPetMapper : IAsyncMapper<PetInput, Pet>
    {
        public List<CancellationToken> Tokens { get; } = [];

        public async ValueTask<Pet?> MapAsync(PetInput source, MappingContext context, CancellationToken cancellationToken)
        {
            Tokens.Add(cancellationToken);
            await Task.Yield();
            return new Pet(context.Required(source.Name), context.Parse<int>(source.Age));
        }
    }

    private sealed class NullMapper : IMapper<PetInput, Pet>
    {
        public Pet? Map(PetInput source, MappingContext context) => null;
    }

    private sealed class UnusableSource : IEnumerable<PetInput>
    {
        public IEnumerator<PetInput> GetEnumerator() => throw new InvalidOperationException("Must not enumerate.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleUsePets : IEnumerable<PetInput>
    {
        public int Enumerations { get; private set; }
        public int ItemsRead { get; private set; }
        public bool Disposed { get; private set; }

        public IEnumerator<PetInput> GetEnumerator()
        {
            if (++Enumerations != 1)
                throw new InvalidOperationException("Source can only be enumerated once.");
            return ReadItems().GetEnumerator();
        }

        private IEnumerable<PetInput> ReadItems()
        {
            try
            {
                foreach (var pet in new[] { new PetInput("Rex", "3"), new PetInput("Tom", "x"), new PetInput("Kit", "1") })
                {
                    ItemsRead++;
                    yield return pet;
                }
            }
            finally
            {
                Disposed = true;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
