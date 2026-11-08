using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

/// <summary>
/// Boundary cases of the Library symbol document: emitted metadata the C#
/// compiler cannot produce, and compiled declarations whose metadata
/// encoding differs from their C# spelling.
/// </summary>
public sealed class LibrarySymbolDocumentFixtureTests
{
    private static readonly LibrarySymbolDocumentSource s_source =
        new("Fixture.dll", "local", "", "", "", "");

    [Fact]
    public async Task RepeatedTypeIdentityKeepsBothRecordsAndIsPartial()
    {
        byte[] image = Emit(builder =>
        {
            builder.Class("Dup", "Thing", TypeAttributes.Public);
            builder.Class("Dup", "Thing", TypeAttributes.Public);
        });

        (LibrarySymbolDocumentOutcome outcome, JsonDocument document) =
            await ProduceAsync(image, MetadataLibrarySymbolAdmission.PublicApi);
        using (document)
        {
            var written = Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
            Assert.False(written.IsComplete);
            Assert.Equal(2, written.Types);
            JsonElement root = document.RootElement;
            Assert.True(root.GetProperty("types").TryGetProperty("T:Dup.Thing", out _));
            JsonElement collision = Assert.Single(root.GetProperty("type_collisions").EnumerateArray());
            Assert.Equal("T:Dup.Thing", collision.GetProperty("documentation_id").GetString());
            Assert.Equal("Thing", collision.GetProperty("record").GetProperty("name").GetString());
            JsonElement declarations = root.GetProperty("section_outcomes")[0];
            Assert.Equal("partial", declarations.GetProperty("status").GetString());
            Assert.Contains("T:Dup.Thing", declarations.GetProperty("detail").GetString());
            Assert.Equal("partial", root.GetProperty("outcome").GetString());
            Assert.Equal(
                ["T:Dup.Thing"],
                root.GetProperty("base_type_derived_types").GetProperty("T:System.Object")
                    .EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Fact]
    public async Task CyclicTypeSpecificationIsDecodedWithoutOverflow()
    {
        // TypeSpec row 1 is a required custom modifier naming TypeSpec row 1.
        byte[] image = Emit(builder =>
        {
            TypeSpecificationHandle cycle = builder.Metadata.AddTypeSpecification(
                builder.Metadata.GetOrAddBlob(new byte[] { 0x1F, 0x06, 0x08 }));
            builder.Class("Bad", "Cycle", TypeAttributes.Public, baseType: cycle);
        });

        (LibrarySymbolDocumentOutcome outcome, JsonDocument document) =
            await ProduceAsync(image, MetadataLibrarySymbolAdmission.PublicApi);
        using (document)
        {
            Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
            Assert.True(document.RootElement.GetProperty("types").TryGetProperty("T:Bad.Cycle", out _));
        }
    }

    [Fact]
    public async Task ExternalBaseNeverResolvesToALocalNamesake()
    {
        byte[] image = Emit(builder =>
        {
            AssemblyReferenceHandle external = builder.Metadata.AddAssemblyReference(
                builder.Metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            TypeReferenceHandle externalBase = builder.Metadata.AddTypeReference(
                external,
                builder.Metadata.GetOrAddString("Conflict"),
                builder.Metadata.GetOrAddString("Base"));
            builder.Class(
                "Conflict",
                "Base",
                TypeAttributes.Public | TypeAttributes.Abstract,
                abstractMethods: ["UnrelatedLocalObligation"]);
            builder.Class(
                "Conflict",
                "Derived",
                TypeAttributes.Public | TypeAttributes.Abstract,
                baseType: externalBase);
        });

        (_, JsonDocument document) =
            await ProduceAsync(image, MetadataLibrarySymbolAdmission.PublicApi);
        using (document)
        {
            JsonElement derived = document.RootElement.GetProperty("types").GetProperty("T:Conflict.Derived");
            Assert.Empty(derived.GetProperty("required_overrides").EnumerateArray());
            Assert.False(derived.GetProperty("required_overrides_complete").GetBoolean());
        }
    }

    [Fact]
    public async Task NestedForwardersAreAdmitted()
    {
        // The compiler writes a nested forwarding row with no visibility.
        byte[] image = Emit(builder =>
        {
            AssemblyReferenceHandle target = builder.Metadata.AddAssemblyReference(
                builder.Metadata.GetOrAddString("Target"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            ExportedTypeHandle outer = builder.Metadata.AddExportedType(
                (TypeAttributes)0x00200000, // tdForwarder (ECMA-335 II.23.1.15)
                builder.Metadata.GetOrAddString("Forwarded"),
                builder.Metadata.GetOrAddString("Outer"),
                target,
                0);
            builder.Metadata.AddExportedType(
                default,
                default,
                builder.Metadata.GetOrAddString("Inner"),
                outer,
                0);
        });

        foreach (MetadataLibrarySymbolAdmission admission in Enum.GetValues<MetadataLibrarySymbolAdmission>())
        {
            (_, JsonDocument document) = await ProduceAsync(image, admission);
            using (document)
            {
                JsonElement inner = document.RootElement.GetProperty("types")
                    .GetProperty("T:Forwarded.Outer.Inner");
                Assert.Equal("forwarder", inner.GetProperty("declaration_kind").GetString());
                Assert.Equal("T:Forwarded.Outer", inner.GetProperty("declaring_type").GetString());
                Assert.Equal("Target", inner.GetProperty("forwarded_to_assembly").GetString());
                Assert.True(document.RootElement.GetProperty("types").TryGetProperty("T:Forwarded.Outer", out _));
            }
        }
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(10, false)]
    public async Task MemberBudgetBoundsTheWholeLibrary(int maxMembers, bool written)
    {
        // Three Types of four Members each: no Type reaches ten, the Library
        // does.
        byte[] image = Emit(builder =>
        {
            foreach (string name in new[] { "A", "B", "C" })
            {
                builder.Class(
                    "Many",
                    name,
                    TypeAttributes.Public | TypeAttributes.Abstract,
                    abstractMethods: ["M1", "M2", "M3", "M4"]);
            }
        });

        (LibrarySymbolDocumentOutcome outcome, JsonDocument document) = await ProduceAsync(
            image,
            MetadataLibrarySymbolAdmission.PublicApi,
            maxMembers);
        document.Dispose();
        if (written)
        {
            Assert.Equal(12, Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome).Members);
        }
        else
        {
            Assert.Equal(
                LibrarySymbolDocumentAbort.MemberBudget,
                Assert.IsType<LibrarySymbolDocumentOutcome.Aborted>(outcome).Reason);
        }
    }

    [Fact]
    public async Task CompiledDeclarationsCarryTheirCSharpFacts()
    {
        byte[] image = await File.ReadAllBytesAsync(
            typeof(SymbolFixtures.CovariantBase).Assembly.Location,
            TestContext.Current.CancellationToken);
        (_, JsonDocument document) =
            await ProduceAsync(image, MetadataLibrarySymbolAdmission.PublicApi);
        using (document)
        {
            JsonElement root = document.RootElement;
            const string Namespace = "DotnetInspector.Sections.Tests.SymbolFixtures";

            // A covariant return override is emitted newslot with a MethodImpl.
            JsonElement make = Member(root, $"M:{Namespace}.CovariantDerived.Make");
            Assert.True(make.GetProperty("is_override").GetBoolean());
            Assert.False(make.GetProperty("is_virtual").GetBoolean());
            Assert.Equal($"M:{Namespace}.CovariantBase.Make", make.GetProperty("overrides").GetString());
            Assert.Equal(
                [$"M:{Namespace}.CovariantAbstractDerived.Other"],
                root.GetProperty("types").GetProperty($"T:{Namespace}.CovariantAbstractDerived")
                    .GetProperty("required_overrides").EnumerateArray().Select(value => value.GetString()));

            // A ref return produces and mentions its referent.
            foreach (string id in new[] { $"M:{Namespace}.RefReturns.Value", $"P:{Namespace}.RefReturns.Property" })
            {
                JsonElement member = Member(root, id);
                Assert.Equal(["T:System.Int32"], Strings(member.GetProperty("produced_types")));
                Assert.Equal(["T:System.Int32"], Strings(member.GetProperty("return_type_mentions")));
            }

            // Function pointers are compared by signature: the int override
            // leaves the long overload's obligation outstanding. The identity
            // is the Type document's, whose function-pointer parameter list is
            // empty until #11049.
            Assert.Equal(
                [$"M:{Namespace}.FunctionMiddle.M()"],
                Strings(root.GetProperty("types").GetProperty($"T:{Namespace}.FunctionDerived")
                    .GetProperty("required_overrides")));
            Assert.Equal(
                $"M:{Namespace}.FunctionBase.M()",
                root.GetProperty("members").EnumerateArray()
                    .Single(member =>
                        member.GetProperty("declaring_type").GetString() == $"T:{Namespace}.FunctionDerived"
                        && member.GetProperty("name").GetString() == "M")
                    .GetProperty("overrides").GetString());

            // A function pointer's calling convention is part of its
            // signature: the Cdecl override leaves the Stdcall obligation.
            Assert.Equal(
                [$"M:{Namespace}.ConventionMiddle.M()"],
                Strings(root.GetProperty("types").GetProperty($"T:{Namespace}.ConventionDerived")
                    .GetProperty("required_overrides")));
            Assert.Equal(
                $"M:{Namespace}.ConventionBase.M()",
                root.GetProperty("members").EnumerateArray()
                    .Single(member =>
                        member.GetProperty("declaring_type").GetString() == $"T:{Namespace}.ConventionDerived"
                        && member.GetProperty("name").GetString() == "M")
                    .GetProperty("overrides").GetString());

            // An internal abstract getter keeps its property's obligation
            // inaccessible, whatever its setter's access.
            JsonElement internalGetter = root.GetProperty("types").GetProperty($"T:{Namespace}.InternalGetter");
            Assert.Equal(
                [$"P:{Namespace}.InternalGetter.P"],
                Strings(internalGetter.GetProperty("required_overrides")));
            Assert.Equal(1, internalGetter.GetProperty("inaccessible_required_overrides").GetInt32());

            // Pointer and function-pointer parameters mention their primitives.
            foreach ((string name, string[] mentions) in new[]
            {
                ("P", new[] { "T:System.Int32" }),
                ("F", new[] { "T:System.Int32", "T:System.Int64" }),
            })
            {
                JsonElement member = root.GetProperty("members").EnumerateArray().Single(candidate =>
                    candidate.GetProperty("declaring_type").GetString() == $"T:{Namespace}.Pointers"
                    && candidate.GetProperty("name").GetString() == name);
                Assert.Equal(mentions, Strings(member.GetProperty("parameters")[0].GetProperty("type_mentions")));
            }

            // Static interface Members occupy slots without overriding.
            JsonElement staticAbstract = Member(root, $"M:{Namespace}.IStaticMembers.Abstract");
            Assert.False(staticAbstract.GetProperty("is_override").GetBoolean());
            Assert.True(staticAbstract.GetProperty("is_abstract").GetBoolean());
            JsonElement staticVirtual = Member(root, $"M:{Namespace}.IStaticMembers.Virtual");
            Assert.False(staticVirtual.GetProperty("is_override").GetBoolean());
            Assert.True(staticVirtual.GetProperty("is_virtual").GetBoolean());

            // Indexer parameters mention Types at every depth.
            Assert.Equal(
                ["T:System.Collections.Generic.List`1", "T:System.Int32"],
                Strings(Member(root, $"P:{Namespace}.Indexers.Item(System.Collections.Generic.List{{System.Int32}})")
                    .GetProperty("parameters")[0].GetProperty("type_mentions")));
            Assert.Equal(
                ["T:System.Int32"],
                Strings(Member(root, $"P:{Namespace}.Indexers.Item(System.Int32[0:,0:])")
                    .GetProperty("parameters")[0].GetProperty("type_mentions")));
        }
    }

    [Fact]
    public async Task CoreLibraryWritesACompleteDocument()
    {
        byte[] image = await File.ReadAllBytesAsync(
            typeof(object).Assembly.Location,
            TestContext.Current.CancellationToken);
        (LibrarySymbolDocumentOutcome outcome, JsonDocument document) =
            await ProduceAsync(image, MetadataLibrarySymbolAdmission.PublicApi);
        using (document)
        {
            var written = Assert.IsType<LibrarySymbolDocumentOutcome.Written>(outcome);
            Assert.True(written.IsComplete);
            JsonElement root = document.RootElement;
            JsonElement stream = root.GetProperty("types").GetProperty("T:System.IO.Stream");
            Assert.True(stream.GetProperty("is_abstract").GetBoolean());
            Assert.Contains(
                "M:System.IO.Stream.Read(System.Byte[],System.Int32,System.Int32)",
                Strings(stream.GetProperty("required_overrides")));
            Assert.Contains(
                "T:System.IO.MemoryStream",
                Strings(root.GetProperty("base_type_derived_types").GetProperty("T:System.IO.Stream")));
        }
    }

    [Fact]
    public async Task TypeRecordsCountAgainstTheTextAndForwarderBudgets()
    {
        byte[] withForwarder = Emit(builder =>
        {
            builder.Metadata.AddExportedType(
                (TypeAttributes)0x00200000, // tdForwarder (ECMA-335 II.23.1.15)
                builder.Metadata.GetOrAddString("Forwarded"),
                builder.Metadata.GetOrAddString("Outer"),
                builder.Metadata.AddAssemblyReference(
                    builder.Metadata.GetOrAddString("Target"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default),
                0);
            builder.Class("Small", "Type", TypeAttributes.Public | TypeAttributes.Abstract);
        });

        (LibrarySymbolDocumentOutcome noText, JsonDocument first) = await ProduceAsync(
            withForwarder, MetadataLibrarySymbolAdmission.PublicApi, maxRetainedTextCharacters: 0);
        first.Dispose();
        Assert.Equal(
            LibrarySymbolDocumentAbort.RetainedTextBudget,
            Assert.IsType<LibrarySymbolDocumentOutcome.Aborted>(noText).Reason);

        (LibrarySymbolDocumentOutcome noForwarders, JsonDocument second) = await ProduceAsync(
            withForwarder, MetadataLibrarySymbolAdmission.PublicApi, maxTypeForwarders: 0);
        second.Dispose();
        Assert.Equal(
            LibrarySymbolDocumentAbort.TypeForwarderBudget,
            Assert.IsType<LibrarySymbolDocumentOutcome.Aborted>(noForwarders).Reason);
    }

    private static async Task<(LibrarySymbolDocumentOutcome Outcome, JsonDocument Document)> ProduceAsync(
        byte[] image,
        MetadataLibrarySymbolAdmission admission,
        int maxMembers = 1_000_000,
        int maxRetainedTextCharacters = 100_000_000,
        int maxTypeForwarders = 10_000)
    {
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                image,
                LibraryInspectionTestLibrary.Identity(image));
        using var destination = new MemoryStream();
        LibrarySymbolDocumentOutcome outcome = LibrarySymbolDocumentOperation.Execute(
            new LibrarySymbolDocumentRequest(
                library.Reference,
                admission,
                new LibrarySymbolDocumentGenerator("dotnet-inspect", "test"),
                s_source,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 50_000,
                    maxMembers,
                    maxInspectionFailures: 1_000,
                    maxTypeForwarders,
                    maxMetadataRows: 10_000_000,
                    maxRetainedTextCharacters)),
            library.IssueOperation(),
            destination,
            TestContext.Current.CancellationToken);
        await library.RetireAsync();
        return (
            outcome,
            outcome is LibrarySymbolDocumentOutcome.Written
                ? JsonDocument.Parse(destination.ToArray())
                : JsonDocument.Parse("{}"));
    }

    private static JsonElement Member(JsonElement root, string id) =>
        Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            member => member.GetProperty("documentation_id").GetString() == id);

    private static string[] Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(value => value.GetString()!)];

    private static byte[] Emit(Action<FixtureBuilder> define)
    {
        var builder = new FixtureBuilder();
        define(builder);
        return builder.Serialize();
    }

    private sealed class FixtureBuilder
    {
        private readonly TypeReferenceHandle _object;

        internal FixtureBuilder()
        {
            Metadata.AddModule(
                0,
                Metadata.GetOrAddString("Fixture.dll"),
                Metadata.GetOrAddGuid(new Guid("0b9a2b4e-6c1d-4f3a-9e57-1d2c3b4a5f60")),
                default,
                default);
            Metadata.AddAssembly(
                Metadata.GetOrAddString("Fixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                AssemblyHashAlgorithm.None);
            AssemblyReferenceHandle runtime = Metadata.AddAssemblyReference(
                Metadata.GetOrAddString("System.Runtime"),
                new Version(10, 0, 0, 0),
                default,
                default,
                default,
                default);
            _object = Metadata.AddTypeReference(
                runtime,
                Metadata.GetOrAddString("System"),
                Metadata.GetOrAddString("Object"));
            Metadata.AddTypeDefinition(
                default,
                default,
                Metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        internal MetadataBuilder Metadata { get; } = new();

        internal void Class(
            string ns,
            string name,
            TypeAttributes attributes,
            EntityHandle baseType = default,
            string[]? abstractMethods = null)
        {
            Metadata.AddTypeDefinition(
                attributes | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
                Metadata.GetOrAddString(ns),
                Metadata.GetOrAddString(name),
                baseType.IsNil ? _object : baseType,
                MetadataTokens.FieldDefinitionHandle(Metadata.GetRowCount(TableIndex.Field) + 1),
                MetadataTokens.MethodDefinitionHandle(Metadata.GetRowCount(TableIndex.MethodDef) + 1));
            foreach (string method in abstractMethods ?? [])
            {
                var signature = new BlobBuilder();
                new BlobEncoder(signature)
                    .MethodSignature(isInstanceMethod: true)
                    .Parameters(0, returnType => returnType.Void(), _ => { });
                Metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Virtual
                        | MethodAttributes.Abstract
                        | MethodAttributes.HideBySig
                        | MethodAttributes.NewSlot,
                    MethodImplAttributes.IL,
                    Metadata.GetOrAddString(method),
                    Metadata.GetOrAddBlob(signature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(Metadata.GetRowCount(TableIndex.Param) + 1));
            }
        }

        internal byte[] Serialize()
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                    PEHeaderBuilder.CreateLibraryHeader(),
                    new MetadataRootBuilder(Metadata),
                    new BlobBuilder())
                .Serialize(image);
            return image.ToArray();
        }
    }
}
