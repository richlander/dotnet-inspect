using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.MetadataPrimitives;
using InertText.Encoding;

namespace ILInspector.Metadata.Tests;

public sealed class ApiQualifiedAnchorTests
{
    static readonly FixturePair Pair =
        FixtureCatalog.MetadataApiCorrespondencePair;
    const string FixtureNamespace = "MetadataCorrespondenceFixture";
    const string FixtureType = "Container`1";

    [Fact]
    public void TypeAnchor_ErasesImageAndVersionLocalFacts()
    {
        MetadataDeclarationLocation oldLocation =
            FindLocation(
                Pair.OldAssemblyPath(),
                FixtureNamespace,
                FixtureType,
                ApiDeclarationKind.Type);
        MetadataDeclarationLocation newLocation =
            FindLocation(
                Pair.NewAssemblyPath(),
                FixtureNamespace,
                FixtureType,
                ApiDeclarationKind.Type);

        ApiQualifiedAnchor oldAnchor =
            Issue(Pair.OldAssemblyPath(), oldLocation);
        ApiQualifiedAnchor newAnchor =
            Issue(Pair.NewAssemblyPath(), newLocation);

        Assert.NotEqual(
            oldLocation.ModuleVersionId,
            newLocation.ModuleVersionId);
        Assert.Equal(oldAnchor, newAnchor);
        Assert.Equal(
            new ApiQualifiedAssemblyFamily(
                "ILInspector.Metadata.ApiDeclarationCorrespondence",
                culture: null,
                publicKeyToken: null),
            oldAnchor.AssemblyFamily);
    }

    [Theory]
    [InlineData(ApiDeclarationKind.Method, "StableMethod")]
    [InlineData(ApiDeclarationKind.Method, "BodyOnly")]
    [InlineData(ApiDeclarationKind.Property, "StableProperty")]
    [InlineData(ApiDeclarationKind.Property, "AccessorChanged")]
    [InlineData(ApiDeclarationKind.Event, "StableEvent")]
    [InlineData(ApiDeclarationKind.Field, "StableField")]
    public void MemberAnchor_MatchesAcrossVersionAndRowOrder(
        ApiDeclarationKind kind,
        string memberName)
    {
        MetadataDeclarationLocation oldLocation =
            FindLocation(
                Pair.OldAssemblyPath(),
                FixtureNamespace,
                FixtureType,
                kind,
                memberName);
        MetadataDeclarationLocation newLocation =
            FindLocation(
                Pair.NewAssemblyPath(),
                FixtureNamespace,
                FixtureType,
                kind,
                memberName);
        var resolver = new ScopeAuthorizingResolver();

        ApiQualifiedAnchor.Member oldAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(Pair.OldAssemblyPath(), oldLocation, resolver));
        ApiQualifiedAnchor.Member newAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(Pair.NewAssemblyPath(), newLocation, resolver));

        Assert.Equal(oldAnchor, newAnchor);
        Assert.Equal(
            oldAnchor.CompanionAnchor,
            newAnchor.CompanionAnchor);
    }

    [Theory]
    [InlineData(ApiDeclarationKind.Method, "ReturnChanged")]
    [InlineData(ApiDeclarationKind.Method, "StaticMethodChanged")]
    [InlineData(ApiDeclarationKind.Method, "ParameterFlagsChanged")]
    [InlineData(ApiDeclarationKind.Method, "ArrayChanged")]
    [InlineData(ApiDeclarationKind.Method, "FunctionPointerChanged")]
    [InlineData(ApiDeclarationKind.Property, "ChangedProperty")]
    [InlineData(ApiDeclarationKind.Property, "StaticPropertyChanged")]
    [InlineData(ApiDeclarationKind.Event, "ChangedEvent")]
    [InlineData(ApiDeclarationKind.Event, "StaticEventChanged")]
    [InlineData(ApiDeclarationKind.Field, "ChangedField")]
    [InlineData(ApiDeclarationKind.Field, "StaticFieldChanged")]
    public void MemberAnchor_RetainsDesignedDiscriminators(
        ApiDeclarationKind kind,
        string memberName)
    {
        var resolver = new ScopeAuthorizingResolver();
        ApiQualifiedAnchor oldAnchor = Issue(
            Pair.OldAssemblyPath(),
            FindLocation(
                Pair.OldAssemblyPath(),
                FixtureNamespace,
                FixtureType,
                kind,
                memberName),
            resolver);
        ApiQualifiedAnchor newAnchor = Issue(
            Pair.NewAssemblyPath(),
            FindLocation(
                Pair.NewAssemblyPath(),
                FixtureNamespace,
                FixtureType,
                kind,
                memberName),
            resolver);

        Assert.NotEqual(oldAnchor, newAnchor);
    }

    [Fact]
    public void TypeAnchor_ErasesGenericConstraints()
    {
        ApiQualifiedAnchor oldAnchor = Issue(
            Pair.OldAssemblyPath(),
            FindLocation(
                Pair.OldAssemblyPath(),
                FixtureNamespace,
                "ConstraintChanged`1",
                ApiDeclarationKind.Type));
        ApiQualifiedAnchor newAnchor = Issue(
            Pair.NewAssemblyPath(),
            FindLocation(
                Pair.NewAssemblyPath(),
                FixtureNamespace,
                "ConstraintChanged`1",
                ApiDeclarationKind.Type));

        Assert.Equal(oldAnchor, newAnchor);
    }

    [Fact]
    public void MemberAnchor_ErasesNonShapeDeclarationDetails()
    {
        const string memberName = "ErasedDeclarationDetails";
        ApiQualifiedAnchor.Member oldAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    Pair.OldAssemblyPath(),
                    FindLocation(
                        Pair.OldAssemblyPath(),
                        FixtureNamespace,
                        FixtureType,
                        ApiDeclarationKind.Method,
                        memberName)));
        ApiQualifiedAnchor.Member newAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    Pair.NewAssemblyPath(),
                    FindLocation(
                        Pair.NewAssemblyPath(),
                        FixtureNamespace,
                        FixtureType,
                        ApiDeclarationKind.Method,
                        memberName)));

        Assert.Equal(oldAnchor, newAnchor);
        Assert.Equal(
            oldAnchor.CompanionAnchor,
            newAnchor.CompanionAnchor);
    }

    [Theory]
    [InlineData("SpecialMethods", ".ctor")]
    [InlineData(
        "SpecialMethods",
        "MetadataCorrespondenceFixture.IExplicit.M")]
    [InlineData("Extensions", "Twice")]
    public void CompanionAnchor_MatchesCurrentIdentityForSpecialMethods(
        string typeName,
        string methodName)
    {
        var resolver = new ScopeAuthorizingResolver();
        ApiQualifiedAnchor.Member oldAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    Pair.OldAssemblyPath(),
                    FindLocation(
                        Pair.OldAssemblyPath(),
                        FixtureNamespace,
                        typeName,
                        ApiDeclarationKind.Method,
                        methodName),
                    resolver));
        ApiQualifiedAnchor.Member newAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    Pair.NewAssemblyPath(),
                    FindLocation(
                        Pair.NewAssemblyPath(),
                        FixtureNamespace,
                        typeName,
                        ApiDeclarationKind.Method,
                        methodName),
                    resolver));

        Assert.Equal(oldAnchor, newAnchor);
        Assert.Equal(
            oldAnchor.CompanionAnchor,
            newAnchor.CompanionAnchor);
        if (methodName == "Twice")
        {
            Assert.StartsWith(
                "extension:Twice~",
                oldAnchor.CompanionAnchor.StableSelector,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CompanionAnchor_MatchesCurrentConversionIdentity()
    {
        string path = typeof(ApiQualifiedAnchorTests).Assembly.Location;
        ApiQualifiedAnchor.Member anchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    path,
                    FindLocation(
                        path,
                        "ILInspector.Metadata.Tests",
                        nameof(ApiQualifiedConversionFixture),
                        ApiDeclarationKind.Method,
                        "op_Explicit")));

        Assert.Equal(
            "op_Explicit",
            anchor.CompanionAnchor.MemberName);
        Assert.StartsWith(
            "operator:op_Explicit~",
            anchor.CompanionAnchor.StableSelector,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ModelEquality_RetainsKindNameArityModifiersAndNamedFamily()
    {
        var resolver = new ScopeAuthorizingResolver();
        ApiQualifiedAnchor.Member methodAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    Pair.OldAssemblyPath(),
                    FindLocation(
                        Pair.OldAssemblyPath(),
                        FixtureNamespace,
                        FixtureType,
                        ApiDeclarationKind.Method,
                        "StableMethod"),
                    resolver));
        ApiQualifiedMemberDeclaration.Method method =
            Assert.IsType<ApiQualifiedMemberDeclaration.Method>(
                methodAnchor.Declaration);
        var renamed = new ApiQualifiedAnchor.Member(
            methodAnchor.Format,
            methodAnchor.AssemblyFamily,
            methodAnchor.DeclaringType,
            method with { Name = "Other" },
            methodAnchor.CompanionAnchor);
        var differentArity = new ApiQualifiedAnchor.Member(
            methodAnchor.Format,
            methodAnchor.AssemblyFamily,
            methodAnchor.DeclaringType,
            method with { GenericArity = method.GenericArity + 1 },
            methodAnchor.CompanionAnchor);
        var differentKind = new ApiQualifiedAnchor.Member(
            methodAnchor.Format,
            methodAnchor.AssemblyFamily,
            methodAnchor.DeclaringType,
            new ApiQualifiedMemberDeclaration.Field(
                method.Name,
                method.Static,
                method.ReturnType),
            methodAnchor.CompanionAnchor);
        var modifier = new ApiQualifiedTypeIdentity.Named(
            methodAnchor.DeclaringType.Definition,
            IsValueType: false);
        var modifiedReturn = new ApiQualifiedAnchor.Member(
            methodAnchor.Format,
            methodAnchor.AssemblyFamily,
            methodAnchor.DeclaringType,
            method with
            {
                ReturnType = new ApiQualifiedTypeIdentity.Modified(
                    modifier,
                    method.ReturnType,
                    IsRequired: true),
            },
            methodAnchor.CompanionAnchor);
        var differentCompanion = new ApiQualifiedAnchor.Member(
            methodAnchor.Format,
            methodAnchor.AssemblyFamily,
            methodAnchor.DeclaringType,
            methodAnchor.Declaration,
            methodAnchor.CompanionAnchor with
            {
                MemberName = "independent-policy",
            });

        Assert.NotEqual(methodAnchor, renamed);
        Assert.NotEqual(methodAnchor, differentArity);
        Assert.NotEqual(methodAnchor, differentKind);
        Assert.NotEqual(methodAnchor, modifiedReturn);
        Assert.Equal(methodAnchor, differentCompanion);
        Assert.Equal(
            methodAnchor.GetHashCode(),
            differentCompanion.GetHashCode());

        ApiQualifiedAnchor.Member eventAnchor =
            Assert.IsType<ApiQualifiedAnchor.Member>(
                Issue(
                    Pair.OldAssemblyPath(),
                    FindLocation(
                        Pair.OldAssemblyPath(),
                        FixtureNamespace,
                        FixtureType,
                        ApiDeclarationKind.Event,
                        "StableEvent"),
                    resolver));
        ApiQualifiedMemberDeclaration.Event eventDeclaration =
            Assert.IsType<ApiQualifiedMemberDeclaration.Event>(
                eventAnchor.Declaration);
        ApiQualifiedTypeIdentity.GenericInstance eventType =
            Assert.IsType<ApiQualifiedTypeIdentity.GenericInstance>(
                eventDeclaration.EventType);
        ApiQualifiedTypeDefinitionIdentity otherFamily =
            new(
                new ApiQualifiedAssemblyFamily(
                    "Different.Assembly",
                    culture: null,
                    publicKeyToken: null),
                eventType.Definition.Namespace,
                eventType.Definition.Segments,
                eventType.Definition
                    .IntroducedGenericParameterCounts);
        var differentFamily = new ApiQualifiedAnchor.Member(
            eventAnchor.Format,
            eventAnchor.AssemblyFamily,
            eventAnchor.DeclaringType,
            eventDeclaration with
            {
                EventType = eventType with
                {
                    Definition = otherFamily,
                },
            },
            eventAnchor.CompanionAnchor);

        Assert.NotEqual(eventAnchor, differentFamily);
    }

    [Fact]
    public void ExternalNamedType_RequiresOwnerAuthorizedResolution()
    {
        string path = Pair.OldAssemblyPath();
        MetadataDeclarationLocation location = FindLocation(
            path,
            FixtureNamespace,
            FixtureType,
            ApiDeclarationKind.Event,
            "StableEvent");

        ApiQualifiedAnchorResult result =
            IssueResult(path, location, resolver: null);

        ApiQualifiedAnchorResult.Refused refused =
            Assert.IsType<ApiQualifiedAnchorResult.Refused>(
                result,
                exactMatch: true);
        Assert.Equal(
            ApiQualifiedAnchorRefusalReason.ResolverRequired,
            refused.Refusal.Reason);
    }

    [Fact]
    public void RefAndImplementationMethod_ShareOneAnchorAtDifferentLocations()
    {
        string referencePath = Pinned(
            "ref",
            "System.Net.Sockets.dll");
        string implementationPath = Pinned(
            "runtime",
            "System.Net.Sockets.dll");
        const string canonical =
            "M:System.Net.Sockets.Socket.Close()";
        MetadataDeclarationLocation referenceLocation =
            FindMethodByCompanionAnchor(
                referencePath,
                "System.Net.Sockets",
                "Socket",
                canonical);
        MetadataDeclarationLocation implementationLocation =
            FindMethodByCompanionAnchor(
                implementationPath,
                "System.Net.Sockets",
                "Socket",
                canonical);

        ApiQualifiedAnchor reference =
            Issue(referencePath, referenceLocation);
        ApiQualifiedAnchor implementation =
            Issue(implementationPath, implementationLocation);

        Assert.NotEqual(
            referenceLocation.ModuleVersionId,
            implementationLocation.ModuleVersionId);
        Assert.NotEqual(
            referenceLocation.MetadataToken,
            implementationLocation.MetadataToken);
        Assert.Equal(reference, implementation);
    }

    [Fact]
    public void ConfigurationReferenceAndImplementation_LoadShareOneAnchor()
    {
        string referencePath = Pinned(
            "packages",
            "Microsoft.Extensions.Configuration.10.0.10.ref.dll");
        string implementationPath = Pinned(
            "packages",
            "Microsoft.Extensions.Configuration.10.0.10.lib.dll");
        const string canonical =
            "M:Microsoft.Extensions.Configuration.StreamConfigurationProvider.Load(System.IO.Stream)";
        var resolver = new ScopeAuthorizingResolver();

        ApiQualifiedAnchor reference = Issue(
            referencePath,
            FindMethodByCompanionAnchor(
                referencePath,
                "Microsoft.Extensions.Configuration",
                "StreamConfigurationProvider",
                canonical),
            resolver);
        ApiQualifiedAnchor implementation = Issue(
            implementationPath,
            FindMethodByCompanionAnchor(
                implementationPath,
                "Microsoft.Extensions.Configuration",
                "StreamConfigurationProvider",
                canonical),
            resolver);

        Assert.Equal(reference, implementation);
    }

    [Fact]
    public void SystemTextJsonVersionPair_SharesMethodAnchor()
    {
        string oldPath = Pinned(
            "packages",
            "System.Text.Json.9.0.0.dll");
        string newPath = Pinned(
            "packages",
            "System.Text.Json.10.0.0.dll");
        const string canonical =
            "M:System.Text.Json.JsonSerializerOptions.MakeReadOnly()";
        MetadataDeclarationLocation oldLocation =
            FindMethodByCompanionAnchor(
                oldPath,
                "System.Text.Json",
                "JsonSerializerOptions",
                canonical);
        MetadataDeclarationLocation newLocation =
            FindMethodByCompanionAnchor(
                newPath,
                "System.Text.Json",
                "JsonSerializerOptions",
                canonical);

        ApiQualifiedAnchor oldAnchor =
            Issue(oldPath, oldLocation);
        ApiQualifiedAnchor newAnchor =
            Issue(newPath, newLocation);

        Assert.Equal(oldAnchor, newAnchor);
    }

    [Fact]
    public void InvalidPhysicalLocation_FailsWithoutDegradedAnchor()
    {
        string path = Pair.OldAssemblyPath();
        MetadataDeclarationLocation valid = FindLocation(
            path,
            FixtureNamespace,
            FixtureType,
            ApiDeclarationKind.Type);
        MetadataDeclarationLocation invalid = valid with
        {
            MetadataToken = 0x0200ffff,
        };

        ApiQualifiedAnchorResult result =
            IssueResult(path, invalid);

        ApiQualifiedAnchorResult.Failed failed =
            Assert.IsType<ApiQualifiedAnchorResult.Failed>(result);
        Assert.Equal(
            ApiQualifiedAnchorFailureReason.InvalidAddress,
            failed.Failure.Reason);
    }

    [Fact]
    public void ExhaustedStructuredWorkBudget_FailsWithoutDegradedAnchor()
    {
        string path = Pair.OldAssemblyPath();
        MetadataDeclarationLocation location = FindLocation(
            path,
            FixtureNamespace,
            FixtureType,
            ApiDeclarationKind.Type);
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxStructuredNodes: 0);

        ApiQualifiedAnchorResult result =
            IssueResult(path, location, policy: policy);

        ApiQualifiedAnchorResult.Failed failed =
            Assert.IsType<ApiQualifiedAnchorResult.Failed>(result);
        Assert.Equal(
            ApiQualifiedAnchorFailureReason.WorkLimitExceeded,
            failed.Failure.Reason);
    }

    [Fact]
    public void AmbiguousLocalTypeName_IsRefused()
    {
        (byte[] image, MetadataDeclarationLocation location) =
            BuildAmbiguousTypeImage();

        ApiQualifiedAnchorResult result =
            IssueResult(image, location);

        Assert.True(
            result is ApiQualifiedAnchorResult.Refused,
            result.ToString());
        var refused =
            (ApiQualifiedAnchorResult.Refused)result;
        Assert.Equal(
            ApiQualifiedAnchorRefusalReason.AmbiguousType,
            refused.Refusal.Reason);
    }

    [Theory]
    [InlineData(NamedTypeScopeFixture.MissingLocal,
        ApiQualifiedAnchorRefusalReason.UnresolvedType)]
    [InlineData(NamedTypeScopeFixture.AmbiguousLocal,
        ApiQualifiedAnchorRefusalReason.AmbiguousType)]
    [InlineData(NamedTypeScopeFixture.Module,
        ApiQualifiedAnchorRefusalReason.ModuleScopedType)]
    public void NonPortableNamedType_DoesNotProduceDegradedSuccess(
        NamedTypeScopeFixture scope,
        ApiQualifiedAnchorRefusalReason expected)
    {
        (byte[] image, MetadataDeclarationLocation location) =
            BuildNamedTypeMethodImage(scope);

        ApiQualifiedAnchorResult result =
            IssueResult(image, location);

        Assert.True(
            result is ApiQualifiedAnchorResult.Refused,
            result.ToString());
        var refused =
            (ApiQualifiedAnchorResult.Refused)result;
        Assert.Equal(expected, refused.Refusal.Reason);
    }

    [Fact]
    public void MalformedFieldSignature_FailsWithoutDegradedAnchor()
    {
        (byte[] image, MetadataDeclarationLocation location) =
            BuildMalformedFieldImage();

        ApiQualifiedAnchorResult result =
            IssueResult(image, location);

        ApiQualifiedAnchorResult.Failed failed =
            Assert.IsType<ApiQualifiedAnchorResult.Failed>(result);
        Assert.Equal(
            ApiQualifiedAnchorFailureReason.MalformedMetadata,
            failed.Failure.Reason);
    }

    static ApiQualifiedAnchor Issue(
        string path,
        MetadataDeclarationLocation location,
        IApiQualifiedTypeDefinitionResolver? resolver = null)
    {
        ApiQualifiedAnchorResult result =
            IssueResult(path, location, resolver);
        return Assert.IsType<ApiQualifiedAnchorResult.Complete>(result)
            .Anchor;
    }

    static ApiQualifiedAnchorResult IssueResult(
        string path,
        MetadataDeclarationLocation location,
        IApiQualifiedTypeDefinitionResolver? resolver = null,
        MetadataOperationPolicy? policy = null)
    {
        using AssemblyInspectionSession assembly =
            AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.PostApiQualifiedAnchor(
            location,
            resolver);
    }

    static ApiQualifiedAnchorResult IssueResult(
        byte[] image,
        MetadataDeclarationLocation location)
    {
        using AssemblyInspectionSession assembly =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.PostApiQualifiedAnchor(location);
    }

    static MetadataDeclarationLocation FindMethodByCompanionAnchor(
        string path,
        string @namespace,
        string typeName,
        string canonicalSignature)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            FindType(reader, @namespace, typeName);
        MethodDefinitionHandle method = Assert.Single(
            reader.GetTypeDefinition(type).GetMethods(),
            handle => ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    type,
                    reader.GetMethodDefinition(handle))
                .CanonicalSignature == canonicalSignature);
        return Location(
            reader,
            ApiDeclarationMetadataTable.MethodDefinition,
            method);
    }

    static MetadataDeclarationLocation FindLocation(
        string path,
        string @namespace,
        string typeName,
        ApiDeclarationKind kind,
        string? memberName = null)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            FindType(reader, @namespace, typeName);
        TypeDefinition definition = reader.GetTypeDefinition(type);

        return kind switch
        {
            ApiDeclarationKind.Type => Location(
                reader,
                ApiDeclarationMetadataTable.TypeDefinition,
                type),
            ApiDeclarationKind.Method => Location(
                reader,
                ApiDeclarationMetadataTable.MethodDefinition,
                Assert.Single(
                    definition.GetMethods(),
                    handle => reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                        == memberName)),
            ApiDeclarationKind.Property => Location(
                reader,
                ApiDeclarationMetadataTable.PropertyDefinition,
                Assert.Single(
                    definition.GetProperties(),
                    handle => reader.GetString(
                        reader.GetPropertyDefinition(handle).Name)
                        == memberName)),
            ApiDeclarationKind.Event => Location(
                reader,
                ApiDeclarationMetadataTable.EventDefinition,
                Assert.Single(
                    definition.GetEvents(),
                    handle => reader.GetString(
                        reader.GetEventDefinition(handle).Name)
                        == memberName)),
            ApiDeclarationKind.Field => Location(
                reader,
                ApiDeclarationMetadataTable.FieldDefinition,
                Assert.Single(
                    definition.GetFields(),
                    handle => reader.GetString(
                        reader.GetFieldDefinition(handle).Name)
                        == memberName)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string @namespace,
        string name)
        => Assert.Single(
            reader.TypeDefinitions,
            handle =>
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(handle);
                return reader.GetString(definition.Namespace)
                        == @namespace &&
                    reader.GetString(definition.Name) == name;
            });

    static MetadataDeclarationLocation Location(
        MetadataReader reader,
        ApiDeclarationMetadataTable table,
        EntityHandle handle)
        => new(
            MetadataModuleIdentity.ReadVersionId(reader),
            table,
            MetadataTokens.GetToken(handle));

    static string Pinned(string directory, string file)
        => Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            directory,
            file);

    static (
        byte[] Image,
        MetadataDeclarationLocation Location)
        BuildAmbiguousTypeImage()
    {
        MetadataBuilder metadata = NewMetadata(
            "AmbiguousAnchor",
            out Guid mvid,
            out _);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle first = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Duplicate"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Duplicate"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        return (
            Serialize(metadata),
            new(
                mvid,
                ApiDeclarationMetadataTable.TypeDefinition,
                MetadataTokens.GetToken(first)));
    }

    static (
        byte[] Image,
        MetadataDeclarationLocation Location)
        BuildNamedTypeMethodImage(NamedTypeScopeFixture scope)
    {
        MetadataBuilder metadata = NewMetadata(
            "NamedTypeAnchor",
            out Guid mvid,
            out ModuleDefinitionHandle module);
        EntityHandle resolutionScope =
            scope == NamedTypeScopeFixture.Module
                ? metadata.AddModuleReference(
                    metadata.GetOrAddString("other.netmodule"))
                : module;
        TypeReferenceHandle referenced =
            metadata.AddTypeReference(
                resolutionScope,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Referenced"));
        int codedType = (MetadataTokens.GetRowNumber(referenced) << 2) | 1;
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(codedType);
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        if (scope == NamedTypeScopeFixture.AmbiguousLocal)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Referenced"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("Referenced"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        }

        return (
            Serialize(metadata),
            new(
                mvid,
                ApiDeclarationMetadataTable.MethodDefinition,
                MetadataTokens.GetToken(method)));
    }

    static (
        byte[] Image,
        MetadataDeclarationLocation Location)
        BuildMalformedFieldImage()
    {
        MetadataBuilder metadata = NewMetadata(
            "MalformedFieldAnchor",
            out Guid mvid,
            out _);
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        FieldDefinitionHandle field =
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(signature));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Owner"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));

        return (
            Serialize(metadata),
            new(
                mvid,
                ApiDeclarationMetadataTable.FieldDefinition,
                MetadataTokens.GetToken(field)));
    }

    static MetadataBuilder NewMetadata(
        string assemblyName,
        out Guid mvid,
        out ModuleDefinitionHandle module)
    {
        var metadata = new MetadataBuilder();
        mvid = Guid.NewGuid();
        module = metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            (AssemblyFlags)0,
            AssemblyHashAlgorithm.None);
        return metadata;
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    public enum NamedTypeScopeFixture
    {
        MissingLocal,
        AmbiguousLocal,
        Module,
    }

    sealed class ScopeAuthorizingResolver :
        IApiQualifiedTypeDefinitionResolver
    {
        public ApiQualifiedTypeDefinitionResolution Resolve(
            MetadataNamedTypeIdentity reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataAssemblyIdentity? assembly =
                reference.Scope.Assembly;
            if (assembly is null)
            {
                return new ApiQualifiedTypeDefinitionResolution.Refused(
                    ApiQualifiedAnchorRefusalReason.UnresolvedType,
                    "The test authority has no assembly definition for this reference.");
            }

            return new ApiQualifiedTypeDefinitionResolution.Resolved(
                new(
                    new ApiQualifiedAssemblyFamily(
                        Decode(assembly.Name),
                        assembly.Culture is { } culture
                            ? Decode(culture)
                            : null,
                        assembly.PublicKeyToken is { } token
                            ? Decode(token)
                            : null),
                    reference.Namespace,
                    reference.Segments,
                    reference.IntroducedGenericParameterCounts));
        }

        static string Decode(InertText.InertString value)
            => VisualEncoder.TryDecode(
                value.ToString(),
                out string? decoded)
                ? decoded
                : throw new InvalidOperationException(
                    "The contained test identity must decode.");
    }
}

public readonly struct ApiQualifiedConversionFixture
{
    public static explicit operator int(
        ApiQualifiedConversionFixture value)
        => 0;
}
