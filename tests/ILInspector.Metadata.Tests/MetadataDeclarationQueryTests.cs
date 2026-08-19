using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataDeclarationQueryTests
{
    static readonly PEReader PeReader;
    static readonly MetadataReader Reader;

    static MetadataDeclarationQueryTests()
    {
        PeReader = new PEReader(File.OpenRead(typeof(MetadataDeclarationQueryTests).Assembly.Location));
        Reader = PeReader.GetMetadataReader();
    }

    [Fact]
    public void MethodDeclaration_ExposesAccessibilityModifiersParametersAndReturnAttributes()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, "ProtectedVirtual");

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        Assert.Equal("protected", declaration.Accessibility);
        Assert.True(declaration.IsPublicOrProtected);
        Assert.True(declaration.IsVirtual);
        Assert.False(declaration.IsAbstract);
        Assert.Equal("string", declaration.Signature.ReturnType);
        Assert.Equal(["System.Diagnostics.CodeAnalysis.NotNull"], declaration.Signature.ReturnAttributes);
        Assert.Equal("value", declaration.Signature.Parameters[0].Name);
        Assert.Equal("string", declaration.Signature.Parameters[0].Type);
        Assert.Equal(
            ["System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)"],
            declaration.Signature.Parameters[0].Attributes);
        Assert.True(declaration.Signature.Parameters[1].HasDefault);
        Assert.Equal("2", declaration.Signature.Parameters[1].DefaultValueText);
    }

    [Fact]
    public void MethodDeclaration_ExposesAttributedDecimalDefaults()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, nameof(MetadataDeclarationQueryFixtures.DecimalDefault));

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        var parameter = Assert.Single(declaration.Signature.Parameters);
        Assert.Equal("System.Decimal", parameter.Type);
        Assert.True(parameter.HasDefault);
        Assert.Equal("5m", parameter.DefaultValueText);
    }

    [Fact]
    public void MethodDeclaration_SynthesizesParameterWhenParamRowIsAbsent()
    {
        string path = EmitMethodWithoutParamRow();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var typeHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "MissingParamSample");
            var type = reader.GetTypeDefinition(typeHandle);
            var method = type.GetMethods()
                .Select(reader.GetMethodDefinition)
                .Single(candidate => reader.GetString(candidate.Name) == "Echo");

            Assert.Empty(method.GetParameters());

            var declaration = MetadataDeclarationQuery.GetMethod(reader, type, method);
            var parameter = Assert.Single(declaration.Signature.Parameters);

            Assert.Equal("arg0", parameter.Name);
            Assert.Equal("int", parameter.Type);
            Assert.Empty(parameter.Attributes);
            Assert.False(parameter.HasDefault);
            Assert.Null(parameter.DefaultValueText);
            Assert.Null(declaration.SignatureDecodeStatus);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PropertyDeclaration_ExposesAbstractAccessorShape()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.AbstractBase));
        var property = GetProperty(type, "Name");

        var declaration = MetadataDeclarationQuery.GetProperty(Reader, type, property);

        Assert.Equal("protected", declaration.Accessibility);
        Assert.True(declaration.IsAbstract);
        Assert.Equal("string", declaration.Signature.ReturnType);
        Assert.Equal(["get", "set"], declaration.Signature.Accessors.Select(accessor => accessor.Kind).ToArray());
    }

    [Fact]
    public void TypeSurface_IncludesNonPublicMembersWhenRequested()
    {
        var handle = GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures));

        var publicOnly = MetadataDeclarationQuery.GetTypeSurface(Reader, handle);
        var all = MetadataDeclarationQuery.GetTypeSurface(Reader, handle, includeNonPublicMembers: true);

        Assert.DoesNotContain(publicOnly.Members, member => member.Name == "ProtectedVirtual");
        var method = Assert.Single(all.Members, member => member.Name == "ProtectedVirtual");
        Assert.Equal("protected", method.Accessibility);
        Assert.True(method.IsVirtual);

        var field = Assert.Single(all.Members, member => member.Name == "_count");
        Assert.Equal("private", field.Accessibility);
        Assert.Equal("int", field.ReturnType);
    }

    [Fact]
    public void TypeSurface_RecordsExecutableBodyPresence()
    {
        var concrete = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        var abstractType = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.AbstractBase)),
            includeNonPublicMembers: true);

        Assert.True(Assert.Single(concrete.Members, member => member.Name == "Count").HasMethodBody);
        Assert.True(Assert.Single(concrete.Members, member => member.Name == "while").HasMethodBody);
        Assert.False(Assert.Single(abstractType.Members, member => member.Name == "Name").HasMethodBody);
    }

    [Fact]
    public void TypeSurface_AccessorFactsPreserveDistinctAccessibility()
    {
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        var property = Assert.Single(
            surface.Members,
            member => member.Name == nameof(MetadataDeclarationQueryFixtures.Restricted));

        Assert.Equal(
            "private",
            Assert.Single(property.AccessorFacts, accessor => accessor.Kind == "set")
                .Accessibility);
        Assert.Equal(
            "private",
            Assert.Single(property.SignatureModel!.Accessors, accessor => accessor.Kind == "set")
                .Accessibility);
    }

    [Fact]
    public void PropertyDeclarationAndTypeSurface_PreserveInitSetterStructuralReturn()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var property = GetProperty(type, nameof(MetadataDeclarationQueryFixtures.InitValue));
        var declaration = MetadataDeclarationQuery.GetProperty(Reader, type, property);
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        string structuralReturn = StructuralTypeIdentity.Modified(
            required: true,
            "System.Runtime.CompilerServices.IsExternalInit",
            "System.Void");

        Assert.Equal(
            structuralReturn,
            declaration.Signature.Accessors.Single(accessor => accessor.Kind == "set")
                .StructuralReturnType);
        Assert.Equal(
            structuralReturn,
            surface.Members.Single(member => member.Name == nameof(MetadataDeclarationQueryFixtures.InitValue))
                .AccessorFacts.Single(accessor => accessor.Kind == "set")
                .StructuralReturnType);
    }

    [Fact]
    public void TypeSurface_RecordsUnsafeBodylessSignatures()
    {
        var interfaceSurface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.IUnsafeSurface)));
        var classSurface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.UnsafeFields)));

        var method = Assert.Single(interfaceSurface.Members, member => member.Name == "Consume");

        Assert.False(method.HasMethodBody);
        Assert.True(method.IsUnsafe);
        Assert.True(Assert.Single(classSurface.Members, member => member.Name == "Pointer").IsUnsafe);
        Assert.True(Assert.Single(classSurface.Members, member => member.Name == "Value").IsUnsafe);
        var changed = Assert.Single(classSurface.Members, member => member.Name == "Changed");
        Assert.Equal("event", changed.Kind);
        Assert.True(changed.HasMethodBody);
        Assert.DoesNotContain(classSurface.Members, member => member.Name is "add_Changed" or "remove_Changed");
    }

    [Fact]
    public void TypeSurface_PreservesEventRaiserBodies()
    {
        string path = typeof(VbCustomEventFixture.CustomEvents).Assembly.Location;
        using var peReader = new PEReader(File.OpenRead(path));
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(
            reader,
            typeof(VbCustomEventFixture.CustomEvents).FullName!);

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            type => type.FullName == typeof(VbCustomEventFixture.CustomEvents).FullName);

        foreach (var surface in new[] { queried, extracted })
        {
            Assert.Single(surface.Members, member => member is { Name: "Changed", Kind: "event" });
            Assert.DoesNotContain(
                surface.Members,
                member => member.Name is "add_Changed" or "remove_Changed");
            Assert.Contains(
                surface.Members,
                member => member is
                {
                    Name: "raise_Changed",
                    Kind: "method",
                    HasMethodBody: true
                });
        }

        var typeDefinition = reader.GetTypeDefinition(typeHandle);
        var raiser = typeDefinition.GetMethods()
            .Select(reader.GetMethodDefinition)
            .Single(method => reader.GetString(method.Name) == "raise_Changed");
        Assert.NotEmpty(peReader.GetMethodBody(raiser.RelativeVirtualAddress).ExceptionRegions);
    }

    [Fact]
    public void TypeSurface_PreservesMetadataEventOtherAccessors()
    {
        string path = EmitEventWithOtherAccessor();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "EventOtherSample");

            var queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
                type => type.FullName == "EventOtherSample");

            foreach (var surface in new[] { queried, extracted })
            {
                Assert.Single(surface.Members, member => member is { Name: "Changed", Kind: "event" });
                Assert.Contains(
                    surface.Members,
                    member => member is
                    {
                        Name: "other_Changed",
                        Kind: "method",
                        HasMethodBody: true
                    });
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_PreservesRemoveOnlyEvents()
    {
        string path = EmitRemoveOnlyEvent();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "RemoveOnlyEventSample");
            var queried = MetadataDeclarationQuery.GetTypeSurface(reader, typeHandle);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader).Types,
                type => type.FullName == "RemoveOnlyEventSample");

            foreach (var surface in new[] { queried, extracted })
            {
                var evt = Assert.Single(surface.Members, member => member.Name == "Changed");
                Assert.Null(evt.AdderToken);
                Assert.NotNull(evt.RemoverToken);
                Assert.Single(evt.AccessorFacts, accessor => accessor.Kind == "remove");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_PreservesUnassociatedAccessorLikeMethods()
    {
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)),
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
            type => type.FullName == typeof(MetadataDeclarationQueryFixtures).FullName);

        foreach (var surface in new[] { queried, extracted })
            Assert.Single(surface.Members, member => member is { Name: "get_Orphan", Kind: "method" });
    }

    [Fact]
    public void TypeSurface_FoldsCompilerProducedExtensionPropertyAndIndexerImplementations()
    {
        using var stream = File.OpenRead(typeof(ExtensionPropertyIdentityFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(
            reader,
            typeof(ExtensionPropertyIdentityFixture).FullName!);
        var queried = MetadataDeclarationQuery.GetTypeSurface(reader, typeHandle);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader).Types,
            type => type.FullName == typeof(ExtensionPropertyIdentityFixture).FullName);

        using var summaryStream =
            File.OpenRead(typeof(ExtensionPropertyIdentityFixture).Assembly.Location);
        using var summaryReader = new PEReader(summaryStream);
        var summarized = Assert.Single(
            ApiSurfaceExtractor.ExtractSummary(summaryReader).Types,
            type => type.FullName == typeof(ExtensionPropertyIdentityFixture).FullName);

        foreach (var surface in new[] { queried, extracted, summarized })
        {
            Assert.Equal(
                2,
                surface.Members.Count(member => member.Kind == "method"));
            Assert.Contains(
                surface.Members,
                member => member is { Name: "get_Standalone", Kind: "method" });
            Assert.Contains(
                surface.Members,
                member => member is { Name: "set_Standalone", Kind: "method" });
            Assert.DoesNotContain(
                surface.Members,
                member => member.Kind == "method"
                    && member.Name is "get_HasValue"
                        or "get_Item"
                        or "get_StaticValue"
                        or "get_Capacity"
                        or "set_Capacity");
        }
    }

    [Fact]
    public void TypeSurface_PreservesQualifiedMethodImplAccessors()
    {
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.ExplicitSurface)),
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
            type => type.FullName.EndsWith(
                "." + nameof(MetadataDeclarationQueryFixtures.ExplicitSurface),
                StringComparison.Ordinal));

        Assert.Contains(
            queried.Members,
            member => member.Name.EndsWith(".get_Value", StringComparison.Ordinal));
        Assert.Contains(
            queried.Members,
            member => member.Name.EndsWith(".add_Changed", StringComparison.Ordinal));
        Assert.Contains(
            extracted.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(".get_Value", StringComparison.Ordinal));
        Assert.Contains(
            extracted.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(".add_Changed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeSurface_PreservesUnqualifiedMethodImplAccessors(bool isPublic)
    {
        string path = EmitUnqualifiedMethodImplAccessor(isPublic);
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "UnqualifiedAccessor");
            var queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle);
            var queriedAll = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
                type => type.FullName == "UnqualifiedAccessor");

            foreach (var surface in new[] { queried, queriedAll })
            {
                // Read is a real Property accessor with a deliberately noncanonical name.
                // Association alone must not hide it as compiler extension-property plumbing.
                Assert.Contains(
                    surface.Members,
                    member => member is { Name: "Read", Kind: "method" });
            }
            Assert.Contains(
                extracted.Members,
                member => member is
                {
                    Name: "Read",
                    Kind: "explicit-interface-implementation"
                });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_FoldsPublicInterfaceMethodImplAccessors()
    {
        using var stream = File.OpenRead(typeof(Half).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(reader, typeof(Half).FullName!);

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            type => type.FullName == typeof(Half).FullName);

        foreach (var surface in new[] { queried, extracted })
        {
            Assert.Contains(surface.Members, member => member is { Name: "MaxValue", Kind: "property" });
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_MaxValue");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_Zero");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_Pi");
        }
    }

    [Fact]
    public void TypeSurface_FoldsCanonicalVisualBasicMethodImplAccessors()
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        using var peReader = new PEReader(
            File.OpenRead(Path.Combine(runtimeDirectory, "Microsoft.VisualBasic.Core.dll")));
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(reader, "Microsoft.VisualBasic.Collection");

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            type => type.FullName == "Microsoft.VisualBasic.Collection");

        foreach (var surface in new[] { queried, extracted })
        {
            Assert.Contains(surface.Members, member => member.Name == "ICollectionCount");
            Assert.Contains(surface.Members, member => member.Name == "IListItem");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_ICollectionCount");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_IListItem");
            Assert.DoesNotContain(surface.Members, member => member.Name == "set_IListItem");
        }

        var defaultQueried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: false);
        var defaultExtracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: false).Types,
            type => type.FullName == "Microsoft.VisualBasic.Collection");
        foreach (var surface in new[] { defaultQueried, defaultExtracted })
        {
            Assert.DoesNotContain(surface.Members, member => member.Name == "ICollectionCount");
            Assert.Contains(surface.Members, member => member.Name == "get_ICollectionCount");
        }
    }

    [Fact]
    public void ExplicitInterfaceBodies_KeepAssemblyScopeInDeclarationIdentity()
    {
        string path = EmitSameNamedBaseAndInterfaceMethodImpl();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "Derived");
            var typeDef = reader.GetTypeDefinition(typeHandle);

            var bodies = ApiSurfaceExtractor.GetExplicitInterfaceImplementationBodies(
                reader,
                typeDef);
            var names = bodies
                .Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name))
                .ToList();

            Assert.Contains("ImplementInterface", names);
            Assert.DoesNotContain("OverrideBase", names);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_AccessorFactsPreserveReturnAttributes()
    {
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)),
            includeNonPublicMembers: true);
        var property = Assert.Single(
            surface.Members,
            member => member.Name == nameof(
                MetadataDeclarationQueryFixtures.PropertyWithReturnNotNull));
        var getter = Assert.Single(
            property.AccessorFacts,
            accessor => accessor.Kind == "get");

        Assert.Contains(
            getter.ReturnAttributes,
            attribute => attribute.Contains(
                "System.Diagnostics.CodeAnalysis.NotNull",
                StringComparison.Ordinal));
    }

    [Fact]
    public void PrivateScopeAccessors_AreNotClassifiedAsPublic()
    {
        var methodAccessibility = typeof(MetadataDeclarationQuery).GetMethod(
            "AccessibilityKeyword",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(MethodAttributes)]);
        var fieldAccessibility = typeof(MetadataDeclarationQuery).GetMethod(
            "AccessibilityKeyword",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(FieldAttributes)]);

        Assert.Equal("private", methodAccessibility!.Invoke(null, [MethodAttributes.PrivateScope]));
        Assert.Equal("private", fieldAccessibility!.Invoke(null, [FieldAttributes.PrivateScope]));
    }

    [Fact]
    public void ReservedMethodAccessibility_IsRejected()
    {
        var methodAccessibility = typeof(MetadataDeclarationQuery).GetMethod(
            "AccessibilityKeyword",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(MethodAttributes)]);

        var exception = Assert.Throws<TargetInvocationException>(
            () => methodAccessibility!.Invoke(null, [(MethodAttributes)0x7]));
        Assert.IsType<BadImageFormatException>(exception.InnerException);
    }

    [Fact]
    public void TypeSurface_EscapesKeywordMemberNames()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, "class");

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);
        var surface = MetadataDeclarationQuery.GetTypeSurface(Reader, GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        var property = Assert.Single(surface.Members, member => member.Name == "while");
        var field = Assert.Single(surface.Members, member => member.Name == "event");

        Assert.Equal("@class", declaration.CSharpName);
        Assert.Equal("@class", declaration.Signature.MemberName);
        Assert.Contains(surface.Members, member => member.Name == "class" && member.Signature!.Contains("@class", StringComparison.Ordinal));
        Assert.Equal("@while", property.SignatureModel!.MemberName);
        Assert.Contains("@while", property.Signature);
        Assert.Equal("@event", field.SignatureModel!.MemberName);
    }

    [Fact]
    public void TypeSurface_EscapesQualifiedKeywordParameterTypesInCompatibilitySignatures()
    {
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));

        var method = Assert.Single(surface.Members, member => member.Name == "QualifiedKeyword");

        Assert.Contains(
            "MetadataDeclarationQueryFixtures.@namespace @class",
            method.Signature,
            StringComparison.Ordinal);
        Assert.Contains("\".namespace\"", method.Signature, StringComparison.Ordinal);

        var globalKeyword = Assert.Single(
            surface.Members,
            member => member.Name == "GlobalKeyword");
        Assert.Contains(
            "GlobalType(typeof(@class), (@event)1)",
            globalKeyword.SignatureModel!.Parameters[0].Attributes);
        Assert.Equal(
            "(@event)1",
            globalKeyword.SignatureModel.Parameters[2].DefaultValueText);
        Assert.Contains("@class value", globalKeyword.Signature, StringComparison.Ordinal);
        Assert.Contains("List<@class> values = null", globalKeyword.Signature, StringComparison.Ordinal);
        Assert.Contains("@event mode = (@event)1", globalKeyword.Signature, StringComparison.Ordinal);
        Assert.Contains(
            "GlobalType(typeof(@class), (@event)1)",
            globalKeyword.Signature,
            StringComparison.Ordinal);
        Assert.Contains("\"a\\\"b.class\"", globalKeyword.Signature, StringComparison.Ordinal);

        var syntaxKeywords = Assert.Single(
            surface.Members,
            member => member.Name == "SyntaxKeywordTypes");
        Assert.Contains("@delegate delegateValue", syntaxKeywords.Signature, StringComparison.Ordinal);
        Assert.Contains("@readonly readonlyValue", syntaxKeywords.Signature, StringComparison.Ordinal);
        Assert.Contains("@scoped scopedValue", syntaxKeywords.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodDeclaration_PreservesNestedGenericTypeArgumentPlacement()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, nameof(MetadataDeclarationQueryFixtures.NestedGeneric));

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        const string nestedType =
            "ILInspector.Metadata.Tests.MetadataDeclarationQueryFixtures.Container<int>.Row<string>";
        Assert.Equal(nestedType, declaration.Signature.ReturnType);
        Assert.Equal(nestedType, Assert.Single(declaration.Signature.Parameters).Type);
        Assert.Null(declaration.SignatureDecodeStatus);
    }

    [Fact]
    public void SelfTypeSignature_IncludesDeclaringGenericParameters()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.Container<>.Row<>));

        var signature = MetadataDeclarationQuery.SelfTypeSignature(Reader, type);

        Assert.Equal("ILInspector.Metadata.Tests.MetadataDeclarationQueryFixtures.Container<T>.Row<U>", signature);
    }

    [Fact]
    public void GetGenericConstraintClauses_RendersSpecialConstraints()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));

        var structClauses = MetadataDeclarationQuery.GetGenericConstraintClauses(
            Reader, type, GetMethod(type, nameof(MetadataDeclarationQueryFixtures.StructConstraint)));
        Assert.Equal("struct", Assert.Contains("T", structClauses));

        var classClauses = MetadataDeclarationQuery.GetGenericConstraintClauses(
            Reader, type, GetMethod(type, nameof(MetadataDeclarationQueryFixtures.ClassNewConstraint)));
        Assert.Equal("class, new()", Assert.Contains("T", classClauses));
    }

    [Fact]
    public void SpellableConstraintClause_DropsExplicitObjectConstraint()
    {
        // C# forbids `where T : object` (CS0702); it must be dropped even though
        // Roslyn never emits it (non-C# compilers can).
        Assert.Null(MetadataDeclarationQuery.SpellableConstraintClause(
            new TypeParameter { Name = "T", Constraints = { "System.Object" } }));
        Assert.Equal("System.IComparable", MetadataDeclarationQuery.SpellableConstraintClause(
            new TypeParameter { Name = "T", Constraints = { "System.Object", "System.IComparable" } }));
        Assert.Equal("class, new()", MetadataDeclarationQuery.SpellableConstraintClause(
            new TypeParameter { Name = "T", Constraints = { "class", "new()" } }));
    }

    [Fact]
    public void SpellableConstraintClause_EscapesKeywordTypeNamesUsingStructuredKind()
    {
        // A special-constraint keyword and a type literally named the same keyword are
        // indistinguishable as raw strings; the structured kind disambiguates them so
        // the type name is escaped (@struct) while the keyword constraint stays verbatim.
        var parameter = new TypeParameter
        {
            Name = "T",
            Constraints = { "struct", "N.struct", "System.IComparable" },
            StructuredConstraints =
            [
                new TypeParameterConstraint("struct", IsTypeName: false),
                new TypeParameterConstraint("N.struct", IsTypeName: true),
                new TypeParameterConstraint("System.IComparable", IsTypeName: true),
            ],
        };

        Assert.Equal(
            "struct, N.@struct, System.IComparable",
            MetadataDeclarationQuery.SpellableConstraintClause(parameter));

        var globalKeyword = new TypeParameter
        {
            Name = "T",
            Constraints = { "class" },
            StructuredConstraints = [new TypeParameterConstraint("class", IsTypeName: true)],
        };
        Assert.Equal("@class", MetadataDeclarationQuery.SpellableConstraintClause(globalKeyword));
    }

    [Fact]
    public void IsVolatileField_DetectsVolatileModreq()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var context = GenericContext.ForType(Reader, type);
        Assert.True(MetadataDeclarationQuery.IsVolatileField(Reader, GetField(type, "VolatileField"), context));
        Assert.False(MetadataDeclarationQuery.IsVolatileField(Reader, GetField(type, "PlainField"), context));
    }

    [Fact]
    public void HasRequiredModifier_RequiresExactNamespace_NotSuffix()
    {
        var inner = new NamedTypeNode("System.Int32", isReferenceType: false);
        var exact = new ModifiedTypeNode(
            new NamedTypeNode("System.Runtime.CompilerServices.IsVolatile", isReferenceType: true), inner, isRequired: true);
        var wrongNamespace = new ModifiedTypeNode(
            new NamedTypeNode("Other.Namespace.IsVolatile", isReferenceType: true), inner, isRequired: true);
        var globalNamespace = new ModifiedTypeNode(
            new NamedTypeNode("IsVolatile", isReferenceType: true), inner, isRequired: true);

        Assert.True(exact.HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile"));
        Assert.False(wrongNamespace.HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile"));
        Assert.False(globalNamespace.HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile"));
    }

    static string EmitMethodWithoutParamRow()
    {
        var assemblyName = new AssemblyName("MissingParamRow");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("MissingParamSample", TypeAttributes.Public);
        var method = type.DefineMethod(
            "Echo",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        type.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"MissingParamRow-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitEventWithOtherAccessor()
    {
        var assemblyName = new AssemblyName("EventOther");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("EventOtherSample", TypeAttributes.Public);
        const MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        var add = type.DefineMethod("add_Changed", attributes, typeof(void), [typeof(EventHandler)]);
        add.GetILGenerator().Emit(OpCodes.Ret);
        var remove = type.DefineMethod("remove_Changed", attributes, typeof(void), [typeof(EventHandler)]);
        remove.GetILGenerator().Emit(OpCodes.Ret);
        var other = type.DefineMethod("other_Changed", attributes, typeof(void), Type.EmptyTypes);
        other.GetILGenerator().Emit(OpCodes.Ret);
        var eventBuilder = type.DefineEvent("Changed", EventAttributes.None, typeof(EventHandler));
        eventBuilder.SetAddOnMethod(add);
        eventBuilder.SetRemoveOnMethod(remove);
        eventBuilder.AddOtherMethod(other);
        type.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"EventOther-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitRemoveOnlyEvent()
    {
        var assemblyName = new AssemblyName("RemoveOnlyEvent");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("RemoveOnlyEventSample", TypeAttributes.Public);
        const MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        var remove = type.DefineMethod(
            "remove_Changed",
            attributes,
            typeof(void),
            [typeof(EventHandler)]);
        remove.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineEvent("Changed", EventAttributes.None, typeof(EventHandler))
            .SetRemoveOnMethod(remove);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"RemoveOnlyEvent-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitUnqualifiedMethodImplAccessor(bool isPublic)
    {
        var assemblyName = new AssemblyName("UnqualifiedMethodImplAccessor");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);

        var interfaceBuilder = module.DefineType(
            "IUnqualifiedAccessor",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceGetter = interfaceBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        interfaceBuilder
            .DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
            .SetGetMethod(interfaceGetter);
        var interfaceType = interfaceBuilder.CreateType();

        var type = module.DefineType("UnqualifiedAccessor", TypeAttributes.Public);
        type.AddInterfaceImplementation(interfaceType);
        var getter = type.DefineMethod(
            "Read",
            (isPublic ? MethodAttributes.Public : MethodAttributes.Private)
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        getter.GetILGenerator().Emit(OpCodes.Ldc_I4, 42);
        getter.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
            .SetGetMethod(getter);
        type.DefineMethodOverride(getter, interfaceType.GetMethod("get_Value")!);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"UnqualifiedMethodImplAccessor-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitSameNamedBaseAndInterfaceMethodImpl()
    {
        var baseAssembly = new PersistedAssemblyBuilder(
            new AssemblyName("SameNameBase"),
            typeof(object).Assembly);
        var baseModule = baseAssembly.DefineDynamicModule("SameNameBase");
        var baseBuilder = baseModule.DefineType("N.Slot", TypeAttributes.Public);
        var baseMethod = baseBuilder.DefineMethod(
            "BaseSlot",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        baseMethod.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        baseMethod.GetILGenerator().Emit(OpCodes.Ret);
        var baseType = baseBuilder.CreateType();

        var interfaceAssembly = new PersistedAssemblyBuilder(
            new AssemblyName("SameNameInterface"),
            typeof(object).Assembly);
        var interfaceModule = interfaceAssembly.DefineDynamicModule("SameNameInterface");
        var interfaceBuilder = interfaceModule.DefineType(
            "N.Slot",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceMethod = interfaceBuilder.DefineMethod(
            "InterfaceSlot",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        var interfaceType = interfaceBuilder.CreateType();

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("SameNameConsumer"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("SameNameConsumer");
        var type = module.DefineType("Derived", TypeAttributes.Public, baseType);
        type.AddInterfaceImplementation(interfaceType);

        var interfaceBody = type.DefineMethod(
            "ImplementInterface",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        interfaceBody.GetILGenerator().Emit(OpCodes.Ldc_I4_2);
        interfaceBody.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethodOverride(interfaceBody, interfaceMethod);

        var baseBody = type.DefineMethod(
            "OverrideBase",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        baseBody.GetILGenerator().Emit(OpCodes.Ldc_I4_3);
        baseBody.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethodOverride(baseBody, baseMethod);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"SameNameConsumer-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static TypeDefinition GetTypeDefinition(Type type)
        => Reader.GetTypeDefinition(GetTypeDefinitionHandle(type));

    static TypeDefinitionHandle GetTypeDefinitionHandle(Type type)
        => GetTypeDefinitionHandle(Reader, type.FullName!.Replace('+', '.'));

    static TypeDefinitionHandle GetTypeDefinitionHandle(MetadataReader reader, string fullName)
    {
        var metadataName = fullName.Replace('+', '.');
        metadataName = StripGenericArity(metadataName);
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (StripGenericArity(TypeResolver.GetFullName(reader, definition)) == metadataName)
                return handle;
        }

        throw new InvalidOperationException($"Type '{metadataName}' was not found.");
    }

    static MethodDefinition GetMethod(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetMethods())
        {
            var method = Reader.GetMethodDefinition(handle);
            if (Reader.GetString(method.Name) == name)
                return method;
        }

        throw new InvalidOperationException($"Method '{name}' was not found.");
    }

    static PropertyDefinition GetProperty(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetProperties())
        {
            var property = Reader.GetPropertyDefinition(handle);
            if (Reader.GetString(property.Name) == name)
                return property;
        }

        throw new InvalidOperationException($"Property '{name}' was not found.");
    }

    static FieldDefinition GetField(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetFields())
        {
            var field = Reader.GetFieldDefinition(handle);
            if (Reader.GetString(field.Name) == name)
                return field;
        }

        throw new InvalidOperationException($"Field '{name}' was not found.");
    }

    static string StripGenericArity(string value)
    {
        var tick = value.IndexOf('`');
        while (tick >= 0)
        {
            var end = tick + 1;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;
            value = value[..tick] + value[end..];
            tick = value.IndexOf('`');
        }

        return value;
    }
}

public class MetadataDeclarationQueryFixtures
{
    private readonly int _count = 1;

    [return: System.Diagnostics.CodeAnalysis.NotNull]
    protected virtual string? ProtectedVirtual(
        [MarshalAs(UnmanagedType.LPWStr)] string? value,
        int count = 2) => value ?? count.ToString();

    public decimal DecimalDefault(decimal amount = 5m) => amount;

    public int Count() => _count;

    public int @class() => 0;

    public int @while { get; set; }

    public int Restricted { get; private set; }

    public string InitValue { get; init; } = "";

    public string PropertyWithReturnNotNull
    {
        [return: System.Diagnostics.CodeAnalysis.NotNull]
        get => "value";
    }

    public int @event = 1;

    public void QualifiedKeyword(@namespace @class, string text = ".namespace") { }

    public void GlobalKeyword(
        [global::GlobalType(typeof(global::@class), (global::@event)1)] global::@class value,
        List<global::@class>? values = null,
        global::@event mode = (global::@event)1,
        string text = "a\"b.class")
    {
    }

    public int get_Orphan() => 0;

    public interface IExplicitSurface
    {
        int Value { get; }
        event EventHandler Changed;
    }

    public sealed class ExplicitSurface : IExplicitSurface
    {
        int IExplicitSurface.Value => 42;

        event EventHandler IExplicitSurface.Changed
        {
            add { }
            remove { }
        }
    }

    public void SyntaxKeywordTypes(
        global::@delegate delegateValue,
        global::@readonly readonlyValue,
        global::@scoped scopedValue)
    {
    }

    public Container<int>.Row<string> NestedGeneric(Container<int>.Row<string> value)
        => value;

    public volatile int VolatileField;

    public int PlainField;

    public void StructConstraint<T>() where T : struct { }

    public void ClassNewConstraint<T>() where T : class, new() { }

    public abstract class AbstractBase
    {
        protected abstract string Name { get; set; }
    }

    public unsafe interface IUnsafeSurface
    {
        void Consume(int* pointer);
    }

    public unsafe class UnsafeFields
    {
        public int* Pointer;
        public int* Value { get; set; }
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    public class Container<T>
    {
        public class Row<U>
        {
        }
    }

    public class @namespace
    {
    }
}
