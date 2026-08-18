using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class ReferenceEqualityMetadataFactsTests
{
    [Fact]
    public void StructuredDefinitionNames_DoNotShareExactIdentity()
    {
        var assembly = new AssemblyReferenceIdentity(
            "Shapes",
            new Version(1, 0, 0, 0),
            null,
            null);
        var literal = Definition("Shapes", "N", "A+B", ["A+B"], assembly);
        var nested = Definition("Shapes", "N", "A+B", ["A", "B"], assembly);

        Assert.NotEqual(
            TypeDefinitionIdentity.Create(literal),
            TypeDefinitionIdentity.Create(nested));
    }

    [Fact]
    public void LiteralPlusType_DoesNotMaskNestedOperatorFacts()
    {
        string directory = Directory.CreateTempSubdirectory(
            "reference-equality-structured-hierarchy-").FullName;
        string path = Path.Combine(directory, "StructuredHierarchy.dll");
        try
        {
            File.WriteAllBytes(path, BuildStructuredHierarchyCollision());
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(
                source,
                "Collision.Cases",
                "Compare");
            Assert.NotNull(function);
            IrPasses.Run(function!);
            function!.CheckInvariant();

            string output = CSharpPrinter.Print(function).Output!;

            Assert.Contains(
                "return (object)left == (object)right;",
                output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OperatorHierarchyLookup_DoesNotMaterializeUnrelatedTypeNames()
    {
        string directory = Directory.CreateTempSubdirectory(
            "reference-equality-hierarchy-allocation-").FullName;
        string path = Path.Combine(directory, "HierarchyAllocation.dll");
        try
        {
            File.WriteAllBytes(
                path,
                BuildHierarchyAllocationImage(
                    typeCount: 8192,
                    nameLength: 4000));
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var rootHandle = MetadataTokens.TypeDefinitionHandle(2);
            TypeRef root = TypeRefDecoder.Instance.GetTypeFromDefinition(
                source.Reader,
                rootHandle,
                rawTypeKind: 0);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            MetadataFactState result =
                source.HasOperatorInBindingHierarchy(
                    root,
                    "op_Equality");
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(MetadataFactState.No, result);
            Assert.InRange(allocated, 0, 1_000_000);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TypeDefHandleProvenance_IsBoundToItsModule()
    {
        string directory = Directory.CreateTempSubdirectory(
            "reference-equality-handle-provenance-").FullName;
        string firstPath = Path.Combine(directory, "first.dll");
        string secondPath = Path.Combine(directory, "second.dll");
        try
        {
            File.WriteAllBytes(
                firstPath,
                BuildHandleProvenanceImage(shiftRoot: false));
            File.WriteAllBytes(
                secondPath,
                BuildHandleProvenanceImage(shiftRoot: true));
            using var first = MetadataSource.OpenWithoutSymbols(firstPath);
            using var second = MetadataSource.OpenWithoutSymbols(secondPath);
            TypeRef root = TypeRefDecoder.Instance.GetTypeFromDefinition(
                first.Reader,
                MetadataTokens.TypeDefinitionHandle(2),
                rawTypeKind: 0);

            Assert.Equal(
                MetadataFactState.No,
                second.HasOperatorInBindingHierarchy(
                    root,
                    "op_Equality"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DistinctAssemblyVersions_DoNotShareOperatorFacts()
    {
        string directory = Directory.CreateTempSubdirectory("reference-equality-identities-").FullName;
        try
        {
            string v1 = Path.Combine(directory, "v1", "Twin.dll");
            string v2 = Path.Combine(directory, "v2", "Twin.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(v1)!);
            Directory.CreateDirectory(Path.GetDirectoryName(v2)!);
            File.WriteAllBytes(v1, BuildTwin(new Version(1, 0, 0, 0), hasEquality: false));
            File.WriteAllBytes(v2, BuildTwin(new Version(2, 0, 0, 0), hasEquality: true));
            string consumer = Path.Combine(directory, "Consumer.dll");
            File.WriteAllBytes(consumer, BuildIdentityConsumer());

            AssertOrder(consumer, v1, v2, "V1Identity", "V2Identity");
            AssertOrder(consumer, v1, v2, "V2Identity", "V1Identity");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SameNameExternalAssembly_DoesNotUseLocalTypeFacts()
    {
        string directory = Directory.CreateTempSubdirectory("reference-equality-self-identity-").FullName;
        try
        {
            string v1 = Path.Combine(directory, "v1", "Twin.dll");
            string v2 = Path.Combine(directory, "v2", "Twin.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(v1)!);
            Directory.CreateDirectory(Path.GetDirectoryName(v2)!);
            File.WriteAllBytes(v1, BuildTwinEnum(new Version(1, 0, 0, 0)));
            File.WriteAllBytes(v2, BuildSameNameExternalEnumConsumer());

            var resolver = new VersionResolver(v1, v2);
            using var context = new MetadataContext(resolver);
            using var source = MetadataSource.OpenWithoutSymbols(v2, resolver, context);
            var function = IrImporter.Import(source, "Collision.Cases", "Compare");
            Assert.NotNull(function);
            IrPasses.Run(function!);
            function!.CheckInvariant();
            var type = Assert.Single(function.Descendants.OfType<Comparison>()).Left.ResultType!;

            Assert.Equal(new Version(1, 0, 0, 0), type.ResolutionAssembly?.Version);
            Assert.Equal(TypeShapeKind.Enum, source.ClassifyResolvedType(type));
            Assert.True(function.TypeShapes.TryGetValue(type, out var shape));
            Assert.Equal(TypeShape.Unknown, shape);
            Assert.Equal(
                MetadataFactState.No,
                source.HasOperatorInBindingHierarchy(type, "op_Equality"));
            string output = CSharpPrinter.Print(function).Output!;
            Assert.Contains("return left == right;", output);
            Assert.DoesNotContain("(object)", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(2, MetadataFactState.No)]
    [InlineData(5000, MetadataFactState.Unknown)]
    public void WideInterfaceHierarchy_EnforcesWorkBudget(
        int edgeCount,
        MetadataFactState expected)
    {
        string path = Path.Combine(
            Directory.CreateTempSubdirectory("reference-equality-wide-").FullName,
            "Wide.dll");
        try
        {
            File.WriteAllBytes(path, BuildWideInterfaceImage(edgeCount));
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var type = Definition(
                TypeRefDecoder.CanonicalSelf(source.Reader),
                "Wide",
                "IWide",
                ["IWide"],
                AssemblyReferenceIdentity.FromAssemblyDefinition(source.Reader));

            Assert.Equal(
                expected,
                source.HasOperatorInBindingHierarchy(type, "op_Equality"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void VersionedSiblingLocalHierarchyNodes_DoNotShareVisitedIdentity()
    {
        string directory = Directory.CreateTempSubdirectory("reference-equality-versioned-hierarchy-").FullName;
        try
        {
            string v1 = Emit(
                directory,
                "v1",
                "Twin",
                """
                using System.Reflection;

                [assembly: AssemblyVersion("1.0.0.0")]

                namespace N;

                public interface Base<TSelf> : System.IDisposable
                    where TSelf : Base<TSelf>
                {
                    static virtual bool operator ==(TSelf left, TSelf right)
                        => false;
                    static virtual bool operator !=(TSelf left, TSelf right)
                        => true;
                }

                public interface I : Base<I>;
                """);
            string v2 = Emit(
                directory,
                "v2",
                "Twin",
                """
                using System.Reflection;

                [assembly: AssemblyVersion("2.0.0.0")]

                namespace N;

                public interface Base<TSelf>
                    where TSelf : Base<TSelf>;

                public interface I : Base<I>;
                """);
            string rootDirectory = Path.Combine(directory, "root");
            Directory.CreateDirectory(rootDirectory);
            string root = Path.Combine(rootDirectory, "Root.dll");
            File.WriteAllBytes(root, BuildVersionedRootHierarchy());

            var resolver = new VersionResolver(v1, v2, root);
            using var context = new MetadataContext(resolver);
            using var source = MetadataSource.OpenWithoutSymbols(
                typeof(ReferenceEqualityMetadataFactsTests).Assembly.Location,
                resolver,
                context);
            var rootIdentity = new AssemblyReferenceIdentity(
                "Root",
                new Version(1, 0, 0, 0),
                null,
                null);
            var rootType = Definition(
                "Root",
                "N",
                "IRoot",
                ["IRoot"],
                rootIdentity);

            Assert.Equal(
                MetadataFactState.Yes,
                source.HasOperatorInBindingHierarchy(
                    rootType,
                    "op_Equality"));
            Assert.Equal(
                MetadataFactState.Yes,
                source.CrossAssembly.Implements(
                    rootType,
                    TypeRef.CoreLib("System", "IDisposable")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedDynamicAttribute_RemainsUnknown()
    {
        string directory = Directory.CreateTempSubdirectory("malformed-dynamic-fact-").FullName;
        string path = Path.Combine(directory, "MalformedDynamic.dll");
        try
        {
            File.WriteAllBytes(path, BuildMalformedDynamicField());
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var reader = source.Reader;
            var type = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name) == "Carrier");
            var field = reader.GetFieldDefinition(Assert.Single(type.GetFields()));
            var objectType = TypeRef.CoreLib("System", "Object");

            Assert.Equal(
                MetadataFactState.Unknown,
                MethodDefinitionFacts.FieldDynamicFact(reader, field, objectType, objectType));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FunctionPointerRefKinds_DoNotShareSignatureIdentity()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var outAttribute = TypeRef.CoreLib("System.Runtime.InteropServices", "OutAttribute");
        var refParameter = TypeRef.ByRef(intType);
        var outParameter = TypeRef.ByRef(intType).WithCustomModifier(outAttribute, isRequired: true);
        var refPointer = TypeRef.FunctionPointer(voidType, ImmutableArray.Create(refParameter), "");
        var outPointer = TypeRef.FunctionPointer(voidType, ImmutableArray.Create(outParameter), "");

        Assert.NotEqual(refPointer, outPointer);
        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(
            refPointer,
            outPointer,
            allowCoreLibraryAliases: false));
    }

    [Fact]
    public void FunctionPointerRefKinds_SurviveTypeReferenceUpgrade()
    {
        using var source = MetadataSource.Open(typeof(ReferenceEqualityMetadataFactsTests).Assembly.Location);
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var outAttribute = TypeRef.CoreLib("System.Runtime.InteropServices", "OutAttribute");
        var outParameter = TypeRef.ByRef(intType).WithCustomModifier(outAttribute, isRequired: true);
        var pointer = TypeRef.FunctionPointer(voidType, ImmutableArray.Create(outParameter), "");

        var upgraded = source.CrossAssembly.UpgradeTypeReference(pointer);

        Assert.Equal([ArgumentRefKind.Out], upgraded.FunctionPointerParameterRefKinds);
    }

    [Fact]
    public void LocalDefinitionSignature_RequiresTheResolvedAssemblyIdentity()
    {
        var definitionName = MetadataTypeDefinitionName.Create("N", ["C"]) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("C metadata name is invalid"),
        };
        var v1 = new AssemblyReferenceIdentity(
            "Lib",
            new Version(1, 0, 0, 0),
            null,
            null);
        var v2 = v1 with { Version = new Version(2, 0, 0, 0) };
        var localDefinition = TypeRef.DefinitionWithResolution(
            "Lib",
            "N",
            "C",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            resolutionAssembly: null);
        var externalV1 = TypeRef.DefinitionWithResolution(
            "Lib",
            "N",
            "C",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            v1);
        var externalV2 = TypeRef.DefinitionWithResolution(
            "Lib",
            "N",
            "C",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            v2);

        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(
            localDefinition,
            externalV1,
            allowCoreLibraryAliases: false));
        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(
            localDefinition,
            externalV1,
            allowCoreLibraryAliases: false,
            resolvedLocalAssembly: "Lib",
            resolvedLocalAssemblyIdentity: v1));
        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(
            localDefinition,
            externalV1,
            allowCoreLibraryAliases: false,
            resolvedLocalAssembly: "Lib",
            resolvedLocalAssemblyIdentity: v2));
        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(
            localDefinition,
            externalV1,
            allowCoreLibraryAliases: false,
            resolvedLocalAssembly: "Lib",
            resolvedLocalAssemblyIdentity: v2,
            resolvedLocalBindingIdentity: v1));
        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(
            localDefinition,
            externalV1,
            allowCoreLibraryAliases: false,
            resolvedLocalAssembly: "Lib",
            resolvedLocalAssemblyIdentity: v2,
            resolvedLocalBindingIdentity: v2));
        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(
            externalV2,
            externalV1,
            allowCoreLibraryAliases: false,
            resolvedLocalAssembly: "Lib",
            resolvedLocalAssemblyIdentity: v2,
            resolvedLocalBindingIdentity: v1));
    }

    [Fact]
    public void LocalFunctionTypeFactMerge_PreservesExactIdentityAmbiguity()
    {
        var definitionName = MetadataTypeDefinitionName.Create("N", ["E"]) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("E metadata name is invalid"),
        };
        var v1 = new AssemblyReferenceIdentity("Twin", new Version(1, 0, 0, 0), null, null);
        var v2 = v1 with { Version = new Version(2, 0, 0, 0) };
        var externalEnum = TypeRef.DefinitionWithResolution(
            "Twin",
            "N",
            "E",
            ValueTypeHint.ValueType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            v1);
        var localClass = TypeRef.DefinitionWithResolution(
            "Twin",
            "N",
            "E",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            v2);
        var host = SyntheticFunction();
        host.TypeShapes = new Dictionary<TypeRef, TypeShape> { [localClass] = TypeShape.Reference };
        host.TypeFactIdentities = new Dictionary<TypeRef, TypeDefinitionIdentity>
        {
            [localClass] = TypeDefinitionIdentity.Create(localClass)!.Value,
        };
        var body = SyntheticFunction();
        body.TypeShapes = new Dictionary<TypeRef, TypeShape> { [externalEnum] = TypeShape.Enum };
        body.TypeFactIdentities = new Dictionary<TypeRef, TypeDefinitionIdentity>
        {
            [externalEnum] = TypeDefinitionIdentity.Create(externalEnum)!.Value,
        };
        body.EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
        {
            [externalEnum] = new Dictionary<long, string> { [0] = "Zero" },
        };
        body.InterfaceTypes = ImmutableHashSet.Create(externalEnum);

        host.MergeTypeFactsFrom(body);

        Assert.Contains(externalEnum, host.AmbiguousTypeFacts);
        Assert.Equal(TypeShape.Unknown, host.TypeShapes[externalEnum]);
        Assert.DoesNotContain(externalEnum, host.EnumMembers.Keys);
        Assert.DoesNotContain(externalEnum, host.InterfaceTypes);
    }

    static IrFunction SyntheticFunction()
        => new(
            "M",
            TypeRef.Definition("Tests", "Tests", "Host"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            new BlockContainer());

    static TypeRef Definition(
        string assembly,
        string ns,
        string flattenedName,
        ImmutableArray<string> segments,
        AssemblyReferenceIdentity resolutionAssembly)
    {
        var name = MetadataTypeDefinitionName.Create(ns, segments) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("invalid metadata name"),
        };
        return TypeRef.DefinitionWithResolution(
            assembly,
            ns,
            flattenedName,
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            name,
            resolutionAssembly);
    }

    static void AssertOrder(
        string consumer,
        string v1,
        string v2,
        string first,
        string second)
    {
        var resolver = new VersionResolver(v1, v2);
        using var context = new MetadataContext(resolver);
        using var source = MetadataSource.OpenWithoutSymbols(consumer, resolver, context);

        var firstFunction = Import(source, first);
        var secondFunction = Import(source, second);
        var firstType = Assert.Single(firstFunction.Descendants.OfType<Comparison>()).Left.ResultType!;
        var secondType = Assert.Single(secondFunction.Descendants.OfType<Comparison>()).Left.ResultType!;

        Assert.Equal(
            first == "V1Identity" ? MetadataFactState.No : MetadataFactState.Yes,
            source.HasOperatorInBindingHierarchy(firstType, "op_Equality"));
        Assert.Equal(
            second == "V1Identity" ? MetadataFactState.No : MetadataFactState.Yes,
            source.HasOperatorInBindingHierarchy(secondType, "op_Equality"));
        var v1Type = first == "V1Identity" ? firstType : secondType;
        var v2Type = first == "V2Identity" ? firstType : secondType;
        var v1Identity = TypeDefinitionIdentity.Create(v1Type)!.Value;
        var v2Identity = TypeDefinitionIdentity.Create(v2Type)!.Value;
        Assert.NotEqual(v1Identity, v2Identity);
        AssertEquivalentAssemblyIdentityMatches(v1Type);
        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(v1Type, v2Type, allowCoreLibraryAliases: false));
        AssertCoreLibraryVersionAliasesMatch();
        AssertPlatformVersionAliasesMatch();

        var operatorFree = ImmutableHashSet.Create(v1Identity);
        Assert.Contains("return left == right;", PrintSynthetic(v1Type, operatorFree));
        Assert.Contains("return (object)left == (object)right;", PrintSynthetic(v2Type, operatorFree));
    }

    static void AssertEquivalentAssemblyIdentityMatches(TypeRef type)
    {
        var equivalent = TypeRef.DefinitionWithResolution(
            type.Assembly,
            type.Namespace,
            type.Name,
            type.ValueTypeHint,
            type.InlineArray,
            type.EnclosingType,
            type.DefinitionName!,
            type.ResolutionAssembly);

        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(type, equivalent, allowCoreLibraryAliases: false));
    }

    static void AssertCoreLibraryVersionAliasesMatch()
    {
        const string ns = "System.Collections.Generic";
        var definitionName = MetadataTypeDefinitionName.Create(ns, ["List`1"]) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("List metadata name is invalid"),
        };
        var runtime8 = new AssemblyReferenceIdentity(
            "System.Runtime",
            new Version(8, 0, 0, 0),
            null,
            "b03f5f7f11d50a3a");
        var runtime11 = runtime8 with { Version = new Version(11, 0, 0, 0) };
        var first = TypeRef.DefinitionWithResolution(
            TypeRef.CoreLibrary,
            ns,
            "List`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            runtime8);
        var second = TypeRef.DefinitionWithResolution(
            TypeRef.CoreLibrary,
            ns,
            "List`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            runtime11);

        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(first, second, allowCoreLibraryAliases: false));
    }

    static void AssertPlatformVersionAliasesMatch()
    {
        const string ns = "System.Linq";
        var definitionName = MetadataTypeDefinitionName.Create(ns, ["IOrderedEnumerable`1"]) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("IOrderedEnumerable metadata name is invalid"),
        };
        var systemLinq8 = new AssemblyReferenceIdentity(
            "System.Linq",
            new Version(8, 0, 0, 0),
            null,
            "b03f5f7f11d50a3a");
        var systemLinq11 = systemLinq8 with { Version = new Version(11, 0, 0, 0) };
        var first = TypeRef.DefinitionWithResolution(
            "System.Linq",
            ns,
            "IOrderedEnumerable`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            systemLinq8);
        var second = TypeRef.DefinitionWithResolution(
            "System.Linq",
            ns,
            "IOrderedEnumerable`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            systemLinq11);

        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(first, second, allowCoreLibraryAliases: false));
        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(first, second, allowCoreLibraryAliases: true));
    }

    static IrFunction Import(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, "IdentityFixture.Cases", methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static string PrintSynthetic(
        TypeRef type,
        IReadOnlySet<TypeDefinitionIdentity> equalityOperatorFreeTypes)
    {
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "left", type),
            new LoadArgument(1, "right", type));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [
                    new ILInspector.Decompiler.Pipeline.Parameter("left", type),
                    new ILInspector.Decompiler.Pipeline.Parameter("right", type),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [type] = TypeShape.Reference },
            EqualityOperatorFreeTypes = equalityOperatorFreeTypes,
        };
        return CSharpPrinter.Print(function).Output!;
    }

    sealed class VersionResolver(
        string v1,
        string v2,
        string? root = null) : IAssemblyReferenceResolver
    {
        readonly IAssemblyReferenceResolver _runtime = TestAssemblyReferenceResolvers.RuntimeAssemblies();
        readonly string? _root = root;
        readonly Dictionary<Version, string> _paths = new()
        {
            [new Version(1, 0, 0, 0)] = v1,
            [new Version(2, 0, 0, 0)] = v2,
        };

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => identity.Name == "Root"
                && _root is { } rootPath
                    ? ResolvedAssemblyReference.CreateFromPath(
                        rootPath,
                        AssemblyResolutionProvenance.Local("ReferenceEqualityMetadataFactsTests"))
                : identity.Name == "Twin"
                    && identity.Version is { } version
                    && _paths.TryGetValue(version, out var path)
                    ? ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local("ReferenceEqualityMetadataFactsTests"))
                    : _runtime.Resolve(identity, scope);
    }

    static string Emit(
        string directory,
        string subdirectory,
        string assemblyName,
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        string outputDirectory = Path.Combine(directory, subdirectory);
        Directory.CreateDirectory(outputDirectory);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        references.AddRange(RoslynTestReferences.TrustedPlatform);
        if (additionalReferences is not null)
            references.AddRange(additionalReferences);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        string path = Path.Combine(outputDirectory, assemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.True(
            result.Success,
            "fixture compilation failed:\n"
                + string.Join(
                    "\n",
                    result.Diagnostics.Select(
                        diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")));
        return path;
    }

    static byte[] BuildTwin(Version version, bool hasEquality)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Twin.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Twin"),
            version,
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("TwinNs"),
            metadata.GetOrAddString("C"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (hasEquality)
        {
            var firstParameter = metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("left"),
                sequenceNumber: 1);
            metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("right"),
                sequenceNumber: 2);
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("op_Equality"),
                EqualitySignature(metadata, type),
                bodyOffset: 0,
                firstParameter);
        }

        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildVersionedRootHierarchy()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Root.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Root"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var v1 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var v2 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(2, 0, 0, 0),
            default,
            default,
            default,
            default);
        var iV1 = metadata.AddTypeReference(
            v1,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("I"));
        var iV2 = metadata.AddTypeReference(
            v2,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("I"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var root = metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("IRoot"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddInterfaceImplementation(root, iV1);
        metadata.AddInterfaceImplementation(root, iV2);
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildTwinEnum(Version version)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Twin.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Twin"),
            version,
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var enumType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("E"),
            enumType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildSameNameExternalEnumConsumer()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Twin.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Twin"),
            new Version(2, 0, 0, 0),
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var twinV1 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var externalEnum = metadata.AddTypeReference(
            twinV1,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("E"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var localType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("E"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Collision"),
            metadata.GetOrAddString("Cases"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int constructorBody = AddReturnBody(bodyEncoder);
        int compareBody = AddNewObjectCeqBody(
            bodyEncoder,
            MetadataTokens.MethodDefinitionHandle(1));
        var operatorParameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        var compareParameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(0);
        constructorSignature.WriteByte(0x01);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature),
            constructorBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("op_Equality"),
            EqualitySignature(metadata, localType),
            bodyOffset: 0,
            operatorParameters);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Compare"),
            EqualitySignature(metadata, externalEnum, isValueType: true),
            compareBody,
            compareParameters);

        return Serialize(metadata, methodBodies);
    }

    static int AddReturnBody(MethodBodyStreamEncoder encoder)
    {
        var body = new BlobBuilder();
        var instructions = new InstructionEncoder(body, new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ret);
        return encoder.AddMethodBody(instructions, maxStack: 0);
    }

    static int AddNewObjectCeqBody(
        MethodBodyStreamEncoder encoder,
        MethodDefinitionHandle constructor)
    {
        var body = new BlobBuilder();
        var instructions = new InstructionEncoder(body, new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Newobj);
        instructions.Token(constructor);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ldarg_0);
        instructions.OpCode(ILOpCode.Ldarg_1);
        instructions.OpCode(ILOpCode.Ceq);
        instructions.OpCode(ILOpCode.Ret);
        return encoder.AddMethodBody(instructions, maxStack: 2);
    }

    static byte[] BuildMalformedDynamicField()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MalformedDynamic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedDynamic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var expressions = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Linq.Expressions"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var dynamicAttribute = metadata.AddTypeReference(
            expressions,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("DynamicAttribute"));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(0);
        constructorSignature.WriteByte(0x01);
        var constructor = metadata.AddMemberReference(
            dynamicAttribute,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("Carrier"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        fieldSignature.WriteByte(0x1c);
        var field = metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddCustomAttribute(
            field,
            constructor,
            metadata.GetOrAddBlob(new byte[] { 0, 0, 0, 0 }));
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildIdentityConsumer()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Consumer.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Consumer"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var twinV1 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var twinV2 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(2, 0, 0, 0),
            default,
            default,
            default,
            default);
        var cV1 = metadata.AddTypeReference(
            twinV1,
            metadata.GetOrAddString("TwinNs"),
            metadata.GetOrAddString("C"));
        var cV2 = metadata.AddTypeReference(
            twinV2,
            metadata.GetOrAddString("TwinNs"),
            metadata.GetOrAddString("C"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("IdentityFixture"),
            metadata.GetOrAddString("Cases"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int v1Body = AddCeqBody(bodyEncoder);
        int v2Body = AddCeqBody(bodyEncoder);
        var v1Parameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        var v2Parameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("V1Identity"),
            EqualitySignature(metadata, cV1),
            v1Body,
            v1Parameters);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("V2Identity"),
            EqualitySignature(metadata, cV2),
            v2Body,
            v2Parameters);

        return Serialize(metadata, methodBodies);
    }

    static int AddCeqBody(MethodBodyStreamEncoder encoder)
    {
        var body = new BlobBuilder();
        var instructions = new InstructionEncoder(body, new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldarg_0);
        instructions.OpCode(ILOpCode.Ldarg_1);
        instructions.OpCode(ILOpCode.Ceq);
        instructions.OpCode(ILOpCode.Ret);
        return encoder.AddMethodBody(instructions, maxStack: 2);
    }

    static BlobHandle EqualitySignature(
        MetadataBuilder metadata,
        EntityHandle type,
        bool isValueType = false)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                2,
                returnType => returnType.Type().Boolean(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(type, isValueType);
                    parameters.AddParameter().Type().Type(type, isValueType);
                });
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] BuildWideInterfaceImage(int edgeCount)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Wide.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Wide"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var baseInterface = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddString("IBase"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var wideInterface = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddString("IWide"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < edgeCount; i++)
            metadata.AddInterfaceImplementation(wideInterface, baseInterface);
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildStructuredHierarchyCollision()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("StructuredHierarchy.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("StructuredHierarchy"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0xb0, 0x3f, 0x5f, 0x7f,
                    0x11, 0xd5, 0x0a, 0x3a,
                }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var operatorBase = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("OperatorBase"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("A"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        var nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("B"),
            operatorBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(nested, outer);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("A+B"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Collision"),
            metadata.GetOrAddString("Cases"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int operatorBody = AddCeqBody(bodyEncoder);
        int compareBody = AddCeqBody(bodyEncoder);
        var operatorParameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        var compareParameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.SpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("op_Equality"),
            EqualitySignature(metadata, operatorBase),
            operatorBody,
            operatorParameters);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Compare"),
            EqualitySignature(metadata, nested),
            compareBody,
            compareParameters);

        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildHierarchyAllocationImage(
        int typeCount,
        int nameLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("HierarchyAllocation.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("HierarchyAllocation"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("IRoot"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StringHandle decoyName =
            metadata.GetOrAddString(new string('A', nameLength));
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic | TypeAttributes.Class,
                metadata.GetOrAddString("Decoys"),
                decoyName,
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildHandleProvenanceImage(bool shiftRoot)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("HandleProvenance.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("HandleProvenance"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (shiftRoot)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Decoy"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("IRoot"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(
                shiftRoot ? 2 : 1));
        if (shiftRoot)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("op_Equality"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x02 }),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] Serialize(MetadataBuilder metadata, BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
