using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class CatalogMemberCorrespondencePlanTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void RecursiveOpenSignature_ProjectsOneReusableExactKey()
    {
        byte[] image = BuildAssembly("Owner", ["Owner", "Arg"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        TypeRef argument = ReadDefinition(image, "Arg");
        MethodIdentity member = Method(
            source,
            image,
            owner,
            [
                TypeRef.GenericInstance(
                    argument,
                    [TypeRef.SzArray(argument)]),
            ],
            TypeRef.Pointer(argument),
            genericArity: 2);

        CatalogMemberCorrespondencePlan first =
            CatalogMemberCorrespondencePlan.Create(source, member);
        CatalogMemberCorrespondencePlan second =
            CatalogMemberCorrespondencePlan.Create(source, member);

        Assert.True(first.IsStructurallyComplete);
        Assert.Equal(2, first.Requests.Length);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            first.Requests
                .Concat(second.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey firstKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                first.Project(context)).Key;
        CatalogMemberJoinKey secondKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                second.Project(context)).Key;

        Assert.Equal(CatalogMemberCorrespondenceKind.Exact, firstKey.Kind);
        Assert.Equal(firstKey, secondKey);
        Assert.Equal(firstKey.GetHashCode(), secondKey.GetHashCode());
        Assert.Equal(2, firstKey.GenericArity);
        Assert.Equal(
            CatalogTypeShapeKind.GenericInstance,
            firstKey.ParameterTypes[0].Kind);
        Assert.Equal(
            CatalogTypeShapeKind.SzArray,
            firstKey.ParameterTypes[0].Components[0].Kind);
        Assert.Equal(
            CatalogTypeShapeKind.Pointer,
            firstKey.ReturnType.Kind);
    }

    [Fact]
    public void ForwardedDeclaringParameterAndReturnTypes_JoinDefinition()
    {
        byte[] targetImage = BuildAssembly("Target", ["Type"]);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        byte[] facadeImage = BuildFacade(
            "Facade",
            target.Identity,
            "Type");
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        byte[] callerImage = BuildAssembly(
            "Caller",
            [],
            (facade.Identity, "Type"));
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        TypeRef forwarded = ReadReference(callerImage);
        TypeRef definition = ReadDefinition(targetImage, "Type");
        var callSite = new MemberRef(
            forwarded,
            "M",
            [TypeRef.SzArray(forwarded)],
            forwarded,
            MemberKind.Method)
        {
            OpenParameterTypes = [TypeRef.SzArray(forwarded)],
            OpenReturnType = forwarded,
        };
        CatalogMemberCorrespondencePlan callerPlan =
            CatalogMemberCorrespondencePlan.Create(caller, callSite);
        CatalogMemberCorrespondencePlan definitionPlan =
            CatalogMemberCorrespondencePlan.Create(
                target,
                Method(
                    target,
                    targetImage,
                    definition,
                    [TypeRef.SzArray(definition)],
                    definition));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new ExactPolicy([facade, target]),
            [caller, facade, target],
            callerPlan.Requests
                .Concat(definitionPlan.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey callerKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                callerPlan.Project(context)).Key;
        CatalogMemberJoinKey definitionKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                definitionPlan.Project(context)).Key;

        Assert.Equal(CatalogMemberCorrespondenceKind.Exact, callerKey.Kind);
        Assert.Equal(callerKey, definitionKey);
    }

    [Fact]
    public void MethodGenericArity_IsIdentityBearing()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        MethodIdentity arityOne = Method(
            source,
            image,
            owner,
            [TypeRef.MethodGenericParameter(0)],
            owner,
            genericArity: 1);
        MethodIdentity arityTwo = arityOne with { GenericArity = 2 };
        CatalogMemberCorrespondencePlan first =
            CatalogMemberCorrespondencePlan.Create(source, arityOne);
        CatalogMemberCorrespondencePlan second =
            CatalogMemberCorrespondencePlan.Create(source, arityTwo);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            first.Requests
                .Concat(second.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey firstKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                first.Project(context)).Key;
        CatalogMemberJoinKey secondKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                second.Project(context)).Key;

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void UnboundNamedType_ProjectsDegradedIndeterminateKey()
    {
        var missing = new AssemblyReferenceIdentity(
            "Missing",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        byte[] image = BuildAssembly(
            "Owner",
            ["Owner"],
            (missing, "MissingType"));
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        TypeRef unavailable = ReadReference(image);
        MethodIdentity member = Method(
            source,
            image,
            owner,
            [TypeRef.ByRef(unavailable)],
            owner);
        CatalogMemberCorrespondencePlan plan =
            CatalogMemberCorrespondencePlan.Create(source, member);
        CatalogMemberCorrespondencePlan equivalentPlan =
            CatalogMemberCorrespondencePlan.Create(source, member);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            plan.Requests
                .Concat(equivalentPlan.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        var issued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                plan.Project(context));
        CatalogMemberJoinKey equivalentKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                equivalentPlan.Project(context)).Key;

        Assert.Equal(
            CatalogMemberCorrespondenceKind.Indeterminate,
            issued.Key.Kind);
        Assert.Equal(issued.Key, equivalentKey);
        Assert.Equal(
            issued.Key.GetHashCode(),
            equivalentKey.GetHashCode());
        Assert.Equal(
            CatalogTypeShapeKind.DegradedDefinition,
            issued.Key.ParameterTypes[0].ElementType!.Kind);
        Assert.Single(
            issued.Evidence.OfType<
                MemberCorrespondenceEvidence.UnresolvedBinding>());
    }

    [Fact]
    public void OneUnresolvedBinding_RetainsEachStructuredTypeName()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        var missing = new AssemblyReferenceIdentity(
            "Missing",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        TypeRef first = ReferencedType(missing, "First");
        TypeRef second = ReferencedType(missing, "Second");
        CatalogMemberCorrespondencePlan plan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [first, second], owner));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            plan.Requests);

        var issued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                plan.Project(context));
        MemberCorrespondenceEvidence.UnresolvedBinding[] evidence =
            issued.Evidence
                .OfType<
                    MemberCorrespondenceEvidence.UnresolvedBinding>()
                .ToArray();

        Assert.Equal(2, evidence.Length);
        Assert.Equal(
            new[] { TypeName("First"), TypeName("Second") }.ToHashSet(),
            evidence.Select(item => item.Type).ToHashSet());
    }

    [Fact]
    public void UnavailableBinding_IsEligibleButSourceDomainsStayDistinct()
    {
        var missing = new AssemblyReferenceIdentity(
            "Missing",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        byte[] image = BuildAssembly(
            "Owner",
            ["Owner"],
            (missing, "MissingType"));
        ResolvedAssemblyReference firstSource = Descriptor(image);
        ResolvedAssemblyReference secondSource = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        TypeRef unavailable = ReadReference(image);
        CatalogMemberCorrespondencePlan first =
            CatalogMemberCorrespondencePlan.Create(
                firstSource,
                Method(
                    firstSource,
                    image,
                    owner,
                    [unavailable],
                    owner));
        CatalogMemberCorrespondencePlan second =
            CatalogMemberCorrespondencePlan.Create(
                secondSource,
                Method(
                    secondSource,
                    image,
                    owner,
                    [unavailable],
                    owner));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            UnavailablePolicy.Instance,
            [firstSource, secondSource],
            first.Requests.Concat(second.Requests));

        var firstIssued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                first.Project(context));
        var secondIssued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                second.Project(context));

        Assert.Equal(
            CatalogMemberCorrespondenceKind.Indeterminate,
            firstIssued.Key.Kind);
        Assert.NotEqual(firstIssued.Key, secondIssued.Key);
        Assert.NotEqual(
            firstIssued.Key.ParameterTypes[0].UnresolvedBinding,
            secondIssued.Key.ParameterTypes[0].UnresolvedBinding);
        Assert.IsType<TypeResolutionOutcome.Unavailable>(
            Assert.Single(
                firstIssued.Evidence.OfType<
                    MemberCorrespondenceEvidence.UnresolvedBinding>())
                .Outcome);
    }

    [Fact]
    public void DuplicateDefinitions_ProjectOneIndeterminateMemberClass()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference firstSource = Descriptor(image);
        ResolvedAssemblyReference secondSource = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        CatalogMemberCorrespondencePlan first =
            CatalogMemberCorrespondencePlan.Create(
                firstSource,
                Method(firstSource, image, owner, [], owner));
        CatalogMemberCorrespondencePlan second =
            CatalogMemberCorrespondencePlan.Create(
                secondSource,
                Method(secondSource, image, owner, [], owner));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [firstSource, secondSource],
            first.Requests.Concat(second.Requests));

        var firstIssued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                first.Project(context));
        var secondIssued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                second.Project(context));

        Assert.Equal(firstIssued.Key, secondIssued.Key);
        Assert.Equal(
            CatalogMemberCorrespondenceKind.Indeterminate,
            firstIssued.Key.Kind);
        var evidence = Assert.Single(
            firstIssued.Evidence.OfType<
                MemberCorrespondenceEvidence.DuplicateArtifact>());
        Assert.Equal(2, evidence.Evidence.Candidates.Length);
    }

    [Fact]
    public void GenericMemberWithoutOpenSignature_IsIncomplete()
    {
        byte[] image = BuildAssembly("Owner", ["Owner", "Arg"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        TypeRef argument = ReadDefinition(image, "Arg");
        var member = new MemberRef(
            TypeRef.GenericInstance(owner, [argument]),
            "M",
            [argument],
            argument,
            MemberKind.Method);

        CatalogMemberCorrespondencePlan plan =
            CatalogMemberCorrespondencePlan.Create(source, member);
        using TypeResolutionContext context =
            TypeResolutionContext.Create(
                MissingPolicy.Instance,
                [source],
                []);

        var incomplete = Assert.IsType<
            CatalogMemberJoinProjection.Incomplete>(
                plan.Project(context));
        Assert.Contains(
            incomplete.Failures,
            failure => failure
                is MemberCorrespondenceFailure
                    .OpenSignatureUnavailable);

        var partial = member with
        {
            OpenReturnType = argument,
        };
        CatalogMemberCorrespondencePlan partialPlan =
            CatalogMemberCorrespondencePlan.Create(source, partial);
        AssertFailure<
            MemberCorrespondenceFailure.OpenSignatureUnavailable>(
                partialPlan,
                context);

        var truncated = member with
        {
            ParameterTypes = [argument, argument],
            OpenParameterTypes = [TypeRef.GenericParameter(0)],
            OpenReturnType = TypeRef.GenericParameter(0),
        };
        CatalogMemberCorrespondencePlan truncatedPlan =
            CatalogMemberCorrespondencePlan.Create(source, truncated);
        AssertFailure<
            MemberCorrespondenceFailure.OpenSignatureUnavailable>(
                truncatedPlan,
                context);
    }

    [Fact]
    public void InstanceAndStaticMembers_HaveDifferentKeys()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        MethodIdentity staticMethod =
            Method(source, image, owner, [], owner);
        MethodIdentity instanceMethod = staticMethod with
        {
            IsStatic = false,
        };
        CatalogMemberCorrespondencePlan staticPlan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                staticMethod);
        CatalogMemberCorrespondencePlan instancePlan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                instanceMethod);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            staticPlan.Requests
                .Concat(instancePlan.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey staticKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                staticPlan.Project(context)).Key;
        CatalogMemberJoinKey instanceKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                instancePlan.Project(context)).Key;

        Assert.False(staticKey.HasThis);
        Assert.True(instanceKey.HasThis);
        Assert.NotEqual(staticKey, instanceKey);
    }

    [Fact]
    public void MemberKindAndCallingConvention_AreIdentityBearing()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        var method = new MemberRef(
            owner,
            "M",
            [],
            owner,
            MemberKind.Method);
        MemberRef functionPointer = method with
        {
            Kind = MemberKind.FunctionPointer,
        };
        MemberRef vararg = method with
        {
            SignatureHeader = 0x05,
        };
        CatalogMemberCorrespondencePlan methodPlan =
            CatalogMemberCorrespondencePlan.Create(source, method);
        CatalogMemberCorrespondencePlan functionPointerPlan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                functionPointer);
        CatalogMemberCorrespondencePlan varargPlan =
            CatalogMemberCorrespondencePlan.Create(source, vararg);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            methodPlan.Requests
                .Concat(functionPointerPlan.Requests)
                .Concat(varargPlan.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey methodKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                methodPlan.Project(context)).Key;
        CatalogMemberJoinKey functionPointerKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                functionPointerPlan.Project(context)).Key;
        CatalogMemberJoinKey varargKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                varargPlan.Project(context)).Key;

        Assert.Equal(MemberKind.Method, methodKey.MemberKind);
        Assert.Equal(MemberKind.FunctionPointer, functionPointerKey.MemberKind);
        Assert.Equal(0x05, varargKey.SignatureHeader);
        Assert.NotEqual(methodKey, functionPointerKey);
        Assert.NotEqual(methodKey, varargKey);
    }

    [Fact]
    public void CompilerProducedVararg_PreservesCallingConvention()
    {
        string assemblyPath =
            typeof(CatalogMemberCorrespondencePlanTests).Assembly.Location;
        int token = typeof(CatalogMemberCorrespondencePlanTests)
            .GetMethod(
                nameof(VarargFixture),
                BindingFlags.NonPublic | BindingFlags.Static)!
            .MetadataToken;
        LibraryBodyIndex index = LibraryBodyIndex.Open(assemblyPath);
        MethodIdentity member = Assert.Single(
            index.Methods,
            candidate => candidate.MetadataToken == token);
        byte[] image = File.ReadAllBytes(assemblyPath);
        ResolvedAssemblyReference source = Descriptor(image);
        CatalogMemberCorrespondencePlan plan =
            CatalogMemberCorrespondencePlan.Create(source, member);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            plan.Requests);

        CatalogMemberJoinKey key = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                plan.Project(context)).Key;

        Assert.Equal(0x05, member.SignatureHeader & 0x0F);
        Assert.Equal(0x05, key.SignatureHeader & 0x0F);
    }

    [Fact]
    public void ModifierPayload_PreservesEveryNamedType()
    {
        byte[] image = BuildAssembly("Owner", ["Owner", "Modifier", "Arg"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        TypeRef modifier = ReadDefinition(image, "Modifier");
        TypeRef argument = ReadDefinition(image, "Arg");
        TypeRef modified = TypeRef.UnsupportedModified(
            modifier,
            TypeRef.ByRef(argument),
            isRequired: true);
        CatalogMemberCorrespondencePlan requiredPlan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [modified], owner));
        CatalogMemberCorrespondencePlan optionalPlan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(
                    source,
                    image,
                    owner,
                    [
                        TypeRef.UnsupportedModified(
                            modifier,
                            TypeRef.ByRef(argument),
                            isRequired: false),
                    ],
                    owner));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            requiredPlan.Requests
                .Concat(optionalPlan.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey requiredKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                requiredPlan.Project(context)).Key;
        CatalogMemberJoinKey optionalKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                optionalPlan.Project(context)).Key;

        Assert.Equal(3, requiredPlan.Requests.Length);
        Assert.Equal(
            CatalogTypeShapeKind.Modified,
            requiredKey.ParameterTypes[0].Kind);
        Assert.True(requiredKey.ParameterTypes[0].IsRequiredModifier);
        Assert.Equal(
            CatalogTypeShapeKind.ByRef,
            requiredKey.ParameterTypes[0].ElementType!.Kind);
        Assert.NotEqual(requiredKey, optionalKey);
    }

    [Fact]
    public void FunctionPointerPayload_IsRecursivelyIdentityBearing()
    {
        byte[] image = BuildAssembly("Owner", ["Owner", "Arg"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        TypeRef argument = ReadDefinition(image, "Arg");
        var cdecl = new MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.CDecl,
                SignatureAttributes.None),
            argument,
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [owner]);
        var stdcall = new MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.StdCall,
                SignatureAttributes.None),
            argument,
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [owner]);
        CatalogMemberCorrespondencePlan first =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(
                    source,
                    image,
                    owner,
                    [TypeRef.UnsupportedFunctionPointer(cdecl)],
                    owner));
        CatalogMemberCorrespondencePlan second =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(
                    source,
                    image,
                    owner,
                    [TypeRef.UnsupportedFunctionPointer(stdcall)],
                    owner));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            first.Requests
                .Concat(second.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance));

        CatalogMemberJoinKey firstKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                first.Project(context)).Key;
        CatalogMemberJoinKey secondKey = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                second.Project(context)).Key;

        Assert.Equal(
            CatalogTypeShapeKind.FunctionPointer,
            firstKey.ParameterTypes[0].Kind);
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void MissingManifestRequest_IsActionableExpansionFailure()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        CatalogMemberCorrespondencePlan plan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [], owner));
        using TypeResolutionContext context =
            TypeResolutionContext.Create(
                MissingPolicy.Instance,
                [source],
                []);

        var incomplete = Assert.IsType<
            CatalogMemberJoinProjection.Incomplete>(
                plan.Project(context));

        Assert.Single(incomplete.Failures);
        Assert.IsType<MemberCorrespondenceFailure.ExpansionRequired>(
            incomplete.Failures[0]);
    }

    [Fact]
    public void AdvancedCatalog_MakesPriorPlanProjectionStale()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        CatalogMemberCorrespondencePlan plan =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [], owner));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext oldContext = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            plan.Requests);
        using TypeResolutionContext currentContext = catalog.CreateContext(
            MissingPolicy.Instance,
            [source],
            plan.Requests);

        var incomplete = Assert.IsType<
            CatalogMemberJoinProjection.Incomplete>(
                plan.Project(oldContext));

        Assert.Single(incomplete.Failures);
        Assert.IsType<MemberCorrespondenceFailure.StaleGeneration>(
            incomplete.Failures[0]);
        Assert.IsType<CatalogMemberJoinProjection.Issued>(
            plan.Project(currentContext));
    }

    [Fact]
    public void StructuralAndResolutionFailures_CannotIssueKeys()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference source = Descriptor(image);
        TypeRef owner = ReadDefinition(image, "Owner");
        using TypeResolutionContext emptyContext =
            TypeResolutionContext.Create(
                MissingPolicy.Instance,
                [source],
                []);

        CatalogMemberCorrespondencePlan sourceMismatch =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [], owner) with
                {
                    AssemblyName = "Other",
                });
        AssertFailure<MemberCorrespondenceFailure.SourceMismatch>(
            sourceMismatch,
            emptyContext);

        CatalogMemberCorrespondencePlan missingProvenance =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(
                    source,
                    image,
                    owner,
                    [TypeRef.Definition("Owner", "N", "Synthetic")],
                    owner));
        AssertFailure<
            MemberCorrespondenceFailure.MissingResolutionProvenance>(
                missingProvenance,
                emptyContext);

        CatalogMemberCorrespondencePlan unsupported =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(
                    source,
                    image,
                    owner,
                    [TypeRef.Unsupported("unknown signature")],
                    owner));
        AssertFailure<
            MemberCorrespondenceFailure.UnsupportedTypeShape>(
                unsupported,
                emptyContext);

        CatalogMemberCorrespondencePlan malformed =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(
                    source,
                    image,
                    owner,
                    [TypeRef.MdArray(owner, rank: 0)],
                    owner));
        AssertFailure<MemberCorrespondenceFailure.MalformedTypeShape>(
            malformed,
            emptyContext);

        TypeRef deep = owner;
        for (int i = 0; i < 256; i++)
            deep = TypeRef.SzArray(deep);
        CatalogMemberCorrespondencePlan overDepth =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [deep], owner));
        AssertFailure<MemberCorrespondenceFailure.ShapeDepthExceeded>(
            overDepth,
            emptyContext);

        TypeRef absent = TypeRef.Definition(
            source.Identity.Name,
            "N",
            "Absent",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                TypeName("Absent")));
        CatalogMemberCorrespondencePlan notFound =
            CatalogMemberCorrespondencePlan.Create(
                source,
                Method(source, image, owner, [absent], owner));
        using TypeResolutionContext completeContext =
            TypeResolutionContext.Create(
                MissingPolicy.Instance,
                [source],
                notFound.Requests);
        AssertFailure<MemberCorrespondenceFailure.Resolution>(
            notFound,
            completeContext);
    }

    [Fact]
    public void RequestComparer_UsesManifestCoordinates()
    {
        byte[] image = BuildAssembly("Owner", ["Owner"]);
        ResolvedAssemblyReference first = Descriptor(image);
        ResolvedAssemblyReference second = Descriptor(image);
        MetadataTypeDefinitionName name = TypeName("Owner");
        TypeResolutionRequest firstRequest =
            TypeResolutionRequest.FromAssembly(
                first,
                AssemblyResolutionScope.Any,
                name);
        TypeResolutionRequest equivalent =
            TypeResolutionRequest.FromAssembly(
                first,
                AssemblyResolutionScope.Any,
                name);
        TypeResolutionRequest otherRegistration =
            TypeResolutionRequest.FromAssembly(
                second,
                AssemblyResolutionScope.Any,
                name);

        Assert.True(
            TypeResolutionRequestComparer.Instance.Equals(
                firstRequest,
                equivalent));
        Assert.Equal(
            TypeResolutionRequestComparer.Instance.GetHashCode(firstRequest),
            TypeResolutionRequestComparer.Instance.GetHashCode(equivalent));
        Assert.False(
            TypeResolutionRequestComparer.Instance.Equals(
                firstRequest,
                otherRegistration));

        var target = new AssemblyReferenceIdentity(
            "Target",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        TypeResolutionRequest globalReference =
            TypeResolutionRequest.FromReference(
                target,
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Any,
                name);
        TypeResolutionRequest equivalentGlobalReference =
            TypeResolutionRequest.FromReference(
                target,
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Any,
                name);
        Assert.True(
            TypeResolutionRequestComparer.Instance.Equals(
                globalReference,
                equivalentGlobalReference));

        TypeResolutionRequest firstCore =
            TypeResolutionRequest.FromCoreLibrary(
                first,
                AssemblyResolutionScope.Platform,
                name);
        TypeResolutionRequest equivalentCore =
            TypeResolutionRequest.FromCoreLibrary(
                first,
                AssemblyResolutionScope.Platform,
                name);
        TypeResolutionRequest secondCore =
            TypeResolutionRequest.FromCoreLibrary(
                second,
                AssemblyResolutionScope.Platform,
                name);
        Assert.True(
            TypeResolutionRequestComparer.Instance.Equals(
                firstCore,
                equivalentCore));
        Assert.False(
            TypeResolutionRequestComparer.Instance.Equals(
                firstCore,
                secondCore));

        TypeResolutionRequest firstModule =
            TypeResolutionRequest.FromModule(
                first,
                "Part.netmodule",
                name);
        TypeResolutionRequest equivalentModule =
            TypeResolutionRequest.FromModule(
                first,
                "Part.netmodule",
                name);
        TypeResolutionRequest otherModule =
            TypeResolutionRequest.FromModule(
                first,
                "Other.netmodule",
                name);
        Assert.True(
            TypeResolutionRequestComparer.Instance.Equals(
                firstModule,
                equivalentModule));
        Assert.False(
            TypeResolutionRequestComparer.Instance.Equals(
                firstModule,
                otherModule));
    }

    [Fact]
    public void CorrespondenceCurrency_CannotBeExternallyForgedOrExtended()
    {
        Assert.Empty(
            typeof(CatalogTypeShape).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(CatalogMemberJoinKey).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(CatalogMemberCorrespondencePlan).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));
        AssertClosedResult(typeof(MemberCorrespondenceEvidence));
        AssertClosedResult(typeof(MemberCorrespondenceFailure));
        AssertClosedResult(typeof(CatalogMemberJoinProjection));
    }

    static void AssertClosedResult(Type result)
    {
        ConstructorInfo constructor = Assert.Single(
            result.GetConstructors(
                BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.True(constructor.IsFamilyAndAssembly);
        Assert.All(
            result.GetNestedTypes(),
            arm => Assert.Empty(
                arm.GetConstructors(
                    BindingFlags.Public | BindingFlags.Instance)));
    }

    static void AssertFailure<TFailure>(
        CatalogMemberCorrespondencePlan plan,
        TypeResolutionContext context)
        where TFailure : MemberCorrespondenceFailure
    {
        var incomplete = Assert.IsType<
            CatalogMemberJoinProjection.Incomplete>(
                plan.Project(context));
        Assert.Contains(
            incomplete.Failures,
            failure => failure is TFailure);
    }

    static MethodIdentity Method(
        ResolvedAssemblyReference source,
        byte[] image,
        TypeRef declaringType,
        ImmutableArray<TypeRef> parameters,
        TypeRef returnType,
        int genericArity = 0) =>
        new(
            source.Identity.Name,
            ReadMvid(image),
            declaringType,
            "M",
            parameters,
            returnType,
            0x06000001,
            IsStatic: true,
            GenericArity: genericArity);

    static void VarargFixture(__arglist)
    {
    }

    static byte[] BuildAssembly(
        string assemblyName,
        IReadOnlyList<string> definitions,
        (AssemblyReferenceIdentity Assembly, string Type)? reference = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        foreach (string definition in definitions)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(definition),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        if (reference is { } external)
        {
            AssemblyReferenceHandle assembly =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString(external.Assembly.Name),
                    external.Assembly.Version
                        ?? new Version(0, 0, 0, 0),
                    external.Assembly.Culture is null
                        ? default
                        : metadata.GetOrAddString(
                            external.Assembly.Culture),
                    external.Assembly.PublicKeyToken is null
                        ? default
                        : metadata.GetOrAddBlob(
                            Convert.FromHexString(
                                external.Assembly.PublicKeyToken)),
                    flags: default,
                    hashValue: default);
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(external.Type));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildFacade(
        string assemblyName,
        AssemblyReferenceIdentity target,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle implementation =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(target.Name),
                target.Version ?? new Version(0, 0, 0, 0),
                target.Culture is null
                    ? default
                    : metadata.GetOrAddString(target.Culture),
                target.PublicKeyToken is null
                    ? default
                    : metadata.GetOrAddBlob(
                        Convert.FromHexString(target.PublicKeyToken)),
                flags: default,
                hashValue: default);
        metadata.AddExportedType(
            Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(typeName),
            implementation,
            typeDefinitionId: 0);
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static TypeRef ReadDefinition(byte[] image, string name)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = reader.TypeDefinitions.Single(
            candidate =>
                reader.GetString(
                    reader.GetTypeDefinition(candidate).Name) == name);
        return TypeRefDecoder.Instance.GetTypeFromDefinition(
            reader,
            handle,
            0);
    }

    static TypeRef ReadReference(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return TypeRefDecoder.Instance.GetTypeFromReference(
            pe.GetMetadataReader(),
            MetadataTokens.TypeReferenceHandle(1),
            0);
    }

    static TypeRef ReferencedType(
        AssemblyReferenceIdentity assembly,
        string name) =>
        TypeRef.Definition(
            assembly.Name,
            "N",
            name,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(assembly),
                TypeName(name)));

    static ResolvedAssemblyReference Descriptor(byte[] image) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
                new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local(
                "member-correspondence-test"));

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static Guid ReadMvid(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    static MetadataTypeDefinitionName TypeName(string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", [name])).Name;

    sealed class MissingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingPolicy Instance { get; } = new();
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.NotFound();
    }

    sealed class UnavailablePolicy : IAssemblyBindingPolicy
    {
        internal static UnavailablePolicy Instance { get; } = new();
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.IdentityPolicyRequired));
    }

    sealed class ExactPolicy : IAssemblyBindingPolicy
    {
        readonly ImmutableDictionary<
            AssemblyReferenceIdentity,
            ResolvedAssemblyReference> _assemblies;

        internal ExactPolicy(
            IEnumerable<ResolvedAssemblyReference> assemblies) =>
            _assemblies = assemblies.ToImmutableDictionary(
                assembly => assembly.Identity);

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            request.Target is AssemblyBindingTarget.AssemblyReference reference
            && _assemblies.TryGetValue(
                reference.Identity,
                out ResolvedAssemblyReference? assembly)
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
    }
}
