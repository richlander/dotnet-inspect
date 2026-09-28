using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Fixtures.StructuredTypes;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public sealed class CSharpDecompilerTypeDocumentTests
{
    static string AssemblyPath =>
        typeof(StructuredSample).Assembly.Location;

    [Fact]
    public void ProduceTypeDocument_UsesCompleteSameReaderInventoryAndExactRanges()
    {
        ApiType filtered = Type<StructuredSample>();
        filtered.Members =
        [
            filtered.Members.Single(member =>
                member.Name == nameof(StructuredSample.Raise)),
        ];

        CSharpTypeDocument document = Available(Produce(filtered));
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition definition = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(
                filtered.MetadataToken!.Value & 0x00FFFFFF));

        Assert.Equal(
            definition.GetFields().Count
                + definition.GetMethods().Count
                + definition.GetProperties().Count
                + definition.GetEvents().Count,
            document.Artifacts.Length);
        Assert.Equal(definition.GetMethods().Count, document.Bodies.Length);
        Assert.Contains(
            document.Artifacts,
            artifact =>
                artifact.Representation.Role
                    == CSharpTypeArtifactRole.BackingStorage);
        Assert.Contains(
            document.Declarations,
            declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Property
                && declaration.Parts.SelectMany(static part => part.OwnedBodies)
                    .Count() == 2);
        Assert.Contains(
            document.Declarations,
            declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Event
                && declaration.Parts.SelectMany(static part => part.OwnedBodies)
                    .Count() == 2);

        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));

        Assert.Contains("class StructuredSample", bodies.Text);
        Assert.Contains("Overload(int value)", bodies.Text);
        Assert.Contains("this[int index]", bodies.Text);
        Assert.Contains("IExplicitValue.Value", bodies.Text);
        Assert.NotEqual(bodies.Text, skeleton.Text);
        Assert.All(
            bodies.Declarations.SelectMany(static declaration => declaration.Bodies),
            body => Assert.InRange(
                body.Range.End,
                body.Range.Start,
                bodies.Text.Length));

        CSharpTypeBodyContribution initializer = Assert.Single(
            document.Declarations
                .SelectMany(static declaration => declaration.Parts)
                .SelectMany(static part => part.Contributions),
            contribution =>
                contribution.Role
                    == CSharpTypeBodyContributionRole.FieldInitializer);
        CSharpTypeDeclaration constructor = Assert.Single(
            document.Declarations,
            declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Constructor
                && declaration.Parts
                    .SelectMany(static part => part.OwnedBodies)
                    .Any(body => body.BodyId == initializer.BodyId));
        CSharpTypeDocumentProjection selected = Project(
            document,
            new(
                CSharpTypeBodyMode.SelectedBody,
                constructor.Anchor));
        Assert.Contains("_seed = 7", selected.Text);
        Assert.DoesNotContain("return Value + value", selected.Text);

        AssertCompiles(bodies.Text);
        AssertCompiles(skeleton.Text);
        var replay = CSharpTypeDocumentJson.Deserialize(
            CSharpTypeDocumentJson.Serialize(document));
        Assert.Equal(document.Revision, replay.Revision);
        Assert.Equal(bodies.Text, Project(replay, new()).Text);
    }

    [Theory]
    [InlineData(typeof(EmptyType))]
    [InlineData(typeof(IBodylessType))]
    [InlineData(typeof(Choice))]
    [InlineData(typeof(Converter))]
    public void ProduceTypeDocument_AcceptsBodylessAndLanguageFrameTypes(
        Type runtimeType)
    {
        CSharpTypeDocument document = Available(Produce(Type(runtimeType)));
        CSharpTypeDocumentProjection projection = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));

        Assert.NotEmpty(projection.Text);
        Assert.Equal(
            runtimeType == typeof(Converter),
            document.Declarations.IsEmpty);
        if (runtimeType == typeof(Converter))
        {
            Assert.All(
                document.Artifacts,
                artifact => Assert.Equal(
                    CSharpTypeArtifactRole.DelegateSignature,
                    artifact.Representation.Role));
        }
        else if (runtimeType == typeof(Choice))
        {
            Assert.Contains(
                document.Artifacts,
                artifact =>
                    artifact.Representation.Role
                        == CSharpTypeArtifactRole.EnumStorage);
        }
        else if (runtimeType == typeof(IBodylessType))
        {
            Assert.All(
                document.Bodies,
                body => Assert.Equal(
                    CSharpTypeBodyOutcome.NoBody,
                    body.Outcome));
        }
    }

    [Fact]
    public void ProduceTypeDocument_PreservesExactNestedGenericIdentity()
    {
        ApiType type = Type(typeof(Outer<>.Inner<>));

        CSharpTypeDocument document = Available(Produce(type));
        CSharpTypeDocumentProjection projection = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));

        Assert.Equal(type.DefinitionName, document.TypeName);
        Assert.Contains("class Outer<T>", projection.Text);
        Assert.True(
            projection.Text.Contains(
                "struct Inner<U>",
                StringComparison.Ordinal),
            projection.Text);
        AssertCompiles(projection.Text);
    }

    [Theory]
    [InlineData(typeof(InterfaceObligationOuter.Inner), false)]
    [InlineData(typeof(InterfaceObligationOuter.Inner), true)]
    [InlineData(typeof(RequiredBaseOuter.Inner), false)]
    [InlineData(typeof(RequiredBaseOuter.Inner), true)]
    public void ProduceTypeDocument_DeclinesInvalidContainingTypeShells(
        Type runtimeType,
        bool zeroBudget)
    {
        CSharpTypeDocumentOutcome.Unavailable unavailable =
            Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(
                Produce(
                    Type(runtimeType),
                    maxBodyProjections: zeroBudget
                        ? 0
                        : CSharpDecompilerService.DefaultMaxBodyProjections));

        Assert.Contains("Containing Type", unavailable.Reason);
        Assert.Contains("inheritance obligations", unavailable.Reason);
    }

    [Fact]
    public void ProduceTypeDocument_PreservesInheritedInterfaceContext()
    {
        CSharpTypeDocument document = Available(
            Produce(Type<InterfaceContext.Inner>()));
        CSharpTypeDeclaration read = Assert.Single(
            document.Declarations,
            declaration => declaration.Kind == CSharpTypeDeclarationKind.Method);

        Assert.Equal(
            Type<InterfaceContext.Inner>().DefinitionName,
            document.TypeName);
        AssertCompiles(Project(document, new(CSharpTypeBodyMode.Bodies)).Text);
        AssertCompiles(Project(document, new(CSharpTypeBodyMode.Skeleton)).Text);
        AssertCompiles(Project(
            document,
            new(CSharpTypeBodyMode.SelectedBody, read.Anchor)).Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ProduceTypeDocument_BudgetExhaustionRetainsCompleteSkeleton(int budget)
    {
        CSharpTypeDocumentOutcome.Incomplete incomplete =
            Assert.IsType<CSharpTypeDocumentOutcome.Incomplete>(
                Produce(Type<StructuredSample>(), maxBodyProjections: budget));

        Assert.NotEmpty(incomplete.Document.Artifacts);
        Assert.NotEmpty(incomplete.Document.Declarations);
        Assert.Equal(
            incomplete.Document.Bodies.Count(static body =>
                body.HasManagedBody) - budget,
            incomplete.FailedBodyIds.Length);
        CSharpTypeDocumentProjection skeleton = Project(
            incomplete.Document,
            new(CSharpTypeBodyMode.Skeleton));
        Assert.Contains("class StructuredSample", skeleton.Text);
        AssertCompiles(skeleton.Text);
        var replay = CSharpTypeDocumentJson.Deserialize(
            CSharpTypeDocumentJson.Serialize(incomplete.Document));
        Assert.Equal(incomplete.Document.Revision, replay.Revision);
        if (budget > 0)
        {
            var constructor = replay.Declarations.Single(declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Constructor);
            AssertCompiles(Project(replay, new(
                CSharpTypeBodyMode.SelectedBody, constructor.Anchor)).Text);
        }
    }

    [Fact]
    public void ProduceTypeDocument_PreservesEveryInitializerContributor()
    {
        CSharpTypeDocument document = Available(Produce(Type<MultipleConstructors>()));
        var contributions = document.Declarations
            .SelectMany(declaration => declaration.Parts)
            .SelectMany(part => part.Contributions).ToArray();
        Assert.Equal(2, contributions.Length);
        Assert.Equal(2, contributions.Select(value => value.BodyId).Distinct().Count());
        foreach (var constructor in document.Declarations.Where(
            declaration => declaration.Kind == CSharpTypeDeclarationKind.Constructor))
        {
            var selected = Project(document, new(
                CSharpTypeBodyMode.SelectedBody, constructor.Anchor,
                includeAttributes: false));
            Assert.Contains("_value = 7", selected.Text);
            AssertCompiles(selected.Text);
        }
    }

    [Fact]
    public void ProduceTypeDocument_PreservesBackingStorageInitializers()
    {
        CSharpTypeDocument document = Available(
            Produce(Type<BackingStorageInitializers>()));
        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));

        Assert.Contains("Value", bodies.Text);
        Assert.Contains("= 7;", bodies.Text);
        Assert.Contains("Number", bodies.Text);
        Assert.Contains("= 9;", bodies.Text);
        Assert.Contains("Changed =", bodies.Text);
        Assert.DoesNotContain("= 7;", skeleton.Text);
        Assert.DoesNotContain("= 9;", skeleton.Text);
        Assert.DoesNotContain("Changed =", skeleton.Text);

        CSharpTypeDeclaration[] initialized = document.Declarations
            .Where(declaration => declaration.Parts.Any(part =>
                part.ImplementationKind
                    == CSharpTypeImplementationKind.Initializer))
            .ToArray();
        CSharpTypeDeclaration[] instanceInitialized = initialized
            .Where(declaration => declaration.Parts
                .SelectMany(part => part.Contributions)
                .Select(contribution => contribution.BodyId)
                .Distinct()
                .Count() == 2)
            .ToArray();
        Assert.Equal(3, instanceInitialized.Length);
        foreach (var declaration in instanceInitialized)
        {
            Assert.All(
                declaration.Parts.SelectMany(part => part.Contributions),
                contribution => Assert.Equal(
                    declaration.Kind == CSharpTypeDeclarationKind.Property
                        ? CSharpTypeBodyContributionRole.PropertyInitializer
                        : CSharpTypeBodyContributionRole.FieldInitializer,
                    contribution.Role));
            var selected = Project(document, new(
                CSharpTypeBodyMode.SelectedBody, declaration.Anchor));
            AssertCompiles(selected.Text);
            Assert.All(
                selected.Declarations.Where(value =>
                    value.Kind == CSharpTypeDeclarationKind.Constructor),
                constructor => Assert.Contains(
                    "throw null;",
                    selected.Text.Substring(constructor.Range.Start, constructor.Range.Length)));
        }
        Assert.All(
            instanceInitialized,
            declaration => Assert.Equal(
                2,
                declaration.Parts
                    .SelectMany(part => part.Contributions)
                    .Select(contribution => contribution.BodyId)
                    .Distinct()
                    .Count()));

        foreach (CSharpTypeDeclaration constructor in document.Declarations.Where(
            declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Constructor
                && declaration.Placement
                    == CSharpTypeDeclarationPlacement.Instance))
        {
            CSharpTypeDocumentProjection selected = Project(
                document,
                new(CSharpTypeBodyMode.SelectedBody, constructor.Anchor));
            Assert.Contains("= 7;", selected.Text);
            Assert.Contains("= 9;", selected.Text);
            Assert.Contains("Changed =", selected.Text);
            AssertCompiles(selected.Text);
        }
        AssertCompiles(bodies.Text);
        AssertCompiles(skeleton.Text);
    }

    [Fact]
    public void ProduceTypeDocument_UsesThrowingRefPropertySkeletons()
    {
        CSharpTypeDocument document = Available(
            Produce(Type<RefReturnProperties>()));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));

        Assert.Contains("ref int Value", skeleton.Text);
        Assert.Contains("ref readonly int ReadOnlyValue", skeleton.Text);
        Assert.Contains("throw null;", skeleton.Text);
        AssertCompiles(skeleton.Text);
        AssertCompiles(Project(document, new(CSharpTypeBodyMode.Bodies)).Text);
    }

    [Fact]
    public void ProduceTypeDocument_UsesThrowingDefaultInterfacePropertySkeletons()
    {
        CSharpTypeDocument document = Available(
            Produce(Type<DefaultInterface>()));
        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));
        CSharpTypeDeclaration read = Assert.Single(
            document.Declarations,
            declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Method);
        CSharpTypeDocumentProjection selected = Project(
            document,
            new(CSharpTypeBodyMode.SelectedBody, read.Anchor));

        Assert.Contains("public virtual int Value", bodies.Text);
        Assert.Contains("public virtual int Value", skeleton.Text);
        Assert.Contains("throw null;", skeleton.Text);
        Assert.Contains("throw null;", selected.Text);
        AssertCompiles(bodies.Text);
        AssertCompiles(skeleton.Text);
        AssertCompiles(selected.Text);
    }

    [Fact]
    public void ProduceTypeDocument_ZeroBudgetDefaultInterfaceRemainsValid()
    {
        CSharpTypeDocumentOutcome.Incomplete incomplete =
            Assert.IsType<CSharpTypeDocumentOutcome.Incomplete>(
                Produce(
                    Type<DefaultInterface>(),
                    maxBodyProjections: 0));
        CSharpTypeDocumentProjection bodies = Project(
            incomplete.Document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            incomplete.Document,
            new(CSharpTypeBodyMode.Skeleton));

        Assert.Contains("throw null;", bodies.Text);
        Assert.Contains("throw null;", skeleton.Text);
        AssertCompiles(bodies.Text);
        AssertCompiles(skeleton.Text);
    }

    [Fact]
    public void ProduceTypeDocument_PreservesUnsafeAccessorContext()
    {
        CSharpTypeDocument document = Available(
            Produce(Type<UnsafeAccessorContexts>()));
        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));

        Assert.Contains("public unsafe int Value", bodies.Text);
        Assert.Contains("public unsafe event", bodies.Text);
        AssertCompiles(bodies.Text);
    }

    [Fact]
    public void ProduceTypeDocument_DerivedConstructorBudgetRequiresProvenInitializer()
    {
        CSharpTypeDocumentOutcome.Unavailable unavailable =
            Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(
                Produce(
                    Type<DerivedConstructorChain>(),
                    maxBodyProjections: 0));
        Assert.Contains("constructor initializer", unavailable.Reason);

        CSharpTypeDocumentOutcome.Incomplete incomplete =
            Assert.IsType<CSharpTypeDocumentOutcome.Incomplete>(
                Produce(
                    Type<DerivedConstructorChain>(),
                    maxBodyProjections: 1));
        CSharpTypeDocumentProjection bodies = Project(
            incomplete.Document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            incomplete.Document,
            new(CSharpTypeBodyMode.Skeleton));
        Assert.Contains(": base(7)", bodies.Text);
        Assert.Contains(": base(7)", skeleton.Text);
        AssertCompiles(bodies.Text);
        AssertCompiles(skeleton.Text);
    }

    [Fact]
    public void ProduceTypeDocument_FiltersTypeAttributesAndAppliesBodyOptions()
    {
        CSharpTypeDocument document = Available(Produce(Type<MultipleConstructors>()));
        Assert.Contains("structured-frame", Project(document, new()).Text);
        Assert.DoesNotContain("structured-frame",
            Project(document, new(includeAttributes: false)).Text);
        var qualified = Available(CSharpDecompilerService.ProduceTypeDocument(
            Type<MultipleConstructors>(),
            ResolvedAssemblyReference.CreateFromPath(AssemblyPath,
                AssemblyResolutionProvenance.Local(nameof(CSharpDecompilerTypeDocumentTests))),
            new AssemblyReferenceBindingPolicy(
                MetadataSource.DefaultAssemblyReferenceResolver(AssemblyPath)),
            printerOptions: new PrinterOptions { QualifyFieldAccess = true },
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("this._value", Project(qualified, new()).Text);
        Assert.NotEqual(document.Revision, qualified.Revision);
    }

    [Fact]
    public void ProduceTypeDocument_RetainsCustomEventBodies()
    {
        CSharpTypeDocument document = Available(Produce(Type<CustomEvent>()));
        Assert.Equal(2, document.Declarations.Single(declaration =>
            declaration.Kind == CSharpTypeDeclarationKind.Event)
            .Parts.SelectMany(part => part.OwnedBodies).Count());
        string source = Project(document, new()).Text;
        Assert.Contains("Changes", source);
        Assert.Contains("_handlers", source);
        AssertCompiles(source);
    }

    [Fact]
    public void ProduceTypeDocument_AssociatesLoweredHelperWithExactBody()
    {
        var document = Available(Produce(Type<LocalHelper>()));
        Assert.Contains(document.Artifacts, artifact =>
            artifact.Representation.Role == CSharpTypeArtifactRole.LoweredImplementationHelper);
        string source = Project(document, new()).Text;
        Assert.DoesNotContain("g__Twice", source);
        AssertCompiles(source);
    }

    [Fact]
    public void ProduceTypeDocument_AssociatesCapturingHelperWithExactBody()
    {
        var document = Available(Produce(Type<CapturingLocalHelper>()));
        Assert.Contains(document.Artifacts, artifact =>
            artifact.Representation.Role
                == CSharpTypeArtifactRole.LoweredImplementationHelper);
        string source = Project(document, new()).Text;
        Assert.Contains("int AddSquare(int input)", source);
        Assert.DoesNotContain("g__AddSquare", source);
        AssertCompiles(source);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ProduceTypeDocument_RaisesJsonDocumentLocalHelper()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "StructuredTypes",
            "System.Text.Json.dll");
        Assert.True(File.Exists(assemblyPath), assemblyPath);
        ApiType type = Type(
            assemblyPath,
            "System.Text.Json.JsonDocument");
        var document = Available(Produce(type, assemblyPath));

        using var pe = new PEReader(File.OpenRead(assemblyPath));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition definition = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(
                type.MetadataToken!.Value & 0x00FFFFFF));
        int parentToken = MetadataTokens.GetToken(Assert.Single(
            definition.GetMethods(),
            handle => reader.GetString(
                reader.GetMethodDefinition(handle).Name)
                == "CreateForLiteral"));
        int helperToken = MetadataTokens.GetToken(Assert.Single(
            definition.GetMethods(),
            handle => reader.GetString(
                reader.GetMethodDefinition(handle).Name)
                == "<CreateForLiteral>g__Create|81_0"));
        Assert.Contains(document.Artifacts, artifact =>
            artifact.Representation.Role
                == CSharpTypeArtifactRole.LoweredImplementationHelper
            && artifact.MetadataToken == helperToken);
        string bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies)).Text;
        Assert.DoesNotContain("<CreateForLiteral>g__Create|81_0", bodies);
        string skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton)).Text;
        Assert.NotEmpty(skeleton);
        CSharpTypeDeclaration parent = Assert.Single(
            document.Declarations,
            declaration => declaration.DeclarationToken == parentToken);
        string selected = Project(
            document,
            new(CSharpTypeBodyMode.SelectedBody, parent.Anchor)).Text;
        Assert.Contains("CreateForLiteral", selected);
        Assert.DoesNotContain(
            "<CreateForLiteral>g__Create|81_0",
            selected);
    }

    [Fact]
    public void ProduceTypeDocument_PreCancelledOperationRemainsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            CSharpDecompilerService.ProduceTypeDocument(
                Type<StructuredSample>(),
                ResolvedAssemblyReference.CreateFromPath(AssemblyPath,
                    AssemblyResolutionProvenance.Local(nameof(CSharpDecompilerTypeDocumentTests))),
                new AssemblyReferenceBindingPolicy(
                    MetadataSource.DefaultAssemblyReferenceResolver(AssemblyPath)),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ProduceTypeDocument_PreservesPdbPolicyAndRepeatedRevision()
    {
        ApiType type = Type<StructuredSample>();
        CSharpTypeDocument withoutPdb = Available(Produce(type));
        CSharpTypeDocument repeated = Available(Produce(type));
        CSharpTypeDocument withPdb = Available(Produce(
            type,
            [.. File.ReadAllBytes(Path.ChangeExtension(AssemblyPath, ".pdb"))]));

        Assert.False(withoutPdb.Source.PdbSupplied);
        Assert.True(withPdb.Source.PdbSupplied);
        Assert.Equal(withoutPdb.Revision, repeated.Revision);
        Assert.IsType<CSharpTypeDocumentOutcome.Rejected>(
            Produce(type, []));
        Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(
            Produce(type, [1, 2, 3, 4]));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ProduceTypeDocument_ProjectsRealPackageAttribute()
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "StructuredTypes",
            "System.Text.Json.dll");
        Assert.True(File.Exists(assemblyPath), assemblyPath);
        ApiType type = Type(
            assemblyPath,
            "System.Text.Json.Serialization.JsonIgnoreAttribute");
        CSharpTypeDocument document = Available(Produce(type, assemblyPath));

        using var pe = new PEReader(File.OpenRead(assemblyPath));
        TypeDefinition definition = pe.GetMetadataReader().GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(
                type.MetadataToken!.Value & 0x00FFFFFF));
        Assert.Equal(type.DefinitionName, document.TypeName);
        Assert.Equal(definition.GetMethods().Count, document.Bodies.Length);
        Assert.NotEmpty(document.Artifacts);
        Assert.NotEmpty(document.Revision.Sha256);
        Assert.Contains(
            document.Declarations
                .SelectMany(declaration => declaration.Parts)
                .SelectMany(part => part.Contributions),
            contribution =>
                contribution.Role == CSharpTypeBodyContributionRole.PropertyInitializer);
        AssertCompiles(Project(document, new(CSharpTypeBodyMode.Bodies)).Text);
        AssertCompiles(Project(document, new(CSharpTypeBodyMode.Skeleton)).Text);
        CSharpTypeDeclaration constructor = Assert.Single(
            document.Declarations,
            declaration => declaration.Kind == CSharpTypeDeclarationKind.Constructor);
        AssertCompiles(Project(
            document,
            new(CSharpTypeBodyMode.SelectedBody, constructor.Anchor)).Text);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(
        "System.Text.Json.JsonSerializerOptions",
        "<Default>k__BackingField")]
    [InlineData(
        "System.Collections.Generic.OrderedDictionary`2+Enumerator",
        "inheritance obligations")]
    public void ProduceTypeDocument_ReportsRequiredRealPackageBoundaries(
        string metadataName,
        string unavailableReason)
    {
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "StructuredTypes",
            "System.Text.Json.dll");
        Assert.True(File.Exists(assemblyPath), assemblyPath);
        ApiType type = Type(assemblyPath, metadataName);

        CSharpTypeDocumentOutcome outcome = Produce(
            type,
            assemblyPath);
        var unavailable =
            Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(outcome);
        Assert.Contains(unavailableReason, unavailable.Reason);
    }

    static CSharpTypeDocumentOutcome Produce(
        ApiType type,
        ImmutableArray<byte>? pdb = null,
        int maxBodyProjections =
            CSharpDecompilerService.DefaultMaxBodyProjections)
        => Produce(type, AssemblyPath, pdb, maxBodyProjections);

    static CSharpTypeDocumentOutcome Produce(
        ApiType type,
        string assemblyPath,
        ImmutableArray<byte>? pdb = null,
        int maxBodyProjections =
            CSharpDecompilerService.DefaultMaxBodyProjections)
        => CSharpDecompilerService.ProduceTypeDocument(
            type,
            ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local(
                    nameof(CSharpDecompilerTypeDocumentTests))),
            new AssemblyReferenceBindingPolicy(
                MetadataSource.DefaultAssemblyReferenceResolver(
                    assemblyPath)),
            pdb,
            maxBodyProjections: maxBodyProjections,
            cancellationToken:
                TestContext.Current.CancellationToken);

    static CSharpTypeDocument Available(CSharpTypeDocumentOutcome outcome)
        => outcome switch
        {
            CSharpTypeDocumentOutcome.Available available =>
                available.Document,
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected Available, received {outcome}."),
        };

    static CSharpTypeDocumentProjection Project(
        CSharpTypeDocument document,
        CSharpTypeProjectionRequest request)
        => Assert.IsType<CSharpTypeProjectionOutcome.Projected>(
            CSharpTypeDocumentProjector.Project(
                document,
                request)).Projection;

    static void AssertCompiles(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "StructuredTypeProjection",
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    cancellationToken:
                        TestContext.Current.CancellationToken),
            ],
            RoslynTestReferences.TrustedPlatform.Append(
                MetadataReference.CreateFromFile(AssemblyPath)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
        using var output = new MemoryStream();
        var result = compilation.Emit(
            output,
            cancellationToken:
                TestContext.Current.CancellationToken);
        Assert.True(
            result.Success,
            $"{source}\n{string.Join("\n", result.Diagnostics)}");
    }

    static ApiType Type<T>() => Type(typeof(T));

    static ApiType Type(Type runtimeType)
        => Type(AssemblyPath, runtimeType.MetadataToken);

    static ApiType Type(string assemblyPath, string metadataName)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        return Assert.Single(
            ApiSurfaceExtractor.Extract(
                pe,
                includeAll: true,
                includeCompilerGenerated: true).Types,
            type => type.DefinitionName is { } definition
                && string.Equals(
                    string.IsNullOrEmpty(definition.Namespace)
                        ? string.Join("+", definition.Segments)
                        : definition.Namespace
                            + "."
                            + string.Join("+", definition.Segments),
                    metadataName,
                    StringComparison.Ordinal));
    }

    static ApiType Type(string assemblyPath, int metadataToken)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        return Assert.Single(
            ApiSurfaceExtractor.Extract(
                pe,
                includeAll: true,
                includeCompilerGenerated: true).Types,
            type => type.MetadataToken == metadataToken);
    }

    [Fact]
    public void ProduceTypeDocument_PreservesInitializerExecutionOrder()
    {
        CSharpTypeDocument document = Available(
            Produce(Type<InterleavedInitializers>()));
        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));

        int first = bodies.Text.IndexOf(
            "First = Next();",
            StringComparison.Ordinal);
        int second = bodies.Text.IndexOf(
            "Second",
            StringComparison.Ordinal);
        int third = bodies.Text.IndexOf(
            "Third = Next();",
            StringComparison.Ordinal);
        Assert.True(first >= 0, bodies.Text);
        Assert.True(second > first, bodies.Text);
        Assert.True(third > second, bodies.Text);
        AssertCompiles(bodies.Text);
    }

    [Fact]
    public void ProduceTypeDocument_PreservesRequiredConstantInitializers()
    {
        CSharpTypeDocument document = Available(
            Produce(Type(typeof(ConstantField))));
        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));
        CSharpTypeDeclaration read = document.Declarations.Single(
            declaration =>
                declaration.Kind == CSharpTypeDeclarationKind.Method);
        CSharpTypeDocumentProjection selected = Project(
            document,
            new(CSharpTypeBodyMode.SelectedBody, read.Anchor));

        foreach (CSharpTypeDocumentProjection projection in
            new[] { bodies, skeleton, selected })
        {
            Assert.Contains(
                "public const int Value = 7;",
                projection.Text,
                StringComparison.Ordinal);
            AssertCompiles(projection.Text);
        }
    }
}
