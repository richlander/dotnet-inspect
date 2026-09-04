using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the per-member whole render (<see cref="MemberBodyProducer.ProduceMember"/>)
/// against the whole-type listing (<see cref="MemberBodyProducer.Project"/>): the
/// text produced for a single member is byte-identical to that member's segment
/// in the listing — one composition, no drift. This is the #2996 output-contract
/// enabler: CSharp-owned signature + decompiler-owned body, per member.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class MemberBodyProducerMemberRenderTests
{
    static string AssemblyPath => typeof(MemberBodyProducerMemberRenderTests).Assembly.Location;

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(MemberRenderSpecimen).FullName);
    }

    [Fact]
    public void ProduceMember_ByteIdenticalToWholeTypeSegment_ForEveryMember()
    {
        var type = Specimen();
        var listing = MemberBodyProducer.Project(type, AssemblyPath, pdbPath: null).Output;
        Assert.NotNull(listing);

        Assert.NotEmpty(type.Members);
        foreach (var member in type.Members)
        {
            var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

            Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
            Assert.True(rendered.IsComplete);
            Assert.NotNull(rendered.Text);
            // The per-member text is exactly the member's segment in the
            // whole-type listing — no separate signature implementation.
            Assert.Contains(rendered.Text!, listing);
        }
    }

    [Fact]
    public void ProduceMembers_BatchIsByteIdenticalToPerMember_ForEveryMember()
    {
        var type = Specimen();
        var batch = MemberBodyProducer.ProduceMembers(type, AssemblyPath, pdbPath: null);

        Assert.NotEmpty(type.Members);
        foreach (var member in type.Members)
        {
            var single = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);
            Assert.True(batch.TryGetValue(member, out var batched),
                $"batch render missing member {member.Name}");

            // The batch entry is byte-identical to the per-member render — same
            // status, text, and imports. The batch only amortizes the assembly
            // open and type-map build; it must not change any member's output.
            Assert.Equal(single.Status, batched.Status);
            Assert.Equal(single.Text, batched.Text);
            Assert.Equal(single.Namespaces, batched.Namespaces);
        }
    }

    [Fact]
    public void ProduceMember_PreservesAccessorSpecificAccessibility()
    {
        var type = Specimen();
        var property = Assert.Single(type.Members, member => member.Name == "Name");

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            property,
            AssemblyPath,
            pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains("private set", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_ExplicitInterfaceSetterUsesPropertyValueType()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe, includeAll: true).Types,
            candidate =>
                candidate.FullName == typeof(MemberRenderSpecimen).FullName);
        var property = Assert.Single(
            type.Members,
            member => member.Kind == "property"
                && !member.IsStatic
                && member.Name.EndsWith(
                    $".{nameof(IMemberRenderExplicitProperty.Label)}",
                    StringComparison.Ordinal));
        ApiMember setter = Assert.Single(
            ApiMemberAccessors.Create(property, type),
            member => member.Name.EndsWith(
                $".set_{nameof(IMemberRenderExplicitProperty.Label)}",
                StringComparison.Ordinal));
        type.Members = [setter];

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            setter,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains(
            "string? IMemberRenderExplicitProperty.Label",
            rendered.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "set => _explicitLabel = value;",
            rendered.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "void IMemberRenderExplicitProperty.Label",
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("get_")]
    [InlineData("set_")]
    public void ProduceMember_StaticExplicitInterfaceAccessorPreservesStaticModifier(
        string accessorPrefix)
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe, includeAll: true).Types,
            candidate =>
                candidate.FullName == typeof(MemberRenderSpecimen).FullName);
        var property = Assert.Single(
            type.Members,
            member => member.Kind == "property"
                && member.IsStatic
                && member.Name.EndsWith(
                    "."
                        + nameof(
                            IMemberRenderStaticExplicitProperty<
                                MemberRenderSpecimen>.Label),
                    StringComparison.Ordinal));
        ApiMember accessor = Assert.Single(
            ApiMemberAccessors.Create(property, type),
            member => member.Name.Contains(
                $".{accessorPrefix}"
                    + nameof(
                        IMemberRenderStaticExplicitProperty<
                            MemberRenderSpecimen>.Label),
                StringComparison.Ordinal));
        type.Members = [accessor];

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            accessor,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains(
            "static string? IMemberRenderStaticExplicitProperty<MemberRenderSpecimen>.Label",
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("get_Prefix", "int IMemberRenderExplicitPrefixMethods.get_Prefix()")]
    [InlineData("set_Prefix", "void IMemberRenderExplicitPrefixMethods.set_Prefix()")]
    public void ProduceMember_ExplicitGetSetPrefixedMethodRetainsMethodForm(
        string methodName,
        string expectedSignature)
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe, includeAll: true).Types,
            candidate =>
                candidate.FullName == typeof(MemberRenderSpecimen).FullName);
        var member = Assert.Single(
            type.Members,
            candidate =>
                candidate.Kind == "explicit-interface-implementation"
                && candidate.Name.EndsWith(
                    $".{methodName}",
                    StringComparison.Ordinal));

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            member,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains(
            expectedSignature,
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_RendersCustomEventAccessorBodiesAndReturnAttributes()
    {
        var type = Specimen();
        var eventMember = Assert.Single(type.Members, member => member.Name == "Changed");
        var accessors = Assert.IsType<ApiSignature>(eventMember.SignatureModel).Accessors;
        Assert.Equal(["add", "remove"], accessors.Select(accessor => accessor.Kind));
        Assert.All(
            accessors,
            accessor => Assert.Equal(
                ["ILInspector.Decompiler.Tests.SetterMarker"],
                accessor.ReturnAttributes));

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            eventMember,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains("public event EventHandler? Changed", rendered.Text, StringComparison.Ordinal);
        Assert.Contains(
            "[return: ILInspector.Decompiler.Tests.SetterMarker] add",
            rendered.Text,
            StringComparison.Ordinal);
        Assert.Contains("Delegate.Combine(_changed, value)", rendered.Text, StringComparison.Ordinal);
        Assert.Contains(
            "[return: ILInspector.Decompiler.Tests.SetterMarker] remove",
            rendered.Text,
            StringComparison.Ordinal);
        Assert.Contains("Delegate.Remove(_changed, value)", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_PreservesFieldLikeEventDeclaration()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe).Types,
            candidate => candidate.FullName == typeof(FieldLikeEventSpecimen).FullName);
        var eventMember = Assert.Single(type.Members, member => member.Name == "Changed");

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            eventMember,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains("public event EventHandler Changed;", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(" add", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(" remove", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_RendersStaticAutoPropertyWithoutRecursiveBodies()
    {
        var type = Specimen();
        var property = Assert.Single(type.Members, member => member.Name == "StaticName");

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            property,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains(
            "public static string StaticName { get; set; }",
            rendered.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("return StaticName", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_DoesNotFoldUnrelatedCompilerGeneratedBackingField()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"member-render-false-auto-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var assemblyName = new AssemblyName("FalseAutoProperty");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var module = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = module.DefineType(
                "FalseAuto",
                TypeAttributes.Public | TypeAttributes.Class);
            var compilerGenerated = new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
                    .GetConstructor(Type.EmptyTypes)!,
                []);
            var backingField = typeBuilder.DefineField(
                "<Value>k__BackingField",
                typeof(int),
                FieldAttributes.Private);
            backingField.SetCustomAttribute(compilerGenerated);
            var getter = typeBuilder.DefineMethod(
                "get_Value",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                typeof(int),
                Type.EmptyTypes);
            getter.SetCustomAttribute(compilerGenerated);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 42);
            il.Emit(OpCodes.Ret);
            typeBuilder
                .DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
                .SetGetMethod(getter);
            typeBuilder.CreateType();

            string assemblyPath = Path.Combine(directory, "FalseAutoProperty.dll");
            assemblyBuilder.Save(assemblyPath);
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.FullName == "FalseAuto");
            var property = Assert.Single(type.Members, member => member.Name == "Value");

            var rendered = MemberBodyProducer.ProduceMember(
                type,
                property,
                assemblyPath,
                pdbPath: null);

            Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
            Assert.Contains("public static int Value => 42;", rendered.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("{ get; }", rendered.Text, StringComparison.Ordinal);

            var fidelity = Assert.Single(
                FidelityCheck.Evaluate(
                    assemblyPath,
                    typeName => typeName == "FalseAuto",
                    method => method.Method == "get_Value"));
            Assert.True(fidelity.UsedProductWholeMember);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, fidelity.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProduceMember_DeclinesClassicAsyncAccessorBody()
    {
        var type = new ApiType
        {
            Name = nameof(ClassicAsyncAccessorSpecimen),
            Namespace = typeof(ClassicAsyncAccessorSpecimen).Namespace,
            Kind = "class"
        };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            Accessibility = "public",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Threading.Tasks.Task<int>",
                MemberName = "Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        type.Members.Add(property);

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            property,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Failed, rendered.Status);
        Assert.Contains(
            "C# properties cannot carry an async accessor modifier.",
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_DeclinesClassicAsyncEventAccessorBody()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe).Types,
            candidate => candidate.FullName == typeof(ClassicAsyncEventAccessorSpecimen).FullName);
        var reader = pe.GetMetadataReader();
        var typeDefinition = reader.GetTypeDefinition(Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                == nameof(ClassicAsyncEventAccessorSpecimen)));
        var adder = Assert.Single(
            typeDefinition.GetMethods(),
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "add_Changed");
        var remover = Assert.Single(
            typeDefinition.GetMethods(),
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "remove_Changed");
        var eventMember = new ApiMember
        {
            Name = "Changed",
            Kind = "event",
            Accessibility = "public",
            AdderToken = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(adder),
            RemoverToken = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(remover),
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Changed",
                Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" }
                ]
            }
        };
        type.Members.Add(eventMember);

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            eventMember,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Failed, rendered.Status);
        Assert.Contains(
            "C# events cannot carry an async accessor modifier.",
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMember_PreservesSetterReturnAttributes()
    {
        var type = Specimen();
        var property = Assert.Single(
            type.Members,
            member => member.Name == "AttributedValue");
        var accessors = Assert.IsType<ApiSignature>(property.SignatureModel).Accessors;
        Assert.All(
            accessors,
            accessor => Assert.Equal(
                ["ILInspector.Decompiler.Tests.SetterMarker"],
                accessor.ReturnAttributes));

        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var reader = pe.GetMetadataReader();
        var typeDefinition = reader.GetTypeDefinition(Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name)
                == nameof(MemberRenderSpecimen)));
        var propertyDefinition = reader.GetPropertyDefinition(Assert.Single(
            typeDefinition.GetProperties(),
            handle => reader.GetString(reader.GetPropertyDefinition(handle).Name)
                == "AttributedValue"));
        var declaration = MetadataDeclarationQuery.GetProperty(
            reader,
            typeDefinition,
            propertyDefinition);
        Assert.All(
            declaration.Signature.Accessors,
            accessor => Assert.Equal(
                ["ILInspector.Decompiler.Tests.SetterMarker"],
                accessor.ReturnAttributes));

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            property,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains(
            "[return: ILInspector.Decompiler.Tests.SetterMarker] get",
            rendered.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: ILInspector.Decompiler.Tests.SetterMarker] set",
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProduceMembers_DelegatesMapEveryMemberToAbsent()
    {
        var constructor = new ApiMember { Name = ".ctor", Kind = "constructor" };
        var invoke = new ApiMember { Name = "Invoke", Kind = "method" };
        var type = new ApiType
        {
            Name = "Callback",
            Kind = "delegate",
            Members = [constructor, invoke]
        };

        var batch = MemberBodyProducer.ProduceMembers(
            type,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(type.Members.Count, batch.Count);
        foreach (var member in type.Members)
        {
            var single = MemberBodyProducer.ProduceMember(
                type,
                member,
                AssemblyPath,
                pdbPath: null);
            var batched = batch[member];
            Assert.Equal(single.Status, batched.Status);
            Assert.Equal(single.Text, batched.Text);
            Assert.Equal(single.Namespaces, batched.Namespaces);
            Assert.Equal(MemberBodyProductionStatus.Absent, batched.Status);
        }
    }

    [Fact]
    public void ProduceMembers_UnresolvedTypeMapsEveryMemberToAbsent()
    {
        var constructor = new ApiMember { Name = ".ctor", Kind = "constructor" };
        var method = new ApiMember { Name = "M", Kind = "method" };
        var type = new ApiType
        {
            Name = "Missing",
            Namespace = "Canary",
            Kind = "class",
            Members = [constructor, method]
        };

        var batch = MemberBodyProducer.ProduceMembers(
            type,
            AssemblyPath,
            pdbPath: null);

        Assert.Equal(type.Members.Count, batch.Count);
        foreach (var member in type.Members)
        {
            var single = MemberBodyProducer.ProduceMember(
                type,
                member,
                AssemblyPath,
                pdbPath: null);
            var batched = batch[member];
            Assert.Equal(single.Status, batched.Status);
            Assert.Equal(single.Text, batched.Text);
            Assert.Equal(single.Namespaces, batched.Namespaces);
            Assert.Equal(MemberBodyProductionStatus.Absent, batched.Status);
        }
    }

    [Fact]
    public void ProduceMembers_SharedSetupFailureMapsEveryMemberToFailed()
    {
        string assemblyPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(assemblyPath, "not a PE image");
            var constructor = new ApiMember { Name = ".ctor", Kind = "constructor" };
            var method = new ApiMember { Name = "M", Kind = "method" };
            var type = new ApiType
            {
                Name = "Missing",
                Namespace = "Canary",
                Kind = "class",
                Members = [constructor, method]
            };

            var batch = MemberBodyProducer.ProduceMembers(
                type,
                assemblyPath,
                pdbPath: null);

            Assert.Equal(type.Members.Count, batch.Count);
            foreach (var member in type.Members)
            {
                var single = MemberBodyProducer.ProduceMember(
                    type,
                    member,
                    assemblyPath,
                    pdbPath: null);
                var batched = batch[member];
                Assert.Equal(single.Status, batched.Status);
                Assert.Equal(single.Text, batched.Text);
                Assert.Equal(single.Namespaces, batched.Namespaces);
                Assert.Equal(MemberBodyProductionStatus.Failed, batched.Status);
            }
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public void ProduceMember_CanOmitCustomAttributesAndTheirImports()
    {
        var type = Specimen();
        var constructor = Assert.Single(type.Members, member => member.Kind == "constructor");
        Assert.Contains(
            constructor.SignatureModel!.Parameters[0].Attributes,
            attribute => attribute.Contains("ParameterMarker", StringComparison.Ordinal));

        var withAttributes = MemberBodyProducer.ProduceMember(
            type,
            constructor,
            AssemblyPath,
            pdbPath: null);
        var withoutAttributes = MemberBodyProducer.ProduceMember(
            type,
            constructor,
            AssemblyPath,
            pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);
        var batch = MemberBodyProducer.ProduceMembers(
            type,
            AssemblyPath,
            pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);

        Assert.Contains("[Description(\"marker\")]", withAttributes.Text);
        Assert.Contains("ParameterMarker", withAttributes.Text);
        Assert.Contains("[SkipLocalsInit]", withAttributes.Text);
        Assert.DoesNotContain("[Description(\"marker\")]", withoutAttributes.Text);
        Assert.DoesNotContain("ParameterMarker", withoutAttributes.Text);
        Assert.Contains(
            "[global::System.Runtime.CompilerServices.SkipLocalsInit]",
            withoutAttributes.Text);
        Assert.Contains("System.ComponentModel", withAttributes.Namespaces);
        Assert.DoesNotContain("System.ComponentModel", withoutAttributes.Namespaces);
        Assert.Equal(withoutAttributes.Status, batch[constructor].Status);
        Assert.Equal(withoutAttributes.Text, batch[constructor].Text);
        Assert.Equal(withoutAttributes.Namespaces, batch[constructor].Namespaces);
    }

    [Fact]
    public void CompilationRequiredMode_DeclinesUnstructuredCompatibilitySignature()
    {
        var type = Specimen();
        var constructor = Assert.Single(type.Members, member => member.Kind == "constructor");
        constructor.Signature =
            "void .ctor([System.Runtime.InteropServices.Optional, "
            + "System.Runtime.CompilerServices.DateTimeConstant(42L)] "
            + "System.DateTime when)";
        constructor.SignatureModel = new ApiSignature
        {
            MemberName = ".ctor",
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Type = "System.DateTime",
                    Name = "when",
                    HasDefault = true,
                    Attributes =
                    [
                        "System.Runtime.InteropServices.Optional",
                        "System.Runtime.CompilerServices.DateTimeConstant(42L)"
                    ]
                }
            ]
        };

        var single = MemberBodyProducer.ProduceMember(
            type,
            constructor,
            AssemblyPath,
            pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);
        var batch = MemberBodyProducer.ProduceMembers(
            type,
            AssemblyPath,
            pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);

        Assert.Equal(MemberBodyProductionStatus.Failed, single.Status);
        Assert.DoesNotContain("DateTimeConstant", single.Text);
        Assert.Equal(type.Members.Count, batch.Count);
        Assert.Equal(single, batch[constructor]);
    }

    [Fact]
    public void ProduceMember_RendersExpressionBodiedArrow()
    {
        var type = Specimen();
        var increment = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.Increment));

        var rendered = MemberBodyProducer.ProduceMember(type, increment, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // Whole member: CSharp-owned signature + decompiler body, arrow layout.
        Assert.Contains("int Increment(", rendered.Text);
        Assert.Contains("=> ", rendered.Text);
    }

    [Fact]
    public void ProduceMember_WrapsExpressionBodiedArrow_WhenRequested()
    {
        var type = Specimen();
        var increment = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.Increment));

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            increment,
            AssemblyPath,
            pdbPath: null,
            printerOptions: new PrinterOptions { WrapExpressionBodyArrow = true });

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Equal("    public int Increment(int n)\n        => n + 1;", rendered.Text!.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProduceMember_RendersThrowStubAsExpressionBody()
    {
        var type = Specimen();
        var stub = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.ThrowStub));

        var rendered = MemberBodyProducer.ProduceMember(type, stub, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // The canonical #2996 case: a throwing stub is one expression-bodied
        // member spelled by the shared CSharp layout, not a block.
        Assert.Contains("=> throw", rendered.Text);
    }

    [Fact]
    public void ProduceMember_RendersBlockBodiedMethod()
    {
        var type = Specimen();
        var log = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.Log));

        var rendered = MemberBodyProducer.ProduceMember(type, log, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains("void Log(", rendered.Text);
        // Two side-effecting statements cannot fold to an expression body.
        Assert.Contains("{", rendered.Text);
        Assert.DoesNotContain("=>", rendered.Text);
    }

    [Fact]
    public void ProduceMember_PreservesQualifiedNameInsideStringLiteral()
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.QuotedTypeName));

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // Name-shortening must never reach inside a string literal. An escaped
        // quote must not flip in-literal parity and shorten System.String to
        // String, which would corrupt the ldstr operand and induce a false
        // compile-back OperandDiff (#3062).
        Assert.Contains("System.String", rendered.Text);
    }

    [Fact]
    public void ProduceMember_PreservesQualifiedNameInsideInterpolationHoleLiteral()
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.InterpolatedQuotedTypeName));

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // The body re-sugars to an interpolated string; guard that the shape is
        // actually recovered so the hole scan is exercised.
        Assert.Contains("$\"", rendered.Text);
        // Name-shortening scans a hole's code but must copy nested literals
        // verbatim: System.String inside the hole's "System.String" constant
        // must survive, not be mis-segmented and shortened to String (#3064).
        Assert.Contains("\"System.String\"", rendered.Text);
    }

    [Theory]
    [InlineData(nameof(MemberRenderSpecimen.AliasQualifiedShadow))]
    [InlineData(nameof(MemberRenderSpecimen.AliasQualifiedShadowInHole))]
    public void ProduceMember_PreservesAliasQualifiedNameUnderShadowing(string memberName)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // The printer emits global::System.Math to escape the shadowing Math
        // parameter. Shortening must keep the full alias-qualified path, not
        // strip it to the invalid global::Math (CS0400) — in a hole or not.
        Assert.Contains("global::System.Math", rendered.Text);
        Assert.DoesNotContain("global::Math", rendered.Text);
    }

    [Theory]
    [InlineData(nameof(MemberRenderSpecimen.EscapedAliasQualifiedShadow),
        "global::@event.Models.TypeNameShadow", "global::@TypeNameShadow")]
    [InlineData(nameof(MemberRenderSpecimen.SystemEscapedAliasQualifiedShadow),
        "global::System.@event.Models.SystemNameShadow", "global::System.@SystemNameShadow")]
    public void ProduceMember_PreservesAliasQualifiedNameWithEscapedKeywordNamespace(
        string memberName, string expectedFullPath, string corruptedForm)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // A namespace segment that is a keyword is printed @-escaped, so an '@'
        // sits between the '::' alias qualifier and the matched metadata
        // namespace. For a System-rooted namespace the System.-stripped prefix
        // even matches mid-chain (after "System.@"), so the guard must walk the
        // whole qualified run back to its '::' root and decline — not strip to
        // the invalid @-escaped form (a stray escape on a name that does not
        // bind, CS0400/CS0234) (#3064 review).
        Assert.Contains(expectedFullPath, rendered.Text);
        Assert.DoesNotContain(corruptedForm, rendered.Text);
    }

    [Fact]
    public void Project_EscapesKeywordSegmentsInHoistedUsings()
    {
        var type = Specimen();
        var listing = MemberBodyProducer.Project(type, AssemblyPath, pdbPath: null).Output;
        Assert.NotNull(listing);

        // A non-shadowed reference to a type in a keyword-segment namespace is
        // shortened to the simple name, harvesting the namespace into a hoisted
        // using. Metadata namespaces carry no escape, so a keyword segment must
        // be @-escaped in the emitted directive or it is invalid C# (#3090).
        Assert.Contains("using System.@event.Models;", listing);
        Assert.Contains("using @event.Models;", listing);
        Assert.DoesNotContain("using System.event.Models;", listing);
        Assert.DoesNotContain("using event.Models;", listing);
    }
}

#pragma warning disable CA1822 // members are instance to exercise real signatures
public interface IMemberRenderExplicitProperty
{
    string? Label { get; set; }
}

public interface IMemberRenderStaticExplicitProperty<TSelf>
    where TSelf : IMemberRenderStaticExplicitProperty<TSelf>
{
    static abstract string? Label { get; set; }
}

public interface IMemberRenderExplicitPrefixMethods
{
    int get_Prefix();

    void set_Prefix();
}

public sealed class MemberRenderSpecimen :
    IMemberRenderExplicitProperty,
    IMemberRenderStaticExplicitProperty<MemberRenderSpecimen>,
    IMemberRenderExplicitPrefixMethods
{
    EventHandler? _changed;
    string? _explicitLabel;
    static string? _staticExplicitLabel;

    [System.ComponentModel.Description("marker")]
    [System.Runtime.CompilerServices.SkipLocalsInit]
    public MemberRenderSpecimen(
        [ParameterMarker] int seed)
        => Value = seed;

    public int Value { get; }

    public string Name { get; private set; } = "";

    public static string StaticName { get; set; } = "";

    string? IMemberRenderExplicitProperty.Label
    {
        get => _explicitLabel;
        set => _explicitLabel = value;
    }

    static string? IMemberRenderStaticExplicitProperty<MemberRenderSpecimen>.Label
    {
        get => _staticExplicitLabel;
        set => _staticExplicitLabel = value;
    }

    int IMemberRenderExplicitPrefixMethods.get_Prefix() => 1;

    void IMemberRenderExplicitPrefixMethods.set_Prefix()
    {
    }

    public int AttributedValue
    {
        [return: SetterMarker]
        get;

        [return: SetterMarker]
        set;
    }

    public event EventHandler? Changed
    {
        [return: SetterMarker]
        add => _changed += value;

        [return: SetterMarker]
        remove => _changed -= value;
    }

    public int Increment(int n) => n + 1;

    public void Log(int n)
    {
        Console.WriteLine(n);
        Console.WriteLine(n + 1);
    }

    public void ThrowStub() => throw new NotImplementedException();

    // A string constant whose value contains a double-quote followed by a
    // fully-qualified type name. The rendered literal escapes the quote (\"),
    // so a name-shortener that splits on '"' without honoring escapes flips its
    // in-literal parity and mutates System.String inside the constant (#3062).
    public string QuotedTypeName() => "a \"System.String\" b";

    static string Echo(string value) => value;

    // An interpolated string whose hole contains a nested string literal that
    // is itself a fully-qualified type name. The decompiler re-sugars this to
    // $"…{Echo("System.String")}…", so a name-shortener that treats the outer
    // $"…" as one literal mis-segments the hole and shortens System.String
    // inside the nested constant, corrupting the ldstr operand (#3064).
    public string InterpolatedQuotedTypeName(int n) => $"n={n} t={Echo("System.String")}";

    // A parameter named Math shadows System.Math, so the printer emits the
    // alias-qualified global::System.Math to disambiguate. Shortening must not
    // strip it to global::Math, which re-introduces the collision and does not
    // bind (CS0400) — both inside an interpolation hole and in plain code (#3064).
    public static int AliasQualifiedShadow(int Math) => System.Math.Abs(Math) + Math;

    public static string AliasQualifiedShadowInHole(int Math) => $"v={System.Math.Abs(Math) + Math}";

    // The referenced type lives in @event.Models, a namespace whose first
    // segment is a keyword, so the printer escapes it. A parameter named
    // TypeNameShadow shadows the type, forcing the alias-qualified
    // global::@event.Models.TypeNameShadow. Shortening must keep the full path,
    // not strip it to the invalid global::@TypeNameShadow: the '@' sits between
    // the '::' alias qualifier and the raw metadata namespace, so the guard has
    // to skip the escape (#3064 review).
    public static int EscapedAliasQualifiedShadow(int TypeNameShadow)
        => @event.Models.TypeNameShadow.M(TypeNameShadow);

    // Same hazard, but the namespace is System-rooted with a keyword segment
    // (System.@event.Models). The printer emits global::System.@event.Models.
    // TypeNameShadow; the System.-stripped prefix "event.Models" matches
    // mid-chain after "System.@", so a guard that only inspects the characters
    // just before the match cannot see the '::' root. Shortening must still be
    // declined, not corrupted to global::System.@TypeNameShadow (CS0234).
    public static int SystemEscapedAliasQualifiedShadow(int SystemNameShadow)
        => System.@event.Models.SystemNameShadow.M(SystemNameShadow);

    // A non-shadowed reference to a type in the keyword-segment namespace
    // System.@event.Models: the printer emits it plain (no global::) and the
    // shortener shortens it to the simple name, harvesting the namespace into a
    // hoisted using. The metadata namespace carries no escape, so the emitted
    // directive must be @-escaped (using System.@event.Models;) or it is invalid
    // C# (#3090).
    public static int SystemEscapedPlain(int n)
        => System.@event.Models.SystemNameShadow.M(n);

    // Same, for a top-level keyword-segment namespace @event.Models — the
    // hoisted directive must be @-escaped (using @event.Models;) (#3090).
    public static int EscapedPlain(int n)
        => @event.Models.TypeNameShadow.M(n);
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParameterMarkerAttribute : Attribute;

[AttributeUsage(AttributeTargets.ReturnValue)]
public sealed class SetterMarkerAttribute : Attribute;

public sealed class ClassicAsyncAccessorSpecimen
{
    public async Task<int> get_Value()
    {
        await Task.Yield();
        return 42;
    }
}

public sealed class ClassicAsyncEventAccessorSpecimen
{
    public async Task add_Changed(EventHandler value)
    {
        await Task.Yield();
    }

    public void remove_Changed(EventHandler value)
    {
    }
}

#pragma warning disable CS0067 // metadata fixture for field-like event reconstruction
public sealed class FieldLikeEventSpecimen
{
    public event EventHandler? Changed;
}
#pragma warning restore CS0067

#pragma warning restore CA1822
