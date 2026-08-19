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
    public void GenericDeclaringTypeSignatureCollision_UsesDefinitionSignature()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(
            source,
            "UseGenericDeclaringTypeOut",
            "DefinitionCollision",
            ArgumentRefKind.Out);
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

        var additionResult = PrintResult(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition));
        var conversionResult = PrintResult(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit));
        string addition = additionResult.Output ?? "";
        string conversion = conversionResult.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, additionResult.Fidelity);
        Assert.Equal(DecompilationFidelity.Full, conversionResult.Fidelity);
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

        var result = PrintResult(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator));
        string output = result.Output ?? "";

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("left + right", output);
        Assert.DoesNotContain("op_Addition", output);
    }

    [Fact]
    public void CrossAssemblyConversion_WithResolvedExternalClass_RendersOperator()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(
            source,
            nameof(CrossAssemblyFixtureMethods.UseExternalConversion),
            "op_Implicit");
        var result = PrintResult(
            source,
            nameof(CrossAssemblyFixtureMethods.UseExternalConversion));

        Assert.Equal(
            ValueTypeHint.ReferenceType,
            call.Callee.ParameterTypes[0].ValueTypeHint);
        Assert.Equal(MetadataFactState.Yes, call.Callee.IsOperator);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "return (ExternalConversionHolder)value;",
            result.Output);
        Assert.DoesNotContain("op_Implicit", result.Output);
    }

    [Fact]
    public void SameAssemblyConversion_WithResolvedExternalClass_RendersOperator()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.LibraryPath);
        var function = IrImporter.Import(
            source,
            "ExternalFacts.ExternalConversionHolder",
            "Convert");

        Assert.NotNull(function);
        var call = Assert.Single(
            function.Descendants.OfType<Call>(),
            candidate => candidate.Callee.Name == "op_Implicit");
        var result = CSharpPrinter.Print(function);

        Assert.Equal(MetadataFactState.Yes, call.Callee.IsOperator);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "return (ExternalConversionHolder)value;",
            result.Output);
        Assert.DoesNotContain("op_Implicit", result.Output);
    }

    [Fact]
    public void OpenGenericExternalClassConversion_RendersOperator()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.LibraryPath);
        var function = IrImporter.Import(
            source,
            "ExternalFacts.GenericConversionHolder`1",
            "Convert");

        Assert.NotNull(function);
        var call = Assert.Single(
            function.Descendants.OfType<Call>(),
            candidate => candidate.Callee.Name == "op_Implicit");
        var result = CSharpPrinter.Print(function);

        Assert.Equal(MetadataFactState.Yes, call.Callee.IsOperator);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "return (GenericConversionHolder<T>)value;",
            result.Output);
        Assert.DoesNotContain("op_Implicit", result.Output);
    }

    [Fact]
    public void ResolvedExternalInterfaceConversion_StaysRejected()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        TypeRef holder = fixture.Type(
            fixture.InvalidLibraryPath,
            "ExternalFacts",
            "InvalidExternalConversionHolder");
        TypeRef contract = fixture.Type(
            fixture.ContractsPath,
            "ExternalContracts",
            "IExternalFace");
        var callee = new MethodRef(
            holder,
            "op_Explicit",
            holder,
            [contract],
            HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Unknown,
        };

        MethodRef resolved = source.CrossAssembly.Upgrade(
            callee,
            resolveRequiresUnsafe: false);

        Assert.Equal(MetadataFactState.No, resolved.IsOperator);
    }

    [Fact]
    public void MissingOperandRelationshipDependency_KeepsOperatorUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        var siblings =
            new MetadataSource.SiblingAssemblyReferenceResolver(
                fixture.ConsumerPath);
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            null,
            new DenyAssemblyResolver(
                siblings,
                "ExternalFacts.Contracts"));
        TypeRef holder = fixture.Type(
            fixture.InvalidLibraryPath,
            "ExternalFacts",
            "InvalidExternalConversionHolder");
        TypeRef contract = fixture.Type(
            fixture.ContractsPath,
            "ExternalContracts",
            "IExternalFace");
        var callee = new MethodRef(
            holder,
            "op_Explicit",
            holder,
            [contract],
            HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Unknown,
        };

        MethodRef resolved = source.CrossAssembly.Upgrade(
            callee,
            resolveRequiresUnsafe: false);

        Assert.Equal(MetadataFactState.Unknown, resolved.IsOperator);
    }

    [Fact]
    public void ResolvedExternalTransitiveBaseConversion_StaysRejected()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        TypeRef holder = fixture.Type(
            fixture.InvalidDerivedLibraryPath,
            "ExternalFacts",
            "InvalidDerivedConversionHolder");
        TypeRef baseType = fixture.Type(
            fixture.ContractsPath,
            "ExternalContracts",
            "ExternalBaseX");
        var callee = new MethodRef(
            holder,
            "op_Implicit",
            baseType,
            [holder],
            HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Unknown,
        };

        MethodRef resolved = source.CrossAssembly.Upgrade(
            callee,
            resolveRequiresUnsafe: false);

        Assert.Equal(MetadataFactState.No, resolved.IsOperator);
    }

    [Theory]
    [InlineData(
        nameof(CrossAssemblyFixtureMethods.UseRealOperator),
        "op_Addition",
        ".op_Addition(left, right)",
        "left + right")]
    [InlineData(
        nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition),
        "op_Addition",
        ".op_Addition(left, right)",
        "left + right")]
    [InlineData(
        nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit),
        "op_Implicit",
        ".op_Implicit(value)",
        "return (int)value;")]
    public void MissingCrossAssemblyOperatorMetadata_StaysExplicitAndDegrades(
        string methodName,
        string calleeName,
        string expected,
        string forbidden)
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            null,
            TestAssemblyReferenceResolvers.None);
        var function = ImportFunction(source, methodName);
        var call = Assert.Single(
            function.Descendants.OfType<Call>(),
            candidate => candidate.Callee.Name == calleeName);

        Assert.Equal(MetadataFactState.Unknown, call.Callee.IsOperator);

        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains(expected, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(forbidden, result.Output, StringComparison.Ordinal);
        Assert.Contains(
            FidelityRemarks.Collect(function),
            remark => remark.Code == DiagnosticIds.UnrepresentableMetadataName
                && remark.Reason.Contains(
                    "defining metadata was unavailable",
                    StringComparison.Ordinal));
        Assert.Contains(
            FidelityRemarks.CollectCauses(function),
            cause => cause.Discriminator
                == DecompilerFidelityDiscriminators.OperatorMetadataUnavailable);
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
        => PrintResult(source, methodName).Output ?? "";

    static DecompilerResult PrintResult(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        return CSharpPrinter.Print(function);
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

        public static MethodCollisionFixture Create()
        {
            string directory = Directory.CreateTempSubdirectory(
                "dotnet-inspect-method-collisions-").FullName;
            string library = Path.Combine(
                directory,
                "MethodCollisionLib.dll");
            File.WriteAllBytes(library, BuildMethodCollisionLibrary());
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

        static byte[] BuildMethodCollisionLibrary()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
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
            var attributeConstructor = metadata.AddMemberReference(
                requiresUnsafe,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
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

    sealed class CrossAssemblyFixture : IDisposable
    {
        readonly string _directory;

        CrossAssemblyFixture(
            string directory,
            string contractsPath,
            string libraryPath,
            string invalidLibraryPath,
            string invalidDerivedLibraryPath,
            string consumerPath)
        {
            _directory = directory;
            ContractsPath = contractsPath;
            LibraryPath = libraryPath;
            InvalidLibraryPath = invalidLibraryPath;
            InvalidDerivedLibraryPath = invalidDerivedLibraryPath;
            ConsumerPath = consumerPath;
        }

        public string ContractsPath { get; }
        public string LibraryPath { get; }
        public string InvalidLibraryPath { get; }
        public string InvalidDerivedLibraryPath { get; }
        public string ConsumerPath { get; }

        public static CrossAssemblyFixture Create(bool versionDrift = false)
        {
            var directory = Directory.CreateTempSubdirectory("dotnet-inspect-method-facts-").FullName;
            try
            {
                string contractsPath = Emit(
                    directory,
                    "ExternalFacts.Contracts",
                    """
                    namespace ExternalContracts;

                    public sealed class ExternalClass
                    {
                    }

                    public sealed class GenericExternal<T>
                    {
                    }

                    public interface IExternalFace
                    {
                    }

                    public sealed class ExternalOther
                    {
                    }

                    public class ExternalBaseX
                    {
                    }

                    public class ExternalChild : ExternalBaseX
                    {
                    }
                    """);
                const string librarySource = """
                    using System.Reflection;
                    using System.Runtime.CompilerServices;
                    using ExternalContracts;

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

                    public sealed class GenericByRefLibrary<T>
                    {
                        private void DefinitionCollision(out T value) => value = default!;
                        public void DefinitionCollision(out int value) => value = 42;
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

                    public sealed class ExternalConversionHolder
                    {
                        public static implicit operator ExternalConversionHolder(
                            ExternalClass value) => new();

                        public static ExternalConversionHolder Convert(
                            ExternalClass value) => value;
                    }

                    public sealed class GenericConversionHolder<T>
                    {
                        public static implicit operator GenericConversionHolder<T>(
                            GenericExternal<T> value) => new();

                        public static GenericConversionHolder<T> Convert(
                            GenericExternal<T> value) => value;
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
                    librarySource,
                    [MetadataReference.CreateFromFile(contractsPath)]);
                string invalidLibraryPath = Emit(
                    directory,
                    "ExternalFacts.InvalidLibrary",
                    """
                    using ExternalContracts;

                    namespace ExternalFacts;

                    public sealed class InvalidExternalConversionHolder
                    {
                        public static explicit operator InvalidExternalConversionHolder(
                            ExternalClass value) => new();
                    }
                    """,
                    [MetadataReference.CreateFromFile(contractsPath)]);
                PatchTypeReference(
                    invalidLibraryPath,
                    "ExternalClass"u8,
                    "IExternalFace"u8);
                string invalidDerivedLibraryPath = Emit(
                    directory,
                    "ExternalFacts.InvalidDerivedLibrary",
                    """
                    using ExternalContracts;

                    namespace ExternalFacts;

                    public sealed class InvalidDerivedConversionHolder
                        : ExternalChild
                    {
                        public static implicit operator ExternalOther(
                            InvalidDerivedConversionHolder value) => new();
                    }
                    """,
                    [MetadataReference.CreateFromFile(contractsPath)]);
                PatchTypeReference(
                    invalidDerivedLibraryPath,
                    "ExternalOther"u8,
                    "ExternalBaseX"u8);
                string consumerPath = Emit(
                    directory,
                    "ExternalFacts.Consumer",
                    """
                    using ExternalContracts;

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

                        public static int UseGenericDeclaringTypeOut(GenericByRefLibrary<int> library)
                        {
                            library.DefinitionCollision(out var value);
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

                        public static ExternalConversionHolder UseExternalConversion(
                            ExternalClass value) => value;

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
                    [
                        MetadataReference.CreateFromFile(libraryPath),
                        MetadataReference.CreateFromFile(contractsPath),
                    ]);
                if (versionDrift)
                {
                    Emit(
                        directory,
                        "ExternalFacts.Library",
                        librarySource.Replace(
                            """AssemblyVersion("1.0.0.0")""",
                            """AssemblyVersion("2.0.0.0")""",
                            StringComparison.Ordinal),
                        [MetadataReference.CreateFromFile(contractsPath)]);
                }
                return new CrossAssemblyFixture(
                    directory,
                    contractsPath,
                    libraryPath,
                    invalidLibraryPath,
                    invalidDerivedLibraryPath,
                    consumerPath);
            }
            catch
            {
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        static string Emit(string directory, string assemblyName, string source, IEnumerable<MetadataReference>? additionalReferences = null)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var references = ImmutableArray.CreateBuilder<MetadataReference>();
            references.AddRange(RuntimeReferences());
            if (additionalReferences is not null)
                references.AddRange(additionalReferences);

            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source, parseOptions)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            string path = Path.Combine(directory, assemblyName + ".dll");
            var result = compilation.Emit(path);
            Assert.True(
                result.Success,
                "fixture compilation failed:\n" + string.Join("\n", result.Diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}")));
            return path;
        }

        static ImmutableArray<MetadataReference> RuntimeReferences()
            => RoslynTestReferences.TrustedPlatform;

        public TypeRef Type(
            string assemblyPath,
            string @namespace,
            string name)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            var definitionName = MetadataTypeDefinitionName.Create(
                @namespace,
                [name]) switch
            {
                MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
                _ => throw new InvalidOperationException(
                    "fixture metadata name is invalid"),
            };
            return TypeRef.DefinitionWithResolution(
                identity.Name,
                @namespace,
                name,
                ValueTypeHint.ReferenceType,
                MetadataFactState.Unknown,
                null,
                definitionName,
                identity);
        }

        static void PatchTypeReference(
            string assemblyPath,
            ReadOnlySpan<byte> original,
            ReadOnlySpan<byte> replacement)
        {
            Assert.Equal(original.Length, replacement.Length);
            byte[] image = File.ReadAllBytes(assemblyPath);
            int offset = image.AsSpan().IndexOf(original);
            Assert.True(offset >= 0, "fixture TypeRef name was not found");
            replacement.CopyTo(image.AsSpan(offset, replacement.Length));
            Assert.True(
                image.AsSpan(offset + replacement.Length).IndexOf(original) < 0,
                "fixture TypeRef name was not unique");
            File.WriteAllBytes(assemblyPath, image);
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
        public const string UseExternalConversion = nameof(UseExternalConversion);
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

sealed class DenyAssemblyResolver(
    IAssemblyReferenceResolver inner,
    string deniedAssembly)
    : IAssemblyReferenceResolver
{
    public ResolvedAssemblyReference? Resolve(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope)
        => identity.Name == deniedAssembly
            ? null
            : inner.Resolve(identity, scope);
}
