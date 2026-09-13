using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class CrossAssemblyMethodFactsTests
{
    [Fact]
    public void CrossAssemblyByRefMemberRef_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseOut), "WriteOut", ArgumentRefKind.Out);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseRef), "Mutate", ArgumentRefKind.Ref);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseIn), "Read", ArgumentRefKind.In);
    }

    [Fact]
    public void VersionDriftedSiblingAssembly_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create(versionDrift: true);
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseExternalOut), "WriteExternalOut", ArgumentRefKind.Out);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseExternalRef), "MutateExternal", ArgumentRefKind.Ref);
        Assert.Contains(
            "ByRefLibrary.WriteExternalOut(out V_0);",
            Print(source, CrossAssemblyFixtureMethods.UseExternalOut));
    }

    [Fact]
    public void GenericInstantiatedSignatureCollision_UsesDefinitionSignature()
    {
        using var fixture = CrossAssemblyFixture.Create(versionDrift: true);
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(source, "UseGenericOut", "GenericCollision", ArgumentRefKind.Out);
        Assert.Contains(
            "ByRefLibrary.GenericCollision<object>(out V_0);",
            Print(source, "UseGenericOut"));
    }

    [Fact]
    public void GenericReturnSignatureCollision_UsesDefinitionReturnType()
    {
        using var fixture = MethodCollisionFixture.Create();
        var objectType = TypeRef.CoreLib("System", "Object");
        var callee = new MethodRef(
            fixture.Type("C"),
            "ReturnCollision",
            objectType,
            [],
            HasThis: false)
        {
            TypeArguments = [objectType],
            DefinitionReturnType = objectType,
        };

        Assert.False(fixture.Resolve(callee).RequiresUnsafe);
    }

    [Fact]
    public void KnownUnsafeFact_DoesNotSuppressInvalidRulesState()
    {
        using var fixture = MethodCollisionFixture.Create(
            rulesVersion: 99);
        var objectType = TypeRef.CoreLib("System", "Object");
        var callee = new MethodRef(
            fixture.Type("C"),
            "ReturnCollision",
            objectType,
            [],
            HasThis: false)
        {
            TypeArguments = [objectType],
            DefinitionReturnType = objectType,
            RequiresUnsafeFact = MetadataFactState.No,
        };

        MethodRef resolved = fixture.Resolve(callee);

        Assert.Equal(
            MemorySafetyRulesState.Unsupported,
            resolved.MemorySafetyRulesState);
        Assert.Equal(
            MetadataFactState.No,
            resolved.RequiresUnsafeFact);
        Assert.False(resolved.MemorySafetyContractUnavailable);
    }

    [Fact]
    public void CustomModifierSignatureCollision_UsesExactModifiers()
    {
        using var fixture = MethodCollisionFixture.Create();
        var intType = TypeRef.CoreLib("System", "Int32");
        var parameter = TypeRef.ByRef(intType)
            .WithCustomModifier(fixture.Type("Marker"), isRequired: false);
        var callee = new MethodRef(
            fixture.Type("C"),
            "ModifierCollision",
            TypeRef.CoreLib("System", "Void"),
            [parameter],
            HasThis: false);

        Assert.False(fixture.Resolve(callee).RequiresUnsafe);
    }

    [Fact]
    public void TypeSpecCustomModifierSignatureCollision_UsesExactModifiers()
    {
        using var fixture = MethodCollisionFixture.Create();
        var intType = TypeRef.CoreLib("System", "Int32");
        var modifier = TypeRef.GenericInstance(
            fixture.Type("Marker`1"),
            [intType]);
        var parameter = TypeRef.ByRef(intType)
            .WithCustomModifier(modifier, isRequired: false);
        var callee = new MethodRef(
            fixture.Type("C"),
            "TypeSpecModifierCollision",
            TypeRef.CoreLib("System", "Void"),
            [parameter],
            HasThis: false);

        Assert.True(fixture.Resolve(callee).RequiresUnsafe);
    }

    [Fact]
    public void GenericTypeSpecCustomModifier_IsInstantiated()
    {
        using var fixture = MethodCollisionFixture.Create();
        var stringType = TypeRef.CoreLib("System", "String");
        var modifier = TypeRef.GenericInstance(
            fixture.Type("Marker`1"),
            [TypeRef.GenericParameter(0, "T")]);
        var modified = TypeRef.CoreLib("System", "Int32")
            .WithCustomModifier(modifier, isRequired: true);

        TypeRef instantiated = modified.Instantiate(
            [stringType],
            []);

        var retained = Assert.Single(instantiated.CustomModifiers);
        Assert.True(retained.IsRequired);
        Assert.Equal(
            stringType,
            Assert.Single(retained.Modifier.TypeArguments));
    }

    [Fact]
    public void DefinitionFactStamping_PreservesCustomModifiers()
    {
        var modifier = TypeRef.CoreLib(
            "System.Runtime.InteropServices",
            "OutAttribute");
        TypeRef modified = TypeRef.Definition(
                "External",
                "N",
                "C")
            .WithCustomModifier(
                modifier,
                isRequired: true);

        TypeRef withHint = modified.WithValueTypeHint(
            ValueTypeHint.ReferenceType);
        TypeRef withInlineArray = modified.WithInlineArrayFact(
            MetadataFactState.No);

        Assert.Equal(
            modified.CustomModifiers,
            withHint.CustomModifiers);
        Assert.Equal(
            modified.CustomModifiers,
            withInlineArray.CustomModifiers);
    }

    [Fact]
    public void FunctionPointerInstantiation_RecomputesParameterRefKinds()
    {
        var parameter = TypeRef.ByRef(
                TypeRef.CoreLib("System", "Int32"))
            .WithCustomModifier(
                TypeRef.GenericParameter(0, "T"),
                isRequired: true);
        TypeRef pointer = TypeRef.FunctionPointer(
            TypeRef.CoreLib("System", "Void"),
            [parameter],
            "");

        TypeRef instantiated = pointer.Instantiate(
            [
                TypeRef.CoreLib(
                    "System.Runtime.InteropServices",
                    "OutAttribute"),
            ],
            []);

        Assert.Equal(
            [ArgumentRefKind.Out],
            instantiated.FunctionPointerParameterRefKinds);
    }

    /// <summary>
    /// The <c>SuppressGCTransition</c> suffix is derived from a custom modifier
    /// the return type keeps, so re-deriving the convention during substitution
    /// must not spell it twice. <see cref="TypeRef.Equals"/> includes the
    /// calling convention, so a doubled suffix makes the substituted pointer
    /// unequal to the same closed pointer decoded straight from a signature —
    /// exactly the identity this PR exists to protect. Fails if
    /// <c>AddSuppressGcTransition</c> stops being idempotent.
    /// </summary>
    [Fact]
    public void FunctionPointerInstantiation_DoesNotDoubleSuppressGcTransition()
    {
        TypeRef modifier = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "CallConvSuppressGCTransition");
        TypeRef intType = TypeRef.CoreLib("System", "Int32");
        TypeRef openReturn = TypeRef.GenericParameter(0, "T")
            .WithCustomModifier(modifier, isRequired: false);
        TypeRef open = TypeRef.FunctionPointer(openReturn, [intType], "");

        TypeRef instantiated = open.Instantiate([intType], []);
        TypeRef closed = TypeRef.FunctionPointer(
            intType.WithCustomModifier(modifier, isRequired: false),
            [intType],
            "");

        Assert.Equal("unmanaged[SuppressGCTransition]", closed.CallingConvention);
        Assert.Equal(closed.CallingConvention, instantiated.CallingConvention);
        Assert.Equal(closed, instantiated);
        Assert.Equal(closed.GetHashCode(), instantiated.GetHashCode());
    }

    /// <summary>
    /// Substitution can turn a representable modifier into an unsupported one.
    /// Losing that evidence left a type that looked fully supported, so the
    /// fidelity computation lost the reason it could not be spelled. This is
    /// the <c>InstantiateCustomModifiers</c> path, which rebuilds the modifier
    /// list directly and so never reaches <c>WithCustomModifier</c>: it gates
    /// only the traversal, and fails if <see cref="TypeRef.ContainsUnsupported"/>
    /// or <c>UnsupportedReasons</c> stop looking at <c>CustomModifiers</c>.
    /// Retention through <c>WithCustomModifier</c> is gated separately by
    /// <see cref="SubstitutedModifierOnAGenericParameter_IsNotDiscarded"/>.
    /// </summary>
    [Fact]
    public void SubstitutedUnsupportedModifier_KeepsItsEvidence()
    {
        const string reason = "modifier type is unsupported";
        TypeRef modified = TypeRef.CoreLib("System", "Int32")
            .WithCustomModifier(
                TypeRef.GenericParameter(0, "T"),
                isRequired: true);

        TypeRef instantiated = modified.Instantiate(
            [TypeRef.Unsupported(reason)],
            []);

        Assert.Single(instantiated.CustomModifiers);
        Assert.True(instantiated.ContainsUnsupported);
        Assert.Contains(reason, instantiated.UnsupportedReasons());
    }

    /// <summary>
    /// The same evidence loss, on the path where the modified type is itself
    /// the generic parameter being substituted: the modifiers ride across onto
    /// the substituted type. Dropping an unsupported one here returned the bare
    /// argument, indistinguishable from a type that never carried a modifier.
    /// Fails if <c>WithCustomModifier</c> goes back to discarding it.
    /// </summary>
    [Fact]
    public void SubstitutedModifierOnAGenericParameter_IsNotDiscarded()
    {
        const string reason = "modifier type is unsupported";
        TypeRef intType = TypeRef.CoreLib("System", "Int32");
        TypeRef parameter = TypeRef.GenericParameter(0, "T")
            .WithCustomModifier(
                TypeRef.GenericParameter(1, "U"),
                isRequired: true);

        TypeRef instantiated = parameter.Instantiate(
            [intType, TypeRef.Unsupported(reason)],
            []);

        Assert.NotSame(intType, instantiated);
        Assert.Single(instantiated.CustomModifiers);
        Assert.True(instantiated.ContainsUnsupported);
        Assert.Contains(reason, instantiated.UnsupportedReasons());
    }

    [Fact]
    public void AmbiguousMethodCandidates_KeepFactsUnknown()
    {
        using var fixture = MethodCollisionFixture.Create();
        var callee = new MethodRef(
            fixture.Type("C"),
            "DuplicateCollision",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: false);

        Assert.False(fixture.Resolve(callee).RequiresUnsafe);
    }

    [Fact]
    public void PlatformForwardedByRefMemberRef_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseUri), "TryCreate");
        Assert.Equal(ParameterRefKindFacts.Known, call.Callee.ParameterRefKindsFacts);
        Assert.Collection(
            call.Callee.ParameterRefKinds,
            kind => Assert.Equal(ArgumentRefKind.Value, kind),
            kind => Assert.Equal(ArgumentRefKind.Value, kind),
            kind => Assert.Equal(ArgumentRefKind.Out, kind));
    }

    [Fact]
    public void CrossAssemblyGeneratedMemberRef_RecoversCompilerGeneratedFacts()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseGenerated), "Run");

        Assert.Equal(MetadataFactState.Yes, call.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Yes, call.Callee.CompilerGenerated);
    }

    [Fact]
    public void CrossAssemblyDelegateConstructor_RecoversDelegateTypeFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var newObject = SingleNewObject(source, nameof(CrossAssemblyFixtureMethods.UseExternalDelegate));

        Assert.Equal(MetadataFactState.Yes, newObject.Constructor.DeclaringTypeIsDelegate);
    }

    [Fact]
    public void CrossAssemblyOperatorMemberRef_RecoversOperatorFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator), "op_Addition");

        Assert.Equal(MetadataFactState.Yes, call.Callee.IsOperator);
    }

    [Fact]
    public void CrossAssemblyOperatorNameLookalike_RecoversNotOperatorFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var addition = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition), "op_Addition");
        var conversion = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit), "op_Implicit");

        Assert.Equal(MetadataFactState.No, addition.Callee.IsOperator);
        Assert.Equal(MetadataFactState.No, conversion.Callee.IsOperator);
    }

    [Fact]
    public void CrossAssemblyOperatorNameLookalike_RendersMethodCall()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        string addition = Print(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition));
        string conversion = Print(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit));

        Assert.Contains(".op_Addition(left, right)", addition);
        Assert.DoesNotContain("left + right", addition);
        Assert.Contains(".op_Implicit(value)", conversion);
        Assert.DoesNotContain("return (int)value;", conversion);
    }

    [Fact]
    public void CrossAssemblyOperatorMemberRef_RendersOperator()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        string output = Print(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator));

        Assert.Contains("left + right", output);
        Assert.DoesNotContain("op_Addition", output);
    }

    [Fact]
    public void CrossAssemblyPropertyAccessorMemberRef_RecoversAccessorKind()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseProperty), "get_Count");

        Assert.Equal(AccessorKind.PropertyGet, call.Callee.AccessorKind);
    }

    [Fact]
    public void CrossAssemblyPropertyAccessorMemberRef_RendersPropertyAccess()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var function = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseProperty));
        var result = CSharpPrinter.PrintRaised(function, out _);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("library.Count", result.Output);
        Assert.DoesNotContain("get_Count", result.Output);
    }

    [Fact]
    public void VersionUnifiedSignatureType_RecoversAccessorKind()
    {
        using var fixture = VersionUnifiedSignatureFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(source, "UseProperty", "get_Value");
        string output = PrintRaised(source, "UseProperty");

        Assert.Equal(AccessorKind.PropertyGet, call.Callee.AccessorKind);
        Assert.Contains("library.Value", output);
        Assert.DoesNotContain("get_Value", output);
    }

    [Fact]
    public void DistinctSignatureTypeDefinitions_DoNotCorrespond()
    {
        using var fixture = VersionUnifiedSignatureFixture.Create();
        ResolvedAssemblyReference version2 = fixture.SignatureV2;
        ResolvedAssemblyReference version3 = fixture.SignatureV3;
        var version2Request = TypeResolutionRequest.FromAssembly(
            version2,
            AssemblyResolutionScope.Any,
            fixture.PayloadName);
        var version3Request = TypeResolutionRequest.FromAssembly(
            version3,
            AssemblyResolutionScope.Any,
            fixture.PayloadName);
        using var context = new MetadataContext(
            TestAssemblyReferenceResolvers.None);

        Assert.False(
            context.ResolveToSameDefinition(
                version2,
                version2Request,
                version3,
                version3Request));
    }

    [Fact]
    public async Task ConcurrentResolution_DoesNotInvalidateDefinitionCorrespondence()
    {
        using var fixture = VersionUnifiedSignatureFixture.Create();
        ResolvedAssemblyReference root = fixture.SignatureV2;
        var request = TypeResolutionRequest.FromAssembly(
            root,
            AssemblyResolutionScope.Any,
            fixture.PayloadName);
        using var context = new MetadataContext(
            TestAssemblyReferenceResolvers.None);
        using var cancellation = new CancellationTokenSource();
        Task churn = Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                while (!cancellation.IsCancellationRequested)
                    context.Resolve(root, request);
            })));

        int falseResults = 0;
        try
        {
            for (int i = 0; i < 50_000; i++)
            {
                if (!context.ResolveToSameDefinition(
                        root,
                        request,
                        root,
                        request))
                {
                    falseResults++;
                }
            }
        }
        finally
        {
            cancellation.Cancel();
            await churn;
        }

        Assert.Equal(0, falseResults);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MethodFactCache_DistinguishesSignatureResolutionIdentity(
        bool correspondingFirst)
    {
        using var fixture = VersionUnifiedSignatureFixture.Create();
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            externalPdbPath: null,
            fixture.CreateResolver());
        MethodRef corresponding = fixture.Getter(
            fixture.SignatureV2.Identity);
        MethodRef distinct = fixture.Getter(
            fixture.SignatureV3.Identity);

        MethodRef first = source.CrossAssembly.Upgrade(
            correspondingFirst ? corresponding : distinct,
            resolveRequiresUnsafe: false);
        MethodRef second = source.CrossAssembly.Upgrade(
            correspondingFirst ? distinct : corresponding,
            resolveRequiresUnsafe: false);

        MethodRef correspondingResult =
            correspondingFirst ? first : second;
        MethodRef distinctResult =
            correspondingFirst ? second : first;
        Assert.Equal(
            AccessorKind.PropertyGet,
            correspondingResult.AccessorKind);
        Assert.Equal(AccessorKind.Unknown, distinctResult.AccessorKind);
    }

    [Fact]
    public void CrossAssemblyDynamicReturns_PreserveReferenceIdentity()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var property = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseDynamicProperty), "get_DynamicValue");
        var method = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseDynamicMethod), "GetDynamicValue");
        var byRefMethod = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseByRefDynamicMethod), "GetDynamicReference");
        var byRefObjectMethod = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseByRefObjectMethod), "GetObjectReference");
        Assert.Equal(MetadataFactState.Yes, property.Callee.ReturnIsDynamic);
        Assert.Equal(MetadataFactState.Yes, method.Callee.ReturnIsDynamic);
        Assert.Equal(MetadataFactState.Yes, byRefMethod.Callee.ReturnIsDynamic);
        Assert.Equal(MetadataFactState.No, byRefObjectMethod.Callee.ReturnIsDynamic);

        Assert.Contains("(object)library.DynamicValue == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicProperty));
        Assert.Contains("(object)library.GetDynamicValue() == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicMethod));
        Assert.Contains("(object)(library.GetDynamicReference()) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefDynamicMethod));
        Assert.Contains("(object)(library.DynamicReference) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefDynamicProperty));
        Assert.Contains("(object)(library.Reference) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseGenericByRefDynamicProperty));
        Assert.DoesNotContain("(object)", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefObjectMethod));
    }

    [Fact]
    public void CrossAssemblyDynamicFields_PreserveReferenceIdentity()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var direct = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseDynamicField), "DynamicField");
        var generic = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseGenericDynamicField), "Value");
        var plainObject = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseObjectField), "ObjectField");
        var byRefDynamic = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseByRefDynamicField), "DynamicField");
        var byRefObject = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseByRefObjectField), "ObjectField");

        Assert.Equal(MetadataFactState.Yes, direct.Field.DynamicFact);
        Assert.Equal(MetadataFactState.Unknown, generic.Field.DynamicFact);
        Assert.Equal(MetadataFactState.No, plainObject.Field.DynamicFact);
        Assert.Equal(MetadataFactState.Yes, byRefDynamic.Field.DynamicFact);
        Assert.Equal(MetadataFactState.No, byRefObject.Field.DynamicFact);
        Assert.Contains("(object)library.DynamicField == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicField));
        Assert.Contains("(object)library.Value == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseGenericDynamicField));
        Assert.Contains("return library.ObjectField == right;", PrintRaised(source, CrossAssemblyFixtureMethods.UseObjectField));
        Assert.Contains("(object)(library.DynamicField) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefDynamicField));
        Assert.DoesNotContain("(object)", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefObjectField));
    }

    [Fact]
    public void MissingCrossAssemblyDynamicFacts_DeclineConservatively()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            null,
            TestAssemblyReferenceResolvers.None);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseDynamicMethod), "GetDynamicValue");
        Assert.Equal(MetadataFactState.Unknown, call.Callee.ReturnIsDynamic);
        Assert.Contains("(object)library.GetDynamicValue() == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicMethod));
    }

    [Fact]
    public void MissingCrossAssemblyFieldFacts_DeclineConservatively()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            null,
            TestAssemblyReferenceResolvers.None);

        var field = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseDynamicField), "DynamicField");
        Assert.Equal(MetadataFactState.Unknown, field.Field.DynamicFact);
        Assert.Contains("(object)library.DynamicField == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicField));
        Assert.Contains("(object)new ExternalReference() == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseExternalNewObject));
    }

    [Fact]
    public void CrossAssemblyInlineArrayHelper_RecoversInlineArrayTypeArgumentFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseExternalInlineArray), "InlineArrayAsSpan");

        Assert.Equal(MetadataFactState.Yes, call.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Yes, call.Callee.TypeArguments[0].DeclaredInlineArray);
        Assert.True(MemberIdentity.IsInlineArraySpanConversionHelper(call, out var arrayType));
        Assert.Equal(MetadataFactState.Yes, arrayType.DeclaredInlineArray);
    }

    [Fact]
    public void MissingCrossAssemblyMetadata_KeepsFactsUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.None);

        var byRef = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOut), "WriteOut");
        Assert.Equal(ParameterRefKindFacts.Unknown, byRef.Callee.ParameterRefKindsFacts);
        Assert.Empty(byRef.Callee.ParameterRefKinds);

        var generated = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseGenerated), "Run");
        Assert.Equal(MetadataFactState.Unknown, generated.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Unknown, generated.Callee.CompilerGenerated);

        var externalDelegate = SingleNewObject(source, nameof(CrossAssemblyFixtureMethods.UseExternalDelegate));
        Assert.Equal(MetadataFactState.Unknown, externalDelegate.Constructor.DeclaringTypeIsDelegate);

        var operatorLike = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition), "op_Addition");
        Assert.Equal(MetadataFactState.Unknown, operatorLike.Callee.IsOperator);
    }

    [Fact]
    public void MissingCrossAssemblyAccessorMetadata_KeepsAccessorFactUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.None);

        var function = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseProperty));
        var call = Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == "get_Count");

        Assert.Equal(AccessorKind.Unknown, call.Callee.AccessorKind);
        Assert.True(call.Callee.IsSpecialNameInferred);
    }

    [Fact]
    public void CrossAssemblyRefStruct_RecoveredIntoByRefLikeTypes()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        // GPT review round 5 (#3124): a `ref struct` defined in a REFERENCED
        // assembly resolves to a ValueType shape but carries no same-assembly
        // by-ref-like fact, so the value-type-arm gate would raise `T t` over it —
        // invalid C# (CS8121). The [IsByRefLike] fact is now resolved through the
        // cross-assembly resolver and recovered into ByRefLikeTypes; the
        // same-assembly value struct (ExternalNumber) stays out.
        var refStructUser = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseExternalRefStruct));
        Assert.Contains(refStructUser.ByRefLikeTypes, t => t.Name == "ExternalRefStruct");

        var structUser = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseExternalStruct));
        Assert.DoesNotContain(structUser.ByRefLikeTypes, t => t.Name == "ExternalNumber");
    }

    [Fact]
    public void MissingCrossAssemblyRefStructMetadata_KeepsByRefLikeUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.None);

        // With the defining assembly outside the reference closure the fact cannot
        // be resolved, so the referenced ref struct is absent from ByRefLikeTypes —
        // fail visible, not a wrong-positive.
        var refStructUser = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseExternalRefStruct));
        Assert.DoesNotContain(refStructUser.ByRefLikeTypes, t => t.Name == "ExternalRefStruct");
    }

    static string Print(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        return CSharpPrinter.Print(function).Output ?? "";
    }

    static string PrintRaised(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        IrPasses.Run(function);
        return CSharpPrinter.Print(function).Output ?? "";
    }

    static void AssertCallRefKind(MetadataSource source, string methodName, string calleeName, ArgumentRefKind expected)
    {
        var call = SingleCall(source, methodName, calleeName);
        Assert.Equal(ParameterRefKindFacts.Known, call.Callee.ParameterRefKindsFacts);
        Assert.Equal(expected, Assert.Single(call.Callee.ParameterRefKinds));
    }

    static Call SingleCall(MetadataSource source, string methodName, string calleeName)
    {
        var function = ImportFunction(source, methodName);
        return Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == calleeName);
    }

    static NewObject SingleNewObject(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        return Assert.Single(function.Descendants.OfType<NewObject>());
    }

    static LoadField SingleField(MetadataSource source, string methodName, string fieldName)
    {
        var function = ImportFunction(source, methodName);
        return Assert.Single(function.Descendants.OfType<LoadField>(), field => field.Field.Name == fieldName);
    }

    static IrFunction ImportFunction(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, "ExternalFacts.Consumer", methodName);
        Assert.NotNull(function);
        function.CheckInvariant();
        return function!;
    }

    static string Emit(
        string directory,
        string assemblyName,
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
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

        string path = Path.Combine(directory, assemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.True(
            result.Success,
            "fixture compilation failed:\n"
                + string.Join(
                    "\n",
                    result.Diagnostics.Select(
                        diagnostic =>
                            $"{diagnostic.Id}: {diagnostic.GetMessage()}")));
        return path;
    }

    sealed class MethodCollisionFixture : IAssemblyReferenceResolver, IDisposable
    {
        readonly string _directory;
        readonly string _library;
        readonly IAssemblyReferenceResolver _runtime =
            TestAssemblyReferenceResolvers.RuntimeAssemblies();

        MethodCollisionFixture(string directory, string library)
        {
            _directory = directory;
            _library = library;
        }

        public static MethodCollisionFixture Create(
            int rulesVersion = 2)
        {
            string directory = Directory.CreateTempSubdirectory(
                "dotnet-inspect-method-collisions-").FullName;
            string library = Path.Combine(
                directory,
                "MethodCollisionLib.dll");
            File.WriteAllBytes(
                library,
                BuildMethodCollisionLibrary(rulesVersion));
            return new MethodCollisionFixture(directory, library);
        }

        public TypeRef Type(string name)
        {
            var definitionName = MetadataTypeDefinitionName.Create("", [name]) switch
            {
                MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
                _ => throw new InvalidOperationException(
                    "collision fixture metadata name is invalid"),
            };
            return TypeRef.DefinitionWithResolution(
                "MethodCollisionLib",
                "",
                name,
                ValueTypeHint.ReferenceType,
                MetadataFactState.Unknown,
                null,
                definitionName,
                new AssemblyReferenceIdentity(
                    "MethodCollisionLib",
                    new Version(1, 0, 0, 0),
                    null,
                    null));
        }

        public MethodRef Resolve(MethodRef callee)
        {
            using var context = new MetadataContext(this);
            using var source = MetadataSource.OpenWithoutSymbols(
                typeof(CrossAssemblyMethodFactsTests).Assembly.Location,
                this,
                context);
            return source.CrossAssembly.Upgrade(
                callee,
                resolveRequiresUnsafe: true);
        }

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => identity.Name == "MethodCollisionLib"
                ? ResolvedAssemblyReference.CreateFromPath(
                    _library,
                    AssemblyResolutionProvenance.Local(
                        "CrossAssemblyMethodFactsTests"))
                : _runtime.Resolve(identity, scope);

        public void Dispose()
            => Directory.Delete(_directory, recursive: true);

        static byte[] BuildMethodCollisionLibrary(int rulesVersion)
        {
            var metadata = new MetadataBuilder();
            ModuleDefinitionHandle module = metadata.AddModule(
                0,
                metadata.GetOrAddString("MethodCollisionLib.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("MethodCollisionLib"),
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
            var requiresUnsafe = metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Diagnostics.CodeAnalysis"),
                metadata.GetOrAddString("RequiresUnsafeAttribute"));
            var memorySafetyRules = metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("MemorySafetyRulesAttribute"));
            var attributeConstructor = metadata.AddMemberReference(
                requiresUnsafe,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
            var rulesConstructor = metadata.AddMemberReference(
                memorySafetyRules,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x01, 0x01, 0x08 }));
            metadata.AddTypeDefinition(
                default,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var marker = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("Marker"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var genericMarker = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("Marker`1"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddGenericParameter(
                genericMarker,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            var otherGenericMarker = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("OtherMarker`1"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddGenericParameter(
                otherGenericMarker,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("C"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var markerOfInt = metadata.AddTypeSpecification(
                GenericInstanceSignature(
                    metadata,
                    genericMarker));
            var otherMarkerOfInt = metadata.AddTypeSpecification(
                GenericInstanceSignature(
                    metadata,
                    otherGenericMarker));
            metadata.AddCustomAttribute(
                module,
                rulesConstructor,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        1, 0,
                        (byte)rulesVersion, 0, 0, 0,
                        0, 0,
                    }));

            var genericReturn = AddMethod(
                metadata,
                "ReturnCollision",
                GenericMethodSignature(
                    metadata,
                    returnType: null));
            metadata.AddGenericParameter(
                genericReturn,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            AddRequiresUnsafe(
                metadata,
                genericReturn,
                attributeConstructor);
            var objectReturn = AddMethod(
                metadata,
                "ReturnCollision",
                GenericMethodSignature(
                    metadata,
                    objectType));
            metadata.AddGenericParameter(
                objectReturn,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);

            var unmodified = AddMethod(
                metadata,
                "ModifierCollision",
                ByRefIntSignature(
                    metadata,
                    modifier: default));
            AddRequiresUnsafe(
                metadata,
                unmodified,
                attributeConstructor);
            AddMethod(
                metadata,
                "ModifierCollision",
                ByRefIntSignature(
                    metadata,
                    marker));

            AddMethod(
                metadata,
                "TypeSpecModifierCollision",
                ByRefIntSignature(
                    metadata,
                    otherMarkerOfInt));
            var typeSpecModified = AddMethod(
                metadata,
                "TypeSpecModifierCollision",
                ByRefIntSignature(
                    metadata,
                    markerOfInt));
            AddRequiresUnsafe(
                metadata,
                typeSpecModified,
                attributeConstructor);

            var duplicate = AddMethod(
                metadata,
                "DuplicateCollision",
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x01 }));
            AddRequiresUnsafe(
                metadata,
                duplicate,
                attributeConstructor);
            AddMethod(
                metadata,
                "DuplicateCollision",
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x01 }));

            var pe = new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            pe.Serialize(image);
            return image.ToArray();
        }

        static MethodDefinitionHandle AddMethod(
            MetadataBuilder metadata,
            string name,
            BlobHandle signature)
            => metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));

        static void AddRequiresUnsafe(
            MetadataBuilder metadata,
            MethodDefinitionHandle method,
            MemberReferenceHandle constructor)
            => metadata.AddCustomAttribute(
                method,
                constructor,
                metadata.GetOrAddBlob(
                    new byte[] { 1, 0, 0, 0 }));

        static BlobHandle GenericMethodSignature(
            MetadataBuilder metadata,
            EntityHandle? returnType)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x10);
            signature.WriteCompressedInteger(1);
            signature.WriteCompressedInteger(0);
            if (returnType is null)
            {
                signature.WriteByte(0x1e);
                signature.WriteCompressedInteger(0);
            }
            else
            {
                WriteClass(signature, returnType.Value);
            }
            return metadata.GetOrAddBlob(signature);
        }

        static BlobHandle ByRefIntSignature(
            MetadataBuilder metadata,
            EntityHandle modifier)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x01);
            if (!modifier.IsNil)
            {
                signature.WriteByte(0x20);
                WriteTypeDefOrRef(signature, modifier);
            }
            signature.WriteByte(0x10);
            signature.WriteByte(0x08);
            return metadata.GetOrAddBlob(signature);
        }

        static BlobHandle GenericInstanceSignature(
            MetadataBuilder metadata,
            EntityHandle genericType)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x15);
            signature.WriteByte(0x12);
            WriteTypeDefOrRef(signature, genericType);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x08);
            return metadata.GetOrAddBlob(signature);
        }

        static void WriteClass(
            BlobBuilder signature,
            EntityHandle type)
        {
            signature.WriteByte(0x12);
            WriteTypeDefOrRef(signature, type);
        }

        static void WriteTypeDefOrRef(
            BlobBuilder signature,
            EntityHandle type)
        {
            int tag = type.Kind switch
            {
                HandleKind.TypeDefinition => 0,
                HandleKind.TypeReference => 1,
                HandleKind.TypeSpecification => 2,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(type)),
            };
            signature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(type) << 2) | tag);
        }
    }

    sealed class VersionUnifiedSignatureFixture : IDisposable
    {
        readonly string _directory;
        readonly ResolvedAssemblyReference _library;
        readonly ResolvedAssemblyReference _signatureV1;

        VersionUnifiedSignatureFixture(
            string directory,
            string consumerPath,
            string libraryPath,
            string signatureV1Path,
            string signatureV2Path,
            string signatureV3Path)
        {
            _directory = directory;
            ConsumerPath = consumerPath;
            _library = FromPath(libraryPath);
            _signatureV1 = FromPath(signatureV1Path);
            SignatureV2 = FromPath(signatureV2Path);
            SignatureV3 = FromPath(signatureV3Path);
            PayloadName = Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "ExternalFacts",
                        ["Payload"]))
                .Name;
        }

        public string ConsumerPath { get; }
        public ResolvedAssemblyReference SignatureV2 { get; }
        public ResolvedAssemblyReference SignatureV3 { get; }
        public MetadataTypeDefinitionName PayloadName { get; }

        public static VersionUnifiedSignatureFixture Create()
        {
            string directory = Directory.CreateTempSubdirectory(
                "dotnet-inspect-version-unified-signature-").FullName;
            try
            {
                string implementationDirectory = Directory.CreateDirectory(
                    Path.Combine(directory, "implementation")).FullName;
                string signatureV1Path = Emit(
                    implementationDirectory,
                    "VersionUnified.Signatures",
                    """
                    using System.Reflection;

                    [assembly: AssemblyVersion("1.0.0.0")]

                    namespace ExternalFacts;

                    public sealed class Payload;
                    """);
                const string librarySource =
                    """
                    namespace ExternalFacts;

                    public sealed class Library
                    {
                        public Payload Value { get; } = new();
                    }
                    """;
                string implementationLibraryPath = Emit(
                    implementationDirectory,
                    "VersionUnified.Library",
                    librarySource,
                    [MetadataReference.CreateFromFile(signatureV1Path)]);

                string signatureV2Path = Emit(
                    directory,
                    "VersionUnified.Signatures",
                    """
                    using System.Reflection;

                    [assembly: AssemblyVersion("2.0.0.0")]

                    namespace ExternalFacts;

                    public sealed class Payload;
                    """);
                string distinctDirectory = Directory.CreateDirectory(
                    Path.Combine(directory, "distinct")).FullName;
                string signatureV3Path = Emit(
                    distinctDirectory,
                    "VersionUnified.Signatures",
                    """
                    using System.Reflection;

                    [assembly: AssemblyVersion("3.0.0.0")]

                    namespace ExternalFacts;

                    public sealed class Payload;
                    """);
                string libraryPath = Emit(
                    directory,
                    "VersionUnified.Library",
                    librarySource,
                    [MetadataReference.CreateFromFile(signatureV2Path)]);
                string consumerPath = Emit(
                    directory,
                    "VersionUnified.Consumer",
                    """
                    namespace ExternalFacts;

                    public static class Consumer
                    {
                        public static Payload UseProperty(Library library)
                            => library.Value;
                    }
                    """,
                    [
                        MetadataReference.CreateFromFile(libraryPath),
                        MetadataReference.CreateFromFile(signatureV2Path),
                    ]);
                // Model deployment version unification: the consumer was
                // compiled against v2, but the selected library implementation
                // still carries its v1 transitive signature reference.
                File.Copy(
                    implementationLibraryPath,
                    libraryPath,
                    overwrite: true);

                return new VersionUnifiedSignatureFixture(
                    directory,
                    consumerPath,
                    libraryPath,
                    signatureV1Path,
                    signatureV2Path,
                    signatureV3Path);
            }
            catch
            {
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public void Dispose() =>
            Directory.Delete(_directory, recursive: true);

        public IAssemblyReferenceResolver CreateResolver() =>
            new VersionUnifiedResolver(
                _library,
                _signatureV1,
                SignatureV2,
                SignatureV3);

        public MethodRef Getter(
            AssemblyReferenceIdentity signatureIdentity)
        {
            MetadataTypeDefinitionName libraryName =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "ExternalFacts",
                        ["Library"]))
                .Name;
            TypeRef library = TypeRef.DefinitionWithResolution(
                _library.Identity.Name,
                "ExternalFacts",
                "Library",
                ValueTypeHint.Unknown,
                MetadataFactState.Unknown,
                enclosingType: null,
                libraryName,
                _library.Identity);
            TypeRef payload = TypeRef.DefinitionWithResolution(
                signatureIdentity.Name,
                "ExternalFacts",
                "Payload",
                ValueTypeHint.Unknown,
                MetadataFactState.Unknown,
                enclosingType: null,
                PayloadName,
                signatureIdentity);
            return new MethodRef(
                library,
                "get_Value",
                payload,
                [],
                HasThis: true)
            {
                IsSpecialName = true,
                IsSpecialNameInferred = true,
            };
        }

        static ResolvedAssemblyReference FromPath(string path) =>
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "version-unified fixture"));

        sealed class VersionUnifiedResolver(
            ResolvedAssemblyReference library,
            ResolvedAssemblyReference signatureV1,
            ResolvedAssemblyReference signatureV2,
            ResolvedAssemblyReference signatureV3)
            : IAssemblyReferenceResolver
        {
            readonly IAssemblyReferenceResolver _runtime =
                TestAssemblyReferenceResolvers.RuntimeAssemblies();

            public ResolvedAssemblyReference? Resolve(
                AssemblyReferenceIdentity identity,
                AssemblyResolutionScope scope)
            {
                if (identity.Name == library.Identity.Name)
                    return library;
                if (identity.Name == signatureV1.Identity.Name)
                {
                    return identity.Version == signatureV3.Identity.Version
                        ? signatureV3
                        : signatureV2;
                }

                return _runtime.Resolve(identity, scope);
            }
        }
    }

    sealed class CrossAssemblyFixture : IDisposable
    {
        readonly string _directory;

        CrossAssemblyFixture(string directory, string consumerPath)
        {
            _directory = directory;
            ConsumerPath = consumerPath;
        }

        public string ConsumerPath { get; }

        public static CrossAssemblyFixture Create(bool versionDrift = false)
        {
            var directory = Directory.CreateTempSubdirectory("dotnet-inspect-method-facts-").FullName;
            try
            {
                const string librarySource = """
                    using System.Reflection;
                    using System.Runtime.CompilerServices;

                    [assembly: AssemblyVersion("1.0.0.0")]

                    namespace ExternalFacts;

                    public static class ByRefLibrary
                    {
                        public static void WriteOut(out int value) => value = 42;
                        public static void Mutate(ref int value) => value++;
                        public static void Read(in int value) { _ = value; }
                        public static void WriteExternalOut(out ExternalReference value) => value = new();
                        public static void MutateExternal(ref ExternalReference value) => value = new();
                        public static void GenericCollision<T>(ref object value) { }
                        public static void GenericCollision<T>(out T value) => value = default!;
                    }

                    public delegate int ExternalDelegate(int value);

                    public static class DelegateLibrary
                    {
                        public static int DelegateTarget(int value) => value + 1;
                    }

                    public static class OperatorLikeLibrary
                    {
                        public static int op_Addition(int left, int right) => left - right;
                        public static int op_Implicit(int value) => value + 1;
                    }

                    public sealed class PropertyLibrary
                    {
                        public PropertyLibrary(int count) => Count = count;
                        public int Count { get; }
                    }

                    public sealed class DynamicLibrary
                    {
                        readonly ExternalNumber _value = new(1);
                        dynamic _reference = new ExternalNumber(4);
                        object _objectReference = new ExternalNumber(5);
                        public dynamic DynamicField = new ExternalNumber(2);
                        public object ObjectField = new ExternalNumber(3);
                        public dynamic DynamicValue => _value;
                        public dynamic GetDynamicValue() => _value;
                        public ref dynamic GetDynamicReference() => ref _reference;
                        public ref dynamic DynamicReference => ref _reference;
                        public ref object GetObjectReference() => ref _objectReference;
                    }

                    public sealed class GenericDynamicLibrary<T>
                    {
                        public T Value = default!;
                        T _reference = default!;
                        public ref T Reference => ref _reference;
                    }

                    public ref struct RefFieldLibrary
                    {
                        public ref dynamic DynamicField;
                        public ref object ObjectField;

                        public RefFieldLibrary(ref dynamic dynamicField, ref object objectField)
                        {
                            DynamicField = ref dynamicField;
                            ObjectField = ref objectField;
                        }
                    }

                    public sealed class ExternalReference
                    {
                        public static bool operator ==(ExternalReference left, object right) => true;
                        public static bool operator !=(ExternalReference left, object right) => false;
                        public override bool Equals(object? obj) => false;
                        public override int GetHashCode() => 0;
                    }

                    public readonly struct ExternalNumber
                    {
                        public ExternalNumber(int value) => Value = value;
                        public int Value { get; }
                        public static ExternalNumber operator +(ExternalNumber left, ExternalNumber right)
                            => new(left.Value + right.Value);
                    }

                    public ref struct ExternalRefStruct
                    {
                        public int Value;
                    }

                    [CompilerGenerated]
                    public static class Generated__DisplayClass0_0
                    {
                        [CompilerGenerated]
                        public static int Run(int value) => value + 1;
                    }

                    [InlineArray(4)]
                    public struct ExternalInline4
                    {
                        private int _element0;
                    }
                    """;
                string libraryPath = Emit(
                    directory,
                    "ExternalFacts.Library",
                    librarySource);
                string consumerPath = Emit(
                    directory,
                    "ExternalFacts.Consumer",
                    """
                    namespace ExternalFacts;

                    public static class Consumer
                    {
                        public static int UseOut()
                        {
                            ByRefLibrary.WriteOut(out var value);
                            return value;
                        }

                        public static int UseRef()
                        {
                            int value = 1;
                            ByRefLibrary.Mutate(ref value);
                            return value;
                        }

                        public static void UseIn()
                        {
                            int value = 1;
                            ByRefLibrary.Read(in value);
                        }

                        public static ExternalReference UseExternalOut()
                        {
                            ByRefLibrary.WriteExternalOut(out var value);
                            return value;
                        }

                        public static ExternalReference UseExternalRef(ExternalReference value)
                        {
                            ByRefLibrary.MutateExternal(ref value);
                            return value;
                        }

                        public static object UseGenericOut()
                        {
                            ByRefLibrary.GenericCollision<object>(out var value);
                            return value;
                        }

                        public static int UseGenerated(int value)
                            => Generated__DisplayClass0_0.Run(value);

                        public static ExternalDelegate UseExternalDelegate()
                            => DelegateLibrary.DelegateTarget;

                        public static int UseOperatorLikeAddition(int left, int right)
                            => OperatorLikeLibrary.op_Addition(left, right);

                        public static int UseOperatorLikeImplicit(int value)
                            => OperatorLikeLibrary.op_Implicit(value);

                        public static ExternalNumber UseRealOperator(ExternalNumber left, ExternalNumber right)
                            => left + right;

                        public static int UseExternalRefStruct()
                        {
                            ExternalRefStruct value = default;
                            value.Value = 3;
                            return value.Value;
                        }

                        public static int UseExternalStruct()
                        {
                            ExternalNumber value = new(3);
                            return value.Value;
                        }

                        public static int UseProperty(PropertyLibrary library)
                            => library.Count;

                        public static bool UseDynamicProperty(DynamicLibrary library, object right)
                            => (object)library.DynamicValue == right;

                        public static bool UseDynamicMethod(DynamicLibrary library, object right)
                            => (object)library.GetDynamicValue() == right;

                        public static bool UseByRefDynamicMethod(DynamicLibrary library, object right)
                            => (object)library.GetDynamicReference() == right;

                        public static bool UseByRefDynamicProperty(DynamicLibrary library, object right)
                            => (object)library.DynamicReference == right;

                        public static bool UseByRefObjectMethod(DynamicLibrary library, object right)
                            => (object)library.GetObjectReference() == right;

                        public static bool UseDynamicField(DynamicLibrary library, object right)
                            => (object)library.DynamicField == right;

                        public static bool UseObjectField(DynamicLibrary library, object right)
                            => library.ObjectField == right;

                        public static bool UseByRefDynamicField(ref RefFieldLibrary library, object right)
                            => (object)library.DynamicField == right;

                        public static bool UseByRefObjectField(ref RefFieldLibrary library, object right)
                            => (object)library.ObjectField == right;

                        public static bool UseGenericDynamicField(GenericDynamicLibrary<dynamic> library, object right)
                            => (object)library.Value == right;

                        public static bool UseGenericByRefDynamicProperty(GenericDynamicLibrary<dynamic> library, object right)
                            => (object)library.Reference == right;

                        public static bool UseExternalNewObject(object right)
                            => (object)new ExternalReference() == right;

                        public static bool UseUri(string value)
                            => System.Uri.TryCreate(value, System.UriKind.Absolute, out var uri) && uri is not null;

                        public static int UseExternalInlineArray(ExternalInline4 buffer, int index)
                        {
                            System.Span<int> span = buffer;
                            return span[index];
                        }
                    }
                    """,
                    [MetadataReference.CreateFromFile(libraryPath)]);
                if (versionDrift)
                {
                    Emit(
                        directory,
                        "ExternalFacts.Library",
                        librarySource.Replace(
                            """AssemblyVersion("1.0.0.0")""",
                            """AssemblyVersion("2.0.0.0")""",
                            StringComparison.Ordinal));
                }
                return new CrossAssemblyFixture(directory, consumerPath);
            }
            catch
            {
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    static class CrossAssemblyFixtureMethods
    {
        public const string UseOut = nameof(UseOut);
        public const string UseRef = nameof(UseRef);
        public const string UseIn = nameof(UseIn);
        public const string UseExternalOut = nameof(UseExternalOut);
        public const string UseExternalRef = nameof(UseExternalRef);
        public const string UseGenerated = nameof(UseGenerated);
        public const string UseExternalDelegate = nameof(UseExternalDelegate);
        public const string UseOperatorLikeAddition = nameof(UseOperatorLikeAddition);
        public const string UseOperatorLikeImplicit = nameof(UseOperatorLikeImplicit);
        public const string UseRealOperator = nameof(UseRealOperator);
        public const string UseExternalRefStruct = nameof(UseExternalRefStruct);
        public const string UseExternalStruct = nameof(UseExternalStruct);
        public const string UseProperty = nameof(UseProperty);
        public const string UseDynamicProperty = nameof(UseDynamicProperty);
        public const string UseDynamicMethod = nameof(UseDynamicMethod);
        public const string UseByRefDynamicMethod = nameof(UseByRefDynamicMethod);
        public const string UseByRefDynamicProperty = nameof(UseByRefDynamicProperty);
        public const string UseByRefObjectMethod = nameof(UseByRefObjectMethod);
        public const string UseDynamicField = nameof(UseDynamicField);
        public const string UseObjectField = nameof(UseObjectField);
        public const string UseByRefDynamicField = nameof(UseByRefDynamicField);
        public const string UseByRefObjectField = nameof(UseByRefObjectField);
        public const string UseGenericDynamicField = nameof(UseGenericDynamicField);
        public const string UseGenericByRefDynamicProperty = nameof(UseGenericByRefDynamicProperty);
        public const string UseExternalNewObject = nameof(UseExternalNewObject);
        public const string UseUri = nameof(UseUri);
        public const string UseExternalInlineArray = nameof(UseExternalInlineArray);
    }
}
