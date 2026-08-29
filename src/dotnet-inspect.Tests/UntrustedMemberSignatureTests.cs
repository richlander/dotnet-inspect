using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.CSharp;
using CSharpText;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Gates for issue #3319 on the member signature channel. A field, property, or
/// event name is untrusted metadata, and it is rendered into a signature cell of
/// a Markdown table. A hostile name must not be able to break that cell or move
/// the terminal cursor once the table is rendered.
/// </summary>
/// <remarks>
/// The first fix for #3319 covered the C# declaration and body producers but left
/// this channel keyword-escaped only; an adversarial reviewer found it. The
/// rendered evidence before the fix was a signature cell reading
/// <c>public int N&lt;VT&gt;    INJECTED</c> with a live vertical tab in it.
/// </remarks>
[Collection("Console")]
public class UntrustedMemberSignatureTests
{
    static readonly CSharpFormatter Formatter = new();

    // NUL is deliberately absent: metadata names live in the NUL-terminated
    // #Strings heap, so a name cannot carry one and the fixture builder truncates
    // it. The primitive still treats it as a hazard.
    public static TheoryData<string> Hazards => new()
    {
        "\n", "\r\n", "\u2028", "\v", "\u001b[31m", "\u202e",
    };

    [Theory]
    [MemberData(nameof(Hazards))]
    public void FieldPropertyAndEventSignatures_ContainHostileNames(string hazard)
    {
        string hostile = $"N{hazard}    INJECTED";
        string dir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-sig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var assemblyName = new AssemblyName("HostileSignature");
            var ab = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var mb = ab.DefineDynamicModule(assemblyName.Name!);
            var hostileType = mb.DefineType(
                $"Hostile.Type{hazard}INJECTED",
                TypeAttributes.Public | TypeAttributes.Class);
            var hostileDelegate = mb.DefineType(
                $"Hostile.Handler{hazard}INJECTED",
                TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.Class,
                typeof(MulticastDelegate));
            var delegateConstructor = hostileDelegate.DefineConstructor(
                MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.RTSpecialName,
                CallingConventions.Standard,
                [typeof(object), typeof(IntPtr)]);
            delegateConstructor.SetImplementationFlags(
                MethodImplAttributes.Runtime
                    | MethodImplAttributes.Managed);
            var delegateInvoke = hostileDelegate.DefineMethod(
                "Invoke",
                MethodAttributes.Public
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.HideBySig,
                typeof(void),
                [typeof(object), typeof(EventArgs)]);
            delegateInvoke.SetImplementationFlags(
                MethodImplAttributes.Runtime
                    | MethodImplAttributes.Managed);
            var tb = mb.DefineType("Hostile.Bag", TypeAttributes.Public | TypeAttributes.Class);

            tb.DefineField(hostile, typeof(int), FieldAttributes.Public);

            var backing = tb.DefineField(
                "_p",
                hostileType,
                FieldAttributes.Private);
            var method = tb.DefineMethod(
                hostile,
                MethodAttributes.Public | MethodAttributes.Static,
                hostileType,
                [hostileType]);
            var methodIl = method.GetILGenerator();
            methodIl.Emit(OpCodes.Ldnull);
            methodIl.Emit(OpCodes.Ret);
            var property = tb.DefineProperty(
                hostile,
                PropertyAttributes.None,
                hostileType,
                null);
            var getter = tb.DefineMethod(
                "get_" + hostile,
                MethodAttributes.Public | MethodAttributes.SpecialName,
                hostileType,
                Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, backing);
            il.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);

            var @event = tb.DefineEvent(
                hostile,
                EventAttributes.None,
                hostileDelegate);
            var adder = tb.DefineMethod(
                "add_" + hostile,
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(void),
                [hostileDelegate]);
            adder.GetILGenerator().Emit(OpCodes.Ret);
            @event.SetAddOnMethod(adder);

            hostileType.CreateType();
            hostileDelegate.CreateType();
            tb.CreateType();

            string dllPath = Path.Combine(dir, "HostileSignature.dll");
            ab.Save(dllPath);

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var type = Assert.Single(surface.Types, t => t.Name == "Bag");

            var kinds = type.Members
                .Where(m => m.Kind is "field" or "property" or "event" or "method" && m.Name.Contains("INJECTED", StringComparison.Ordinal))
                .ToList();

            // Non-vacuity: all four hostile member kinds really were extracted,
            // so a silently dropped kind cannot make this pass.
            Assert.Equal(
                new[] { "event", "field", "method", "property" },
                kinds.Select(m => m.Kind).OrderBy(k => k, StringComparer.Ordinal).ToArray());

            foreach (var member in kinds)
            {
                // The signature the extractor itself produces, which the type
                // tree renders directly without going through the formatter.
                if (member.Signature is { Length: > 0 } extracted)
                    AssertContained(extracted);

                // The signature cell, which is also what the decompiled and
                // annotated source blocks render.
                AssertContained(Formatter.FormatMember(type, member));

                // The Name column and the "# Type.Member" heading.
                AssertContained(OperatorNames.FormatDisplayName(member.Name));
            }

            foreach (ApiMember typedMember in kinds.Where(
                member => member.Kind is "method" or "property" or "event"))
            {
                Assert.Contains(
                    hazard,
                    typedMember.SignatureModel!.ReturnType);
            }
            ApiMember hostileMethod = Assert.Single(
                kinds,
                member => member.Kind == "method");
            Assert.Contains(
                hazard,
                Assert.Single(
                    hostileMethod.SignatureModel!.Parameters).Type);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FieldAndEnumDeclarations_ContainNamesBeforeComposition()
    {
        const string Hazard = "\u202e";
        const string LiteralEscape = "\\u202e";
        string dir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-member-names-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var assemblyName = new AssemblyName("HostileDeclarationNames");
            var assembly = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assembly.DefineDynamicModule(assemblyName.Name!);
            var holder = module.DefineType(
                "Hostile.Holder",
                TypeAttributes.Public | TypeAttributes.Class);
            holder.DefineField(
                $"Scalar({Hazard}INJECTED",
                typeof(int),
                FieldAttributes.Public);
            holder.DefineField(
                $"Literal({LiteralEscape}INJECTED",
                typeof(int),
                FieldAttributes.Public);
            holder.CreateType();

            var enumBuilder = module.DefineEnum(
                "Hostile.Values",
                TypeAttributes.Public,
                typeof(int));
            enumBuilder.DefineLiteral(
                $"Scalar{Hazard}INJECTED",
                1);
            enumBuilder.DefineLiteral(
                $"Literal{LiteralEscape}INJECTED",
                2);
            enumBuilder.CreateType();

            string path = Path.Combine(dir, "HostileDeclarationNames.dll");
            assembly.Save(path);
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ApiSurface surface = ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
            ApiType holderType = Assert.Single(
                surface.Types,
                type => type.Name == "Holder");
            ApiType enumType = Assert.Single(
                surface.Types,
                type => type.Name == "Values");

            ApiMember scalarFieldModel = Assert.Single(
                holderType.Members,
                member => member.Name.StartsWith(
                    "Scalar",
                    StringComparison.Ordinal));
            ApiMember literalFieldModel = Assert.Single(
                holderType.Members,
                member => member.Name.StartsWith(
                    "Literal",
                    StringComparison.Ordinal));
            Assert.Contains(Hazard, scalarFieldModel.Name);
            Assert.Contains(LiteralEscape, literalFieldModel.Name);
            Assert.Contains(
                enumType.Members,
                member => member.Name.Contains(
                    Hazard,
                    StringComparison.Ordinal));
            Assert.Contains(
                enumType.Members,
                member => member.Name.Contains(
                    LiteralEscape,
                    StringComparison.Ordinal));

            string scalarField = Formatter.FormatMember(
                holderType,
                scalarFieldModel);
            string literalField = Formatter.FormatMember(
                holderType,
                literalFieldModel);
            string enumSource = Assert.Single(
                new CSharpTypePrinter()
                    .Print(new CSharpTypePrintRequest(enumType))
                    .Units)
                .Source;

            AssertContained(scalarField);
            AssertContained(literalField);
            Assert.DoesNotContain(
                enumSource,
                HostileOutputAssert.IsForbidden);
            Assert.NotEqual(scalarField, literalField);
            Assert.Contains(
                @"Literal(\\u202eINJECTED",
                literalField,
                StringComparison.Ordinal);
            Assert.Contains(
                @"Literal\\u202eINJECTED",
                enumSource,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StructuredMetadataDefaultFallback_PreservesContainedSignatureAndStatus()
    {
        string path = EmitStructuredMetadataDefault();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            ApiSurface surface = ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: true);
            ApiType type = Assert.Single(
                surface.Types,
                candidate => candidate.Name == "Probe");
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == "M");
            const string Expected =
                @"void M([System.Runtime.InteropServices.Optional, "
                + @"System.Runtime.CompilerServices.DateTimeConstant(42L)] "
                + @"System.DateTime when\\marker)";

            Assert.Equal(Expected, member.Signature);
            Assert.EndsWith(
                Expected,
                Formatter.FormatMember(type, member),
                StringComparison.Ordinal);

            TypeShapeView shape = ApiOutputFormatter.BuildShapeView(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                memberFilter: []);
            Assert.Equal(
                Expected,
                Assert.Single(
                    Assert.Single(
                        shape.Members,
                        node => node.Text.StartsWith(
                            "Methods",
                            StringComparison.Ordinal))
                        .Children!)
                    .Text);

            ApiArtifactJson.Prepare(type);
            using JsonDocument artifact = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    type,
                    ApiArtifactJson.CompactType));
            JsonElement artifactMember = artifact.RootElement
                .GetProperty("members")[0];
            Assert.Equal(
                Expected,
                artifactMember.GetProperty("signature").GetString());
            Assert.False(
                artifactMember.TryGetProperty(
                    "signature_decode_status",
                    out _));

            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinitionHandle typeHandle =
                reader.TypeDefinitions.Single(
                    handle => reader.GetString(
                        reader.GetTypeDefinition(handle).Name) == "Probe");
            ApiType queriedType = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true);
            ApiMember queriedMember = Assert.Single(
                queriedType.Members,
                candidate => candidate.Name == "M");
            string queriedCompatibility =
                Formatter.FormatCompatibilityMemberSignature(
                    queriedType,
                    queriedMember,
                    out bool renderedFromModel);

            Assert.False(renderedFromModel);
            Assert.Equal(Expected, queriedMember.Signature);
            Assert.Equal(Expected, queriedCompatibility);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SynthesizedAccessorFallback_ContainsRawSignatureSlotsInDecompiledSource()
    {
        string path = EmitHostileIndexer();
        try
        {
            using (var stream = File.OpenRead(path))
            using (var peReader = new PEReader(stream))
            {
                ApiSurface surface = ApiSurfaceExtractor.Extract(
                    peReader,
                    includeAll: true);
                ApiType type = Assert.Single(
                    surface.Types,
                    candidate => candidate.Name == "Probe");
                ApiMember property = Assert.Single(
                    type.Members,
                    candidate => candidate.Name == "Item");
                ApiMember accessor = Assert.Single(
                    ApiOutputFormatter.AccessorMethods(property, type));

                Assert.Contains(
                    '\u202E',
                    Assert.Single(
                        accessor.SignatureModel!.Parameters).Name);
                Assert.DoesNotContain('\u202E', accessor.Signature!);
                Assert.Contains(
                    "get_Item(System.DateTime idx_evil)",
                    accessor.Signature,
                    StringComparison.Ordinal);
            }

            var result = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(new MemberOptions
                {
                    TypeName = "Probe",
                    AssemblyPath = path,
                    MemberFilter = ["Item"],
                    IncludeSections = [SectionNames.DecompiledSource],
                    TipLevel = TipLevel.Quiet,
                    Verbosity = Verbosity.Normal,
                }));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                "## Decompiled Source",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "get_Item(System.DateTime idx_evil)",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                result.Output,
                HostileOutputAssert.IsForbidden);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SynthesizedExplicitInterfaceAccessor_UsesRecordedMethodDefName()
    {
        string path = EmitExplicitInterfaceProperty();
        try
        {
            using (var stream = File.OpenRead(path))
            using (var peReader = new PEReader(stream))
            {
                ApiSurface surface = ApiSurfaceExtractor.Extract(
                    peReader,
                    includeAll: true);
                ApiType type = Assert.Single(
                    surface.Types,
                    candidate => candidate.Name == "Probe");
                ApiMember property = Assert.Single(
                    type.Members,
                    candidate => candidate.Kind == "property");
                ApiAccessor recordedAccessor = Assert.Single(
                    property.SignatureModel!.Accessors,
                    candidate => candidate.Kind == "get");
                ApiMember accessor = Assert.Single(
                    ApiOutputFormatter.AccessorMethods(property, type));

                Assert.Equal("I.get_P", recordedAccessor.Name);
                Assert.Equal(recordedAccessor.Name, accessor.Name);
                Assert.Equal(
                    recordedAccessor.Name,
                    accessor.SignatureModel!.MemberName);
                Assert.Contains(
                    "I.get_P()",
                    accessor.Signature,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "get_I.P",
                    accessor.Signature,
                    StringComparison.Ordinal);
            }

            var result = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(new MemberOptions
                {
                    TypeName = "Probe",
                    AssemblyPath = path,
                    MemberFilter = ["I.P"],
                    IncludeAll = true,
                    IncludeSections = [SectionNames.DecompiledSource],
                    TipLevel = TipLevel.Quiet,
                    Verbosity = Verbosity.Normal,
                }));

            Assert.True(
                result.ExitCode == 0,
                $"Command failed: {result.Error}{Environment.NewLine}{result.Output}");
            Assert.Contains(
                "## Decompiled Source",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "I.get_P()",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "get_I.P",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SynthesizedAccessor_OlderSurfaceUsesConventionalNameFallback()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "Probe",
            Kind = "class",
        };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            GetterToken = 0x0600_0001,
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                Accessors =
                [
                    new ApiAccessor { Kind = "get" },
                ],
            },
        };

        ApiMember accessor = Assert.Single(
            ApiOutputFormatter.AccessorMethods(property, type));

        Assert.Equal("get_Value", accessor.Name);
        Assert.Equal("get_Value", accessor.SignatureModel!.MemberName);
        Assert.Contains(
            "get_Value()",
            accessor.Signature,
            StringComparison.Ordinal);
    }

    static string EmitStructuredMetadataDefault()
    {
        var assemblyName = new AssemblyName("StructuredMetadataDefault");
        var assembly = new PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "Probe",
            TypeAttributes.Public | TypeAttributes.Class);
        MethodBuilder method = type.DefineMethod(
            "M",
            MethodAttributes.Public,
            typeof(void),
            [typeof(DateTime)]);
        ParameterBuilder parameter = method.DefineParameter(
            1,
            ParameterAttributes.Optional,
            "when\\marker");
        // ECMA-335 custom-attribute blobs: prolog, fixed arguments, then
        // the named-argument count.
        parameter.SetCustomAttribute(
            typeof(System.Runtime.InteropServices.OptionalAttribute)
                .GetConstructor(Type.EmptyTypes)!,
            [0x01, 0x00, 0x00, 0x00]);
        parameter.SetCustomAttribute(
            typeof(System.Runtime.CompilerServices.DateTimeConstantAttribute)
                .GetConstructor([typeof(long)])!,
            [
                0x01, 0x00,
                0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00,
            ]);
        method.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"StructuredMetadataDefault-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitHostileIndexer()
    {
        const char Hazard = '\u202E';
        var assemblyName = new AssemblyName("HostileIndexer");
        var assembly = new PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "Probe",
            TypeAttributes.Public | TypeAttributes.Class);
        MethodBuilder getter = type.DefineMethod(
            "get_Item",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            typeof(string),
            [typeof(DateTime)]);
        ParameterBuilder parameter = getter.DefineParameter(
            1,
            ParameterAttributes.Optional,
            $"idx{Hazard}evil");
        parameter.SetCustomAttribute(
            typeof(System.Runtime.InteropServices.OptionalAttribute)
                .GetConstructor(Type.EmptyTypes)!,
            [0x01, 0x00, 0x00, 0x00]);
        parameter.SetCustomAttribute(
            typeof(System.Runtime.CompilerServices.DateTimeConstantAttribute)
                .GetConstructor([typeof(long)])!,
            [
                0x01, 0x00,
                0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00,
            ]);
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        PropertyBuilder property = type.DefineProperty(
            "Item",
            PropertyAttributes.None,
            typeof(string),
            [typeof(DateTime)]);
        property.SetGetMethod(getter);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"HostileIndexer-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitExplicitInterfaceProperty()
    {
        var assemblyName = new AssemblyName("ExplicitInterfaceProperty");
        var assembly = new PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder interfaceType = module.DefineType(
            "I",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        MethodBuilder interfaceGetter = interfaceType.DefineMethod(
            "get_P",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.SpecialName,
            typeof(string),
            Type.EmptyTypes);
        PropertyBuilder interfaceProperty = interfaceType.DefineProperty(
            "P",
            PropertyAttributes.None,
            typeof(string),
            Type.EmptyTypes);
        interfaceProperty.SetGetMethod(interfaceGetter);
        Type createdInterface = interfaceType.CreateType()!;

        TypeBuilder type = module.DefineType(
            "Probe",
            TypeAttributes.Public | TypeAttributes.Class);
        type.AddInterfaceImplementation(createdInterface);
        MethodBuilder getter = type.DefineMethod(
            "I.get_P",
            MethodAttributes.Private
                | MethodAttributes.Final
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName,
            typeof(string),
            Type.EmptyTypes);
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(
            getter,
            createdInterface.GetMethod("get_P")!);
        PropertyBuilder property = type.DefineProperty(
            "I.P",
            PropertyAttributes.None,
            typeof(string),
            Type.EmptyTypes);
        property.SetGetMethod(getter);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"ExplicitInterfaceProperty-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    /// <summary>
    /// The harness owns this predicate rather than calling
    /// <see cref="CSharpIdentifier.IsRenderingHazard"/>: a gate that asks the
    /// product what counts as dangerous cannot fail when the product's answer is
    /// wrong, which is the case it exists to catch.
    /// </summary>
    static void AssertContained(string signature)
    {
        // A signature is a single line, so unlike rendered Markdown it may not
        // carry CR or LF either.
        Assert.DoesNotContain(
            signature,
            c => c is '\n' or '\r' || HostileOutputAssert.IsForbidden(c));
    }
}

/// <summary>
/// A whole-view gate for issue #3319. The per-channel gates only cover channels
/// someone thought to name, and three separate adversarial passes each found a
/// channel the previous fix had missed. This walks the built view object graph
/// reflectively instead, so a row type nobody has thought of yet is still
/// covered the day it is added.
/// </summary>
public class UntrustedViewContainmentTests
{
    [Fact]
    public void NoBuiltViewCarriesARenderingHazard()
    {
        const string Hazard = "\v";
        var type = new ApiType
        {
            Name = $"Holder{Hazard}INJECTED",
            Namespace = $"Ns{Hazard}INJECTED",
            Kind = "class",
            BaseType = $"Base{Hazard}INJECTED",
            Interfaces = [$"IFace{Hazard}INJECTED"],
            TypeParameters = [new TypeParameter { Name = $"T{Hazard}INJECTED" }],
            Members =
            [
                new ApiMember { Name = $"Fld{Hazard}INJECTED", Kind = "field", ReturnType = $"Ret{Hazard}INJECTED" },
                new ApiMember { Name = $"Prop{Hazard}INJECTED", Kind = "property", ReturnType = "int" },
                new ApiMember { Name = $"Evt{Hazard}INJECTED", Kind = "event", ReturnType = "EventHandler" },
                new ApiMember
                {
                    Name = $"Meth{Hazard}INJECTED",
                    Kind = "method",
                    ReturnType = $"Ret{Hazard}INJECTED",
                    Signature = $"Ret{Hazard}INJECTED Meth{Hazard}INJECTED(Arg{Hazard}INJECTED a)",
                    // Adversarial review showed the walk passed vacuously for these
                    // three: attribute text, the [Obsolete] message, and a parameter
                    // default value are all attacker-controlled and were unseeded.
                    Attributes = [$"Attr{Hazard}INJECTED"],
                    ObsoleteMessage = $"Obs{Hazard}INJECTED",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = $"Ret{Hazard}INJECTED",
                        Parameters =
                        [
                            new ApiParameter
                            {
                                Name = "a",
                                Type = $"Arg{Hazard}INJECTED",
                                HasDefault = true,
                                DefaultValueText = $"\"Def{Hazard}INJECTED\"",
                                Attributes = [$"PAttr{Hazard}INJECTED"],
                            },
                        ],
                    },
                },
                new ApiMember { Name = ".ctor", Kind = "constructor" },
            ]
        };

        var enumType = new ApiType
        {
            Name = "HostileEnum",
            Kind = "enum",
            Members =
            [
                new ApiMember { Name = $"Val{Hazard}INJECTED", Kind = "field", ReturnType = "int", EnumValue = 1 },
            ]
        };

        var summaryView = new TypeView();
        // The method-group and event views must be walked too. They were
        // constructed inline here and thrown away, so the event summary row --
        // which was the one member kind still passing its name through raw --
        // was never inspected and the gate passed anyway (found by adversarial
        // review).
        var methodGroupsView = new MethodGroupsView();
        var eventsView = new EventsView();
        ApiOutputFormatter.PopulateMemberSummarySections(
            summaryView, methodGroupsView, eventsView, type, new ApiOptions());

        var enumView = new TypeView();
        ApiOutputFormatter.PopulateEnumValues(enumView, enumType, new ApiOptions());

        var shapeView = ApiOutputFormatter.BuildShapeView(
            type, foundIn: null, packageName: null, packageVersion: null, memberFilter: []);

        var (tableView, _) = ApiOutputFormatter.BuildTypeTableView(type, new ApiOptions());

        // Non-vacuity: the hostile names must actually be present in the views,
        // or a view that dropped every member would pass trivially.
        int seen = 0;
        foreach (var view in new object[]
                 { summaryView, methodGroupsView, eventsView, enumView, shapeView, tableView })
        {
            foreach (string text in Strings(view, new HashSet<object>(ReferenceEqualityComparer.Instance)))
            {
                if (text.Contains("INJECTED", StringComparison.Ordinal))
                    seen++;
                Assert.DoesNotContain(text, IsHazard);
            }
        }

        Assert.True(seen >= 5, $"expected the hostile names to reach the views, saw {seen}");
    }

    /// <summary>
    /// The harness spells the hazard set out rather than calling
    /// <see cref="CSharpIdentifier.IsRenderingHazard"/>, so that a wrong answer
    /// from the product cannot make this gate agree with it.
    /// </summary>
    static bool IsHazard(char c) => c != '\n' && c != '\r' && HostileOutputAssert.IsForbidden(c);

    /// <summary>Every string reachable from a built view, however nested.</summary>
    static IEnumerable<string> Strings(object? node, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node))
            yield break;

        if (node is string s)
        {
            yield return s;
            yield break;
        }

        if (node is System.Collections.IEnumerable list)
        {
            foreach (var item in list)
                foreach (string text in Strings(item, seen))
                    yield return text;
            yield break;
        }

        var type = node.GetType();

        // KeyValuePair, ValueTuple, and Tuple live under System, so a dictionary's
        // entries or a tuple's items would otherwise be skipped by the namespace
        // bail below. Walk their public members explicitly.
        bool isTupleLike = type.IsGenericType
            && type.GetGenericTypeDefinition() is { } definition
            && (definition == typeof(KeyValuePair<,>)
                || definition.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true
                || definition.FullName?.StartsWith("System.Tuple`", StringComparison.Ordinal) == true);

        if (isTupleLike)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object? entry;
                try { entry = property.GetValue(node); }
                catch { continue; }
                foreach (string text in Strings(entry, seen))
                    yield return text;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object? entry;
                try { entry = field.GetValue(node); }
                catch { continue; }
                foreach (string text in Strings(entry, seen))
                    yield return text;
            }

            yield break;
        }

        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            yield break;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;
            object? value;
            try { value = property.GetValue(node); }
            catch { continue; }
            foreach (string text in Strings(value, seen))
                yield return text;
        }

        // Public fields are rendered too, and are not reached by the property walk.
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value;
            try { value = field.GetValue(node); }
            catch { continue; }
            foreach (string text in Strings(value, seen))
                yield return text;
        }
    }
}

/// <summary>
/// Gate for the diff channel (issue #3319). `ApiChange` messages embed untrusted
/// type and member names, and the diff renderer prints them into Markdown
/// headings, bullet lists, and table cells.
/// </summary>
public class UntrustedDiffContainmentTests
{
    [Theory]
    [InlineData("\v")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\u001b[31m")]
    [InlineData("\u202e")]
    public void ApiChangeText_IsContained(string hazard)
    {
        var change = new ApiChange(
            ChangeKind.MemberAdded,
            ChangeClassification.Additive,
            $"Member 'M{hazard}INJECTED' was added",
            OldValue: $"void M{hazard}INJECTED()",
            NewValue: $"int M{hazard}INJECTED()");

        // Non-vacuity: the name must still be there, contained rather than dropped.
        Assert.Contains("INJECTED", change.Message, StringComparison.Ordinal);

        foreach (string text in new[] { change.Message, change.OldValue!, change.NewValue! })
            Assert.DoesNotContain(text, c => c is '\n' or '\r' || HostileOutputAssert.IsForbidden(c));
    }
}
