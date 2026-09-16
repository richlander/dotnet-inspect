using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class CSharpDecompilerServiceTests
{
    static string AssemblyPath =>
        typeof(CSharpDecompilerServiceTests).Assembly.Location;

    [Fact]
    public void ProduceMember_ExplicitNoPdbIgnoresAdjacentPortablePdb()
    {
        string pdbPath = Path.ChangeExtension(
            AssemblyPath,
            ".pdb");
        Assert.True(File.Exists(pdbPath));
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.NamedLocal));

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceMember(
                type,
                member,
                Descriptor(AssemblyPath),
                Policy(AssemblyPath),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        Assert.False(attempt.PdbSupplied);
        Assert.Equal(
            DecompilerSymbolSource.None,
            attempt.Symbols);
        Assert.DoesNotContain(
            "doubled",
            attempt.Text,
            StringComparison.Ordinal);
        Assert.Single(attempt.BodyProjections);
    }

    [Fact]
    public void ProduceMember_ExplicitNoPdbIgnoresEmbeddedPortablePdb()
    {
        byte[] image = CompileEmbeddedPdbAssembly();
        ApiType type = Type(
            image,
            "EmbeddedPdbSpecimen");
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name == "Read");

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceMember(
                type,
                member,
                PathlessDescriptor(image),
                Policy(AssemblyPath),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        Assert.False(attempt.PdbSupplied);
        Assert.Equal(
            DecompilerSymbolSource.None,
            attempt.Symbols);
        Assert.DoesNotContain(
            "named",
            attempt.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_SuppliedPdbUsesNamesForPathlessContent()
    {
        byte[] assemblyImage =
            File.ReadAllBytes(AssemblyPath);
        var descriptor =
            PathlessDescriptor(assemblyImage);
        ImmutableArray<byte> pdbImage =
            [.. File.ReadAllBytes(
                Path.ChangeExtension(AssemblyPath, ".pdb"))];
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.NamedLocal));

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceMember(
                type,
                member,
                descriptor,
                Policy(AssemblyPath),
                pdbImage,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        Assert.True(attempt.PdbSupplied);
        Assert.Equal(
            DecompilerSymbolSource.External,
            attempt.Symbols);
        Assert.Contains(
            "doubled",
            attempt.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_InvalidSuppliedPdbFailsWithoutNoSymbolRetry()
    {
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.NamedLocal));
        var descriptor = Descriptor(AssemblyPath);
        var policy = Policy(AssemblyPath);

        foreach (ImmutableArray<byte> pdbImage in
            new[]
            {
                default,
                ImmutableArray<byte>.Empty,
                ImmutableArray.Create<byte>(
                    (byte)'B',
                    (byte)'S',
                    (byte)'J',
                    (byte)'B'),
                ImmutableArray.CreateRange(
                    File.ReadAllBytes(
                        Path.ChangeExtension(
                            typeof(CSharpDecompilerService)
                                .Assembly.Location,
                            ".pdb"))),
            })
        {
            CSharpDecompilationAttempt attempt =
                CSharpDecompilerService.ProduceMember(
                    type,
                    member,
                    descriptor,
                    policy,
                    (ImmutableArray<byte>?)pdbImage,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            Assert.Equal(
                CSharpDecompilationStatus.Failed,
                attempt.Status);
            Assert.True(attempt.PdbSupplied);
            Assert.Null(attempt.Text);
            Assert.Empty(attempt.BodyProjections);
            Assert.NotEmpty(attempt.Projection.Diagnostics);
        }
    }

    [Fact]
    public void ProduceMember_PreservesAccessorAddressesAndNativeEvidence()
    {
        (ApiType type, ApiMember property) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.Value));

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceMember(
                type,
                property,
                Descriptor(AssemblyPath),
                Policy(AssemblyPath),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        Assert.Equal(2, attempt.BodyProjections.Length);
        Assert.All(
            attempt.BodyProjections,
            projection =>
            {
                Assert.Equal(
                    CSharpBodyProjectionKind.AccessorBody,
                    projection.Kind);
                Assert.True(projection.ContributesToOutput);
                Assert.True(projection.Projection.Succeeded);
            });
        Assert.Equal(
            new[]
            {
                property.GetterToken,
                property.SetterToken,
            }.Order(),
            attempt.BodyProjections
                .Select(
                    projection =>
                        (int?)projection.Address.Token)
                .Order());
        Assert.Equal(
            attempt.BodyProjections
                .Where(
                    projection =>
                        projection.ContributesToOutput)
                .Min(
                    projection =>
                        projection.Projection.Fidelity),
            attempt.Fidelity);
        Assert.Empty(attempt.Projection.Diagnostics);
    }

    [Fact]
    public void ProduceMember_RuntimeJsonSerializerOptionsMaxDepthIsRealInputEvidence()
    {
        string assemblyPath =
            typeof(System.Text.Json.JsonSerializerOptions)
                .Assembly.Location;
        using var pe =
            new PEReader(File.OpenRead(assemblyPath));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(
                pe,
                includeAll: true).Types,
            candidate =>
                candidate.FullName
                    == typeof(
                        System.Text.Json.JsonSerializerOptions)
                        .FullName);
        ApiMember property = Assert.Single(
            type.Members,
            member => member.Name
                == nameof(
                    System.Text.Json.JsonSerializerOptions
                        .MaxDepth));
        var descriptor = Descriptor(assemblyPath);
        var policy = Policy(assemblyPath);

        CSharpDecompilationAttempt available =
            CSharpDecompilerService.ProduceMember(
                type,
                property,
                descriptor,
                policy,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        CSharpDecompilationAttempt incomplete =
            CSharpDecompilerService.ProduceMember(
                type,
                property,
                descriptor,
                policy,
                maxBodyProjections: 0,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(available.IsAvailable);
        Assert.Contains(
            "MaxDepth",
            available.Text,
            StringComparison.Ordinal);
        Assert.Equal(2, available.BodyProjections.Length);
        Assert.Equal(
            new[]
            {
                property.GetterToken,
                property.SetterToken,
            }.Order(),
            available.BodyProjections
                .Select(
                    projection =>
                        (int?)projection.Address.Token)
                .Order());
        Assert.All(
            available.BodyProjections,
            projection => Assert.Equal(
                CSharpBodyProjectionKind.AccessorBody,
                projection.Kind));

        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            incomplete.Status);
        Assert.Equal(0, incomplete.BodyProjectionsAttempted);
        Assert.Null(incomplete.Text);
    }

    [Fact]
    public void ProduceType_AggregatesContributingBodyFidelity()
    {
        ApiType type = Type<ServiceSpecimen>();
        var descriptor = Descriptor(AssemblyPath);
        var policy = Policy(AssemblyPath);
        string pdbPath = Path.ChangeExtension(AssemblyPath, ".pdb");

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceType(
                type,
                descriptor,
                policy,
                [.. File.ReadAllBytes(pdbPath)],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        DecompilerResult legacy =
            MemberBodyProducer.Project(type, descriptor, pdbPath, policy);

        Assert.True(attempt.IsAvailable);
        Assert.Equal(legacy.Output, attempt.Text);
        Assert.NotEmpty(attempt.BodyProjections);
        Assert.Equal(
            attempt.BodyProjections
                .Where(
                    projection =>
                        projection.ContributesToOutput)
                .Min(
                    projection =>
                        projection.Projection.Fidelity),
            attempt.Fidelity);
        Assert.DoesNotContain(
            attempt.BodyProjections,
            projection =>
                projection.ContributesToOutput
                && projection.Projection.Fidelity
                    < attempt.Fidelity);
    }

    [Fact]
    public void ProduceType_NonFullBodyRetainsAddressedDiagnosticsAndBoundsAggregateFidelity()
    {
        byte[] image = File.ReadAllBytes(AssemblyPath);
        ApiType type = Type(
            image,
            typeof(BudgetServiceSpecimen).FullName!);
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name
                == nameof(BudgetServiceSpecimen.Read));
        MakeMethodBodyUnavailable(
            image,
            member.MetadataToken!.Value);

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceType(
                type,
                PathlessDescriptor(image),
                Policy(AssemblyPath),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        Assert.Equal(
            DecompilationFidelity.Partial,
            attempt.Fidelity);
        CSharpBodyProjection body = Assert.Single(
            attempt.BodyProjections,
            candidate => candidate.Address.Token
                == member.MetadataToken);
        Assert.True(body.ContributesToOutput);
        Assert.Equal(
            CSharpBodyProjectionKind.MemberBody,
            body.Kind);
        Assert.Equal(
            DecompilationFidelity.Partial,
            body.Projection.Fidelity);
        Assert.Contains(
            body.Projection.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.ContextUnavailable);
        Assert.DoesNotContain(
            attempt.Projection.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.ContextUnavailable);
        Assert.Contains(
            $"MemberBody 0x{member.MetadataToken:x8}",
            attempt.DiagnosticSummary,
            StringComparison.Ordinal);
        Assert.Contains(
            DiagnosticIds.ContextUnavailable,
            attempt.DiagnosticSummary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceType_AggregateMetadataRemainsCompositionScoped()
    {
        ApiType type = Type<BudgetServiceSpecimen>();
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name
                == nameof(BudgetServiceSpecimen.Read));
        var options = new PrinterOptions
        {
            QualifyFieldAccess = true,
        };

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceType(
                type,
                Descriptor(AssemblyPath),
                Policy(AssemblyPath),
                printerOptions: options,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        Assert.True(
            attempt.Projection.EffectiveOptions
                .QualifyFieldAccess);
        Assert.Empty(attempt.Projection.Decisions);
        Assert.Empty(attempt.Projection.ParameterNames);

        CSharpBodyProjection probe = Assert.Single(
            attempt.BodyProjections,
            body => body.Kind
                == CSharpBodyProjectionKind
                    .FieldInitializerProbe);
        Assert.False(
            probe.Projection.EffectiveOptions
                .QualifyFieldAccess);

        CSharpBodyProjection body = Assert.Single(
            attempt.BodyProjections,
            candidate => candidate.Address.Token
                == member.MetadataToken);
        Assert.True(
            body.Projection.EffectiveOptions
                .QualifyFieldAccess);
        Assert.Contains(
            "this._value",
            body.Projection.Output,
            StringComparison.Ordinal);
        Assert.NotEmpty(body.Projection.Decisions);
    }

    [Fact]
    public void ProduceType_BodyParameterOverridesStayOffAggregateMetadata()
    {
        byte[] image = File.ReadAllBytes(AssemblyPath);
        EraseUniqueUtf8MetadataString(
            image,
            "serviceOuter");
        ApiType type = Type(
            image,
            typeof(BudgetServiceSpecimen).FullName!);
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name
                == nameof(
                    BudgetServiceSpecimen
                        .ParameterNameOverride));

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceType(
                type,
                PathlessDescriptor(image),
                Policy(AssemblyPath),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(attempt.IsAvailable);
        CSharpBodyProjection body = Assert.Single(
            attempt.BodyProjections,
            candidate => candidate.Address.Token
                == member.MetadataToken);
        Assert.True(body.ContributesToOutput);
        Assert.Equal(
            ["arg0_1"],
            body.Projection.ParameterNames);
        Assert.Empty(
            attempt.Projection.ParameterNames);
    }

    [Fact]
    public void ProduceType_DoesNotFollowForwardersAwayFromSuppliedAssembly()
    {
        string facadePath = Path.Combine(
            Path.GetDirectoryName(
                typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        using var coreLibrary =
            new PEReader(
                File.OpenRead(
                    typeof(object).Assembly.Location));
        ApiType stringType = Assert.Single(
            ApiSurfaceExtractor.Extract(
                coreLibrary,
                includeAll: true).Types,
            type => type.FullName == typeof(string).FullName);

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceType(
                stringType,
                Descriptor(facadePath),
                Policy(facadePath),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            CSharpDecompilationStatus.Absent,
            attempt.Status);
        Assert.Null(attempt.Text);
        Assert.Empty(attempt.BodyProjections);
    }

    [Fact]
    public void ProduceMember_BodylessDeclarationIsAvailableAndFieldIsAbsent()
    {
        (ApiType type, ApiMember bodyless) =
            Target<AbstractServiceSpecimen>(
                nameof(AbstractServiceSpecimen.Bodyless));
        ApiMember bodylessProperty = Assert.Single(
            type.Members,
            member => member.Name
                == nameof(
                    AbstractServiceSpecimen.BodylessProperty));
        ApiMember field = Assert.Single(
            type.Members,
            member => member.Name
                == nameof(AbstractServiceSpecimen.Field));
        var descriptor = Descriptor(AssemblyPath);
        var policy = Policy(AssemblyPath);

        CSharpDecompilationAttempt declaration =
            CSharpDecompilerService.ProduceMember(
                type,
                bodyless,
                descriptor,
                policy,
                [.. File.ReadAllBytes(
                    Path.ChangeExtension(
                        AssemblyPath,
                        ".pdb"))],
                maxBodyProjections: 0,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        CSharpDecompilationAttempt unsupported =
            CSharpDecompilerService.ProduceMember(
                type,
                field,
                descriptor,
                policy,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        CSharpDecompilationAttempt property =
            CSharpDecompilerService.ProduceMember(
                type,
                bodylessProperty,
                descriptor,
                policy,
                maxBodyProjections: 0,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(declaration.IsAvailable);
        Assert.Contains(
            "abstract int Bodyless();",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.Empty(declaration.BodyProjections);
        Assert.Equal(0, declaration.BodyProjectionsAttempted);
        Assert.True(declaration.PdbSupplied);
        Assert.Equal(
            DecompilerSymbolSource.None,
            declaration.Symbols);
        Assert.True(property.IsAvailable);
        Assert.Empty(property.BodyProjections);
        Assert.Equal(0, property.BodyProjectionsAttempted);

        Assert.Equal(
            CSharpDecompilationStatus.Absent,
            unsupported.Status);
        Assert.Null(unsupported.Text);
        Assert.Empty(unsupported.BodyProjections);
    }

    [Fact]
    public void ProduceType_BudgetCountsFieldInitializerProbeBeforeWork()
    {
        ApiType type = Type<BudgetServiceSpecimen>();
        var descriptor = Descriptor(AssemblyPath);
        var policy = Policy(AssemblyPath);

        CSharpDecompilationAttempt none =
            CSharpDecompilerService.ProduceType(
                type,
                descriptor,
                policy,
                maxBodyProjections: 0,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        CSharpDecompilationAttempt one =
            CSharpDecompilerService.ProduceType(
                type,
                descriptor,
                policy,
                maxBodyProjections: 1,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            none.Status);
        Assert.Equal(0, none.BodyProjectionsAttempted);
        Assert.Empty(none.BodyProjections);

        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            one.Status);
        Assert.Equal(1, one.BodyProjectionsAttempted);
        CSharpBodyProjection probe =
            Assert.Single(one.BodyProjections);
        Assert.Equal(
            CSharpBodyProjectionKind.FieldInitializerProbe,
            probe.Kind);
        Assert.True(probe.ContributesToOutput);
    }

    [Fact]
    public void ProduceMember_BudgetCountsNonContributingAccessorProbes()
    {
        (ApiType type, ApiMember property) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.AutoValue));
        var descriptor = Descriptor(AssemblyPath);
        var policy = Policy(AssemblyPath);

        CSharpDecompilationAttempt complete =
            CSharpDecompilerService.ProduceMember(
                type,
                property,
                descriptor,
                policy,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        CSharpDecompilationAttempt incomplete =
            CSharpDecompilerService.ProduceMember(
                type,
                property,
                descriptor,
                policy,
                maxBodyProjections: 1,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(complete.IsAvailable);
        Assert.Equal(2, complete.BodyProjectionsAttempted);
        Assert.All(
            complete.BodyProjections,
            projection =>
            {
                Assert.Equal(
                    CSharpBodyProjectionKind.AccessorBody,
                    projection.Kind);
                Assert.False(
                    projection.ContributesToOutput);
            });

        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            incomplete.Status);
        Assert.Equal(1, incomplete.BodyProjectionsAttempted);
        Assert.Single(incomplete.BodyProjections);
    }

    [Fact]
    public void ProduceMember_CancellationPropagatesAndSettlesOpenedInput()
    {
        byte[] image = File.ReadAllBytes(AssemblyPath);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        bool disposed = false;
        int opens = 0;
        ResolvedAssemblyReference descriptor =
            PathlessDescriptor(
                image,
                () =>
                {
                    opens++;
                    cancellation.Cancel();
                    return new TrackingMemoryStream(
                        image,
                        () => disposed = true);
                });
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.NamedLocal));

        Assert.Throws<OperationCanceledException>(
            () => CSharpDecompilerService.ProduceMember(
                type,
                member,
                descriptor,
                Policy(AssemblyPath),
                cancellationToken: cancellation.Token));
        Assert.Equal(1, opens);
        Assert.True(disposed);
    }

    [Fact]
    public void ProduceMember_PreCanceledRequestDoesNotOpenInput()
    {
        byte[] image = File.ReadAllBytes(AssemblyPath);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();
        int opens = 0;
        ResolvedAssemblyReference descriptor =
            PathlessDescriptor(
                image,
                () =>
                {
                    opens++;
                    return new MemoryStream(
                        image,
                        writable: false);
                });
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.NamedLocal));

        Assert.Throws<OperationCanceledException>(
            () => CSharpDecompilerService.ProduceMember(
                type,
                member,
                descriptor,
                Policy(AssemblyPath),
                cancellationToken: cancellation.Token));
        Assert.Equal(0, opens);
    }

    [Fact]
    public void ProduceMember_CancellationAtDependencySelectionPropagates()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        var policy = new CancelingBindingPolicy(
            Policy(AssemblyPath),
            cancellation);
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.CrossAssemblyType));

        Assert.Throws<OperationCanceledException>(
            () => CSharpDecompilerService.ProduceMember(
                type,
                member,
                Descriptor(AssemblyPath),
                policy,
                cancellationToken:
                    cancellation.Token));
        Assert.True(policy.SelectionCount > 0);
    }

    [Fact]
    public void ProduceMember_DisposesOwnedInputAndMatchesLegacyBytes()
    {
        byte[] image = File.ReadAllBytes(AssemblyPath);
        int opens = 0;
        int disposals = 0;
        ResolvedAssemblyReference descriptor =
            PathlessDescriptor(
                image,
                () =>
                {
                    opens++;
                    return new TrackingMemoryStream(
                        image,
                        () => disposals++);
                });
        (ApiType type, ApiMember member) =
            Target<ServiceSpecimen>(
                nameof(ServiceSpecimen.NamedLocal));

        CSharpDecompilationAttempt attempt =
            CSharpDecompilerService.ProduceMember(
                type,
                member,
                descriptor,
                Policy(AssemblyPath),
                [.. File.ReadAllBytes(
                    Path.ChangeExtension(
                        AssemblyPath,
                        ".pdb"))],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        MemberRenderResult legacy =
            MemberBodyProducer.ProduceMember(
                type,
                member,
                AssemblyPath,
                Path.ChangeExtension(
                    AssemblyPath,
                    ".pdb"));

        Assert.True(attempt.IsAvailable);
        Assert.Equal(legacy.Text, attempt.Text);
        Assert.True(opens >= 2);
        Assert.Equal(opens, disposals);
    }

    static ApiType Type<T>()
    {
        return Type(
            File.ReadAllBytes(AssemblyPath),
            typeof(T).FullName!);
    }

    static ApiType Type(
        byte[] image,
        string fullName)
    {
        using var pe =
            new PEReader(
                ImmutableArray.CreateRange(image));
        return Assert.Single(
            ApiSurfaceExtractor.Extract(
                pe,
                includeAll: true).Types,
            type => type.FullName == fullName);
    }

    static (ApiType Type, ApiMember Member) Target<T>(
        string memberName)
    {
        ApiType type = Type<T>();
        return (
            type,
            Assert.Single(
                type.Members,
                member => member.Name == memberName));
    }

    static ResolvedAssemblyReference Descriptor(string path)
        => ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local(
                "CSharpDecompilerServiceTests"));

    static IAssemblyBindingPolicy Policy(string path)
        => new AssemblyReferenceBindingPolicy(
            MetadataSource.DefaultAssemblyReferenceResolver(
                path));

    static ResolvedAssemblyReference PathlessDescriptor(
        byte[] image,
        Func<Stream>? openRead = null)
    {
        using var pe =
            new PEReader(
                ImmutableArray.CreateRange(image));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                pe.GetMetadataReader());
        return ResolvedAssemblyReference.Create(
            identity,
            path: null,
            openRead
                ?? (() => new MemoryStream(
                    image,
                    writable: false)),
            AssemblyResolutionProvenance.Local(
                "CSharpDecompilerServiceTests"));
    }

    static void EraseUniqueUtf8MetadataString(
        byte[] image,
        string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        int offset = image.AsSpan().IndexOf(encoded);
        Assert.True(
            offset >= 0,
            $"Metadata string '{value}' was not found.");
        Assert.Equal(
            -1,
            image.AsSpan(
                offset + encoded.Length).IndexOf(encoded));
        image[offset] = 0;
    }

    static void MakeMethodBodyUnavailable(
        byte[] image,
        int methodToken)
    {
        using var pe = new PEReader(
            new MemoryStream(
                image,
                writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method =
            reader.GetMethodDefinition(
                (MethodDefinitionHandle)
                    MetadataTokens.EntityHandle(
                        methodToken));
        int sectionIndex =
            pe.PEHeaders.GetContainingSectionIndex(
                method.RelativeVirtualAddress);
        Assert.True(sectionIndex >= 0);
        SectionHeader section =
            pe.PEHeaders.SectionHeaders[sectionIndex];
        int bodyOffset = checked(
            method.RelativeVirtualAddress
                - section.VirtualAddress
                + section.PointerToRawData);
        image[bodyOffset] = 0;
    }

    static byte[] CompileEmbeddedPdbAssembly()
    {
        const string Source = """
            public static class EmbeddedPdbSpecimen
            {
                public static int Read(int value)
                {
                    int named = value + 1;
                    return Keep(ref named);
                }

                private static int Keep(ref int value) => value;
            }
            """;
        CSharpCompilation compilation =
            CSharpCompilation.Create(
                $"EmbeddedPdbSpecimen_{Guid.NewGuid():N}",
                [
                    CSharpSyntaxTree.ParseText(
                        Source,
                        new CSharpParseOptions(
                            LanguageVersion.Preview)),
                ],
                [
                    MetadataReference.CreateFromFile(
                        typeof(object).Assembly.Location),
                ],
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel:
                        OptimizationLevel.Release,
                    deterministic: true));
        using var assembly = new MemoryStream();
        var result = compilation.Emit(
            assembly,
            options: new EmitOptions(
                debugInformationFormat:
                    DebugInformationFormat.Embedded,
                pdbFilePath: "EmbeddedPdbSpecimen.pdb"));
        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics));
        return assembly.ToArray();
    }

    sealed class TrackingMemoryStream(
        byte[] image,
        Action disposed)
        : MemoryStream(image, writable: false)
    {
        bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                disposed();
            }
            base.Dispose(disposing);
        }
    }

    sealed class CancelingBindingPolicy(
        IAssemblyBindingPolicy inner,
        CancellationTokenSource cancellation)
        : IAssemblyBindingPolicy
    {
        public int SelectionCount { get; private set; }

        public AssemblyBindingPolicyVersion Version =>
            inner.Version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            SelectionCount++;
            AssemblyBindingSelectionSnapshot result =
                inner.Select(request);
            cancellation.Cancel();
            return result;
        }
    }
}

public sealed class ServiceSpecimen
{
    int _value;

    public int AutoValue { get; set; }

    public int Value
    {
        get => _value + 1;
        set => _value = value - 1;
    }

    public static int NamedLocal(int value)
    {
        int doubled = value * 2;
        return KeepLocal(ref doubled);
    }

    static int KeepLocal(ref int value) => value;

    public static string CrossAssemblyType(int value)
        => new System.Text.StringBuilder()
            .Append(value)
            .ToString();
}

public abstract class AbstractServiceSpecimen
{
    public int Field;

    public abstract int Bodyless();

    public abstract int BodylessProperty { get; }
}

public sealed class BudgetServiceSpecimen
{
    readonly int _value = 42;

    public int Read() => _value;

    public static Func<int, int> ParameterNameOverride(
        int serviceOuter)
        => arg0 => arg0 + 1;
}
