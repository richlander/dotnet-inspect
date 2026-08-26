using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
    public void PropertyDeclaration_DerivesPropertyAccessibilityFromAuthenticatedBaseWhenOnlySetterIsPresent()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.SetterOnlyOverrideDerived));
        var property = GetProperty(type, "Value");

        var declaration = MetadataDeclarationQuery.GetProperty(Reader, type, property);

        Assert.Equal("public", declaration.Accessibility);
        var setter = Assert.Single(declaration.Signature.Accessors);
        Assert.Equal("set", setter.Kind);
        Assert.Equal("protected", setter.Accessibility);
    }

    [Fact]
    public void PropertyDeclaration_DerivesPropertyAccessibilityAcrossAcyclicSetterOnlyOverrideChain()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.SetterOnlyOverrideLeaf));
        var property = GetProperty(type, "Value");

        var declaration = MetadataDeclarationQuery.GetProperty(Reader, type, property);

        Assert.Equal("public", declaration.Accessibility);
        var setter = Assert.Single(declaration.Signature.Accessors);
        Assert.Equal("set", setter.Kind);
        Assert.Equal("protected", setter.Accessibility);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_PreservesNonPublicSourceDeclarationFacts()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.OverrideDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");
        var method = Reader.GetMethodDefinition(methodHandle);

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, derived, method);
        var slot = MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            Reader,
            derivedHandle,
            methodHandle);

        Assert.NotNull(slot);
        var resolvedSlot = slot!;
        Assert.Equal("internal", declaration.Accessibility);
        Assert.True(declaration.IsOverride);
        Assert.False(declaration.IsVirtual);
        Assert.False(MetadataDeclarationQuery.IsSourceDeclarableVirtualMethod(method));
        Assert.Equal(
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.OverrideBase)),
            resolvedSlot.DeclaringType);
        Assert.Equal(
            "Value",
            Reader.GetString(Reader.GetMethodDefinition(resolvedSlot.Method).Name));

        var baseMethod = Reader.GetMethodDefinition(resolvedSlot.Method);
        Assert.True(MetadataDeclarationQuery.GetMethod(
            Reader,
            Reader.GetTypeDefinition(resolvedSlot.DeclaringType),
            baseMethod).IsVirtual);
        Assert.True(MetadataDeclarationQuery.IsSourceDeclarableVirtualMethod(baseMethod));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesNewVirtualSlot()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.NewSlotDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");

        Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            Reader,
            derivedHandle,
            methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesExternalBase()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ExternalOverride));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, nameof(object.ToString));

        Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            Reader,
            derivedHandle,
            methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesAccessibilityChangingOverride()
    {
        string path = EmitAccessibilityChangingOverride();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var derivedHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "AccessDerived");
            var derived = reader.GetTypeDefinition(derivedHandle);
            var methodHandle = derived.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == "Value");

            Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SameAssemblyOverrideSlot_UsesNearestReintroducedSlot()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.OverrideLeaf));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.OverrideMiddle)),
            slot.DeclaringType);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsCovariantReturnShape()
    {
        string path = EmitCovariantReturnOverride();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var derivedHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Derived");
            var derived = reader.GetTypeDefinition(derivedHandle);
            var methodHandle = derived.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == "Value");

            var slot = Assert.IsType<MetadataOverrideSlot>(
                MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                    reader,
                    derivedHandle,
                    methodHandle));

            Assert.Equal("Base", reader.GetString(reader.GetTypeDefinition(slot.DeclaringType).Name));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SameAssemblyOverrideSlot_UsesCompilerProducedCovariantMethodImpl()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.CovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");
        var method = Reader.GetMethodDefinition(methodHandle);
        var implementation = Assert.Single(
            derived.GetMethodImplementations().Select(Reader.GetMethodImplementation),
            candidate => candidate.MethodBody == methodHandle);

        Assert.True((method.Attributes & MethodAttributes.NewSlot) != 0);
        Assert.Equal(HandleKind.MethodDefinition, implementation.MethodDeclaration.Kind);

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, derived, method);
        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.True(declaration.IsOverride);
        Assert.False(declaration.IsVirtual);
        Assert.Equal(
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.CovariantReturnBase)),
            slot.DeclaringType);
        Assert.Equal((MethodDefinitionHandle)implementation.MethodDeclaration, slot.Method);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesCompilerProducedScopedParameterIdentity()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ScopedParameterCovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.ScopedParameterCovariantReturnBase)),
            slot.DeclaringType);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesSameFqnReturnFromDifferentAssemblies()
    {
        using var stream = new MemoryStream(
            BuildScopedSignatureCollisionImage(
                ScopedSignatureCollision.CrossAssemblyReturn));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesNestedVsNamespaceParameter()
    {
        using var stream = new MemoryStream(
            BuildScopedSignatureCollisionImage(
                ScopedSignatureCollision.NestedVsNamespaceParameter));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsReferenceConstrainedGenericCovariantMethodImpl()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.GenericCovariantReturnDerived<>));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");
        var method = Reader.GetMethodDefinition(methodHandle);
        var implementation = Assert.Single(
            derived.GetMethodImplementations().Select(Reader.GetMethodImplementation),
            candidate => candidate.MethodBody == methodHandle);

        Assert.True((method.Attributes & MethodAttributes.NewSlot) != 0);
        Assert.Equal(HandleKind.MethodDefinition, implementation.MethodDeclaration.Kind);

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, derived, method);
        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.True(declaration.IsOverride);
        Assert.False(declaration.IsVirtual);
        Assert.Equal(
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.ObjectCovariantReturnBase)),
            slot.DeclaringType);
        Assert.Equal((MethodDefinitionHandle)implementation.MethodDeclaration, slot.Method);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesReferenceConstrainedGenericToArbitraryClass()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterCovarianceImage(
                referenceTypeConstraint: true,
                explicitConstraint: GenericConstraintTarget.None,
                baseReturnsDog: false));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesConstructedGenericBaseCovariantMethodImpl()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .ConstructedGenericCovariantReturnDerived<>));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");
        var implementation = Reader.GetMethodImplementation(
            Assert.Single(derived.GetMethodImplementations()));

        // The shape this gate exists for: Roslyn writes the base as a
        // TypeSpec and the MethodImpl declaration as a MemberRef rooted in it.
        Assert.Equal(
            HandleKind.TypeSpecification,
            derived.BaseType.Kind);
        Assert.Equal(
            HandleKind.MemberReference,
            implementation.MethodDeclaration.Kind);

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        var baseHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .ConstructedGenericCovariantReturnBase<>));
        Assert.Equal(baseHandle, slot.DeclaringType);
        Assert.Equal(
            GetMethodHandle(
                Reader.GetTypeDefinition(baseHandle),
                "Value"),
            slot.Method);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesConstructedGenericBaseSubstitutedParameter()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .ConstructedGenericSubstitutionDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Describe");

        // A plain override on a constructed generic base carries no
        // MethodImpl, so the slot is reachable only by walking the TypeSpec
        // base and substituting string for the base's type parameter.
        Assert.Equal(
            HandleKind.TypeSpecification,
            derived.BaseType.Kind);
        Assert.Empty(derived.GetMethodImplementations());

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        var baseHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .ConstructedGenericSubstitutionBase<>));
        Assert.Equal(baseHandle, slot.DeclaringType);
        Assert.Equal(
            GetMethodHandle(
                Reader.GetTypeDefinition(baseHandle),
                "Describe"),
            slot.Method);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesSyntheticConstructedGenericMethodImpl()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericMethodImplImage(
                ConstructedGenericMethodImplShape.MatchingInstantiation));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            "Base`1",
            reader.GetString(
                reader.GetTypeDefinition(slot.DeclaringType).Name));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesConstructedGenericMethodImplWithMismatchedInstantiation()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericMethodImplImage(
                ConstructedGenericMethodImplShape.MismatchedInstantiation));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesConstructedGenericMethodImplRootedInExternalDefinition()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericMethodImplImage(
                ConstructedGenericMethodImplShape.ExternalDefinition));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    /// <summary>
    /// The all-<c>TypeDef</c> control for
    /// <see cref="SameAssemblyOverrideSlot_AuthenticatesCovariantReturnThroughConstructedGenericAncestry"/>:
    /// the same compiler-produced covariant-return shape whose returned type
    /// reaches the declared return through direct <c>TypeDef</c> base rows,
    /// which authenticates both before and after constructed-generic ancestry
    /// exists.
    /// </summary>
    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesDirectCovariantReturnThroughTypeDefAncestry()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.CovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");
        var dog = Reader.GetTypeDefinition(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.CovariantDog)));

        Assert.Equal(HandleKind.TypeDefinition, dog.BaseType.Kind);
        Assert.Empty(dog.GetInterfaceImplementations());
        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.CovariantAnimal)),
            (TypeDefinitionHandle)dog.BaseType);

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.CovariantReturnBase)),
            slot.DeclaringType);
    }

    /// <summary>
    /// Covariant-return ancestry is a metadata walk, not a <c>TypeDef</c>
    /// walk. <c>Dog : Middle&lt;int&gt; : Animal</c> reaches its declared
    /// return only through a constructed generic step, and the all-<c>TypeDef</c>
    /// control for the same authentication is
    /// <see cref="SameAssemblyOverrideSlot_AuthenticatesDirectCovariantReturnThroughTypeDefAncestry"/>.
    /// </summary>
    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesCovariantReturnThroughConstructedGenericAncestry()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .ConstructedAncestryCovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");
        var method = Reader.GetMethodDefinition(methodHandle);
        var implementation = Assert.Single(
            derived.GetMethodImplementations()
                .Select(Reader.GetMethodImplementation),
            candidate => candidate.MethodBody == methodHandle);

        // The shape this gate exists for: the compiler emits the covariant
        // return as NewSlot plus a MethodImpl, and writes the returned type's
        // own base as a TypeSpec, so its ancestry is invisible to a TypeDef
        // walk.
        Assert.True((method.Attributes & MethodAttributes.NewSlot) != 0);
        Assert.Equal(
            HandleKind.MethodDefinition,
            implementation.MethodDeclaration.Kind);
        Assert.Equal(
            HandleKind.TypeSpecification,
            Reader.GetTypeDefinition(
                GetTypeDefinitionHandle(
                    typeof(MetadataDeclarationQueryFixtures
                        .CovariantDogThroughConstructedMiddle)))
                .BaseType.Kind);

        var declaration =
            MetadataDeclarationQuery.GetMethod(Reader, derived, method);
        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.True(declaration.IsOverride);
        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures
                    .ConstructedAncestryCovariantReturnBase)),
            slot.DeclaringType);
        Assert.Equal(
            (MethodDefinitionHandle)implementation.MethodDeclaration,
            slot.Method);
    }

    /// <summary>
    /// The same walk carries interface ancestry, and carries each step's exact
    /// arguments into it: <c>Middle&lt;TValue&gt;</c> implements
    /// <c>ICovariantContract&lt;TValue&gt;</c> through a <c>TypeSpec</c>, so
    /// only substituting <c>int</c> proves the declared
    /// <c>ICovariantContract&lt;int&gt;</c> return.
    /// </summary>
    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesCovariantInterfaceReturnThroughConstructedGenericAncestry()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .ConstructedAncestryCovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Contract");
        var middle = Reader.GetTypeDefinition(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures
                    .CovariantConstructedMiddle<>)));

        Assert.Equal(
            HandleKind.TypeSpecification,
            Reader.GetInterfaceImplementation(
                Assert.Single(middle.GetInterfaceImplementations()))
                .Interface.Kind);

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures
                    .ConstructedAncestryCovariantReturnBase)),
            slot.DeclaringType);
    }

    /// <summary>
    /// The non-vacuity control for
    /// <see cref="SameAssemblyOverrideSlot_DeclinesConstructedGenericAncestryWithDifferentArgument"/>:
    /// the two images differ only in the argument the returned type's
    /// constructed base carries.
    /// </summary>
    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesConstructedGenericAncestryWithMatchingArgument()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericAncestryImage(
                ConstructedGenericAncestryShape.MatchingArgument));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            "Base",
            reader.GetString(
                reader.GetTypeDefinition(slot.DeclaringType).Name));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesConstructedGenericAncestryWithDifferentArgument()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericAncestryImage(
                ConstructedGenericAncestryShape.DifferentArgument));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        // Dog : Middle<Animal> is not a Middle<Dog>; ancestry may not erase
        // the instantiation to the definition it instantiates.
        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_CyclicConstructedAncestryFailsClosed()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericAncestryImage(
                ConstructedGenericAncestryShape.Cyclic));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_ExpandingConstructedAncestryFailsClosedWithinBudget()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericAncestryImage(
                ConstructedGenericAncestryShape.Expanding));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        // Middle<T> : Middle<Middle<T>> never repeats an instantiation, so
        // only the node cap and the comparison budget can answer at all.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var slot = MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            reader,
            derivedHandle,
            methodHandle);
        elapsed.Stop();

        Assert.Null(slot);
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(30),
            $"Bounded ancestry traversal took {elapsed.Elapsed}.");
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsValidOnlyExplicitConstraintSet()
    {
        using var stream = new MemoryStream(
            BuildDegradedConstraintSetImage(
                DegradedConstraintSetShape.ValidOnly));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            "Base",
            reader.GetString(
                reader.GetTypeDefinition(slot.DeclaringType).Name));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesDegradedOnlyConstraint()
    {
        using var stream = new MemoryStream(
            BuildDegradedConstraintSetImage(
                DegradedConstraintSetShape.DegradedOnly));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        AssertDegradedConstraintIsUndecodable(reader);
        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    /// <summary>
    /// Malformed current-image constraint evidence is sticky across the whole
    /// constraint set, so a valid constraint that would otherwise authenticate
    /// cannot rescue it, in either metadata order. The valid constraint alone
    /// authenticates in
    /// <see cref="SameAssemblyOverrideSlot_AllowsValidOnlyExplicitConstraintSet"/>.
    /// </summary>
    [Theory]
    [InlineData(DegradedConstraintSetShape.DegradedFirst)]
    [InlineData(DegradedConstraintSetShape.ValidFirst)]
    public void SameAssemblyOverrideSlot_DeclinesMixedDegradedAndValidConstraintSet(
        DegradedConstraintSetShape shape)
    {
        using var stream = new MemoryStream(
            BuildDegradedConstraintSetImage(shape));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        AssertDegradedConstraintIsUndecodable(reader);
        Assert.Equal(
            2,
            reader.GetGenericParameter(
                reader.GetTypeDefinition(derivedHandle)
                    .GetGenericParameters()
                    .Single())
                .GetConstraints()
                .Count);
        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    /// <summary>
    /// Proves the degraded constraint really reaches the fail-closed path
    /// rather than being declined for some unrelated reason: its
    /// <c>TypeSpec</c> is over-deep, so the shared signature guard refuses to
    /// decode it and the constraint can only decode as degraded.
    /// </summary>
    static void AssertDegradedConstraintIsUndecodable(MetadataReader reader)
    {
        TypeSpecificationHandle constraintSpec = Assert.Single(
            reader.GetGenericParameter(
                reader.TypeDefinitions
                    .Select(reader.GetTypeDefinition)
                    .Single(type =>
                        reader.GetString(type.Name) == "Derived")
                    .GetGenericParameters()
                    .Single())
                .GetConstraints()
                .Select(handle =>
                    reader.GetGenericParameterConstraint(handle).Type)
                .Where(type => type.Kind == HandleKind.TypeSpecification)
                .Select(type => (TypeSpecificationHandle)type));

        Assert.False(
            SignatureBlobGuard.IsSafeToDecode(
                reader,
                reader.GetTypeSpecification(constraintSpec).Signature,
                SignatureBlobGuard.Kind.TypeSpecification));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_WideGenericParameterDagFailsClosedWithinBudget()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterConstraintGraphImage(
                parameterCount: 64,
                GenericParameterConstraintGraph.Dag));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        // Path count in this DAG grows like the Fibonacci numbers, so 64
        // parameters is roughly 10^13 distinct constraint paths. Only a
        // cumulative work bound can answer at all.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var slot = MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            reader,
            derivedHandle,
            methodHandle);
        elapsed.Stop();

        Assert.Null(slot);
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(30),
            $"Bounded DAG traversal took {elapsed.Elapsed}.");
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeepGenericParameterChainFailsClosed()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterConstraintGraphImage(
                parameterCount: 2_000,
                GenericParameterConstraintGraph.Chain));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeepGenericParameterChainDoesNotCrashProcess()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterConstraintGraphImage(
                parameterCount: 60_000,
                GenericParameterConstraintGraph.Chain));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        // A recursion-per-link walk overflows the native stack here, which no
        // managed catch can contain: reaching the assertion at all is the
        // evidence, because a stack overflow would take this process down.
        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void AuthenticatedObjectSlotOverride_AcceptsSameImageChainToObject()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .SameImageObjectSlotDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);

        foreach (string name in (string[])
            ["ToString", "GetHashCode", "Equals"])
        {
            Assert.True(
                MetadataDeclarationQuery.IsAuthenticatedObjectSlotOverride(
                    Reader,
                    derivedHandle,
                    GetMethodHandle(derived, name)),
                name);
        }
    }

    [Fact]
    public void AuthenticatedObjectSlotOverride_DeclinesOverrideOfExternalBase()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ExternalOverride));
        var derived = Reader.GetTypeDefinition(derivedHandle);

        // System.Exception declares its own ToString slot, so local metadata
        // cannot tell whether this method occupies Object's slot or the
        // external base's; a flattening consumer must not rebind it.
        Assert.Equal(HandleKind.TypeReference, derived.BaseType.Kind);
        Assert.False(
            MetadataDeclarationQuery.IsAuthenticatedObjectSlotOverride(
                Reader,
                derivedHandle,
                GetMethodHandle(derived, "ToString")));
    }

    [Fact]
    public void AuthenticatedObjectSlotOverride_DeclinesNewSlotObjectShapedVirtual()
    {
        var declarerHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .NewSlotToStringDeclarer));
        var declarer = Reader.GetTypeDefinition(declarerHandle);

        Assert.False(
            MetadataDeclarationQuery.IsAuthenticatedObjectSlotOverride(
                Reader,
                declarerHandle,
                GetMethodHandle(declarer, "ToString")));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsExplicitGenericBaseConstraint()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ExplicitConstraintCovariantReturnDerived<>));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.AnimalCovariantReturnBase)),
            slot.DeclaringType);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsExplicitClassConstrainedGenericArrayCovariance()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ExplicitConstraintArrayCovariantReturnDerived<>));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Values");

        var slot = Assert.IsType<MetadataOverrideSlot>(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));

        Assert.Equal(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.AnimalArrayCovariantReturnBase)),
            slot.DeclaringType);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesInterfaceConstrainedGenericArrayCovariance()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterCovarianceImage(
                referenceTypeConstraint: false,
                explicitConstraint: GenericConstraintTarget.Interface,
                baseReturnsDog: false,
                wrapReturnInArray: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesArrayCovarianceWhenExplicitConstraintDoesNotReachReturn()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterCovarianceImage(
                referenceTypeConstraint: false,
                explicitConstraint: GenericConstraintTarget.Animal,
                baseReturnsDog: true,
                wrapReturnInArray: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsCovariantConstructedConstraint()
    {
        using var stream = new MemoryStream(
            BuildConstructedConstraintImage(
                GenericParameterAttributes.Covariant));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesInvariantConstructedConstraint()
    {
        using var stream = new MemoryStream(
            BuildConstructedConstraintImage(
                GenericParameterAttributes.None));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesExplicitConstraintThatDoesNotReachReturn()
    {
        using var stream = new MemoryStream(
            BuildGenericParameterCovarianceImage(
                referenceTypeConstraint: false,
                explicitConstraint: GenericConstraintTarget.Animal,
                baseReturnsDog: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsCompilerProducedNestedGenericVariance()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.NestedVariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Value");

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameAssemblyOverrideSlot_UsesExactNestedGenericVarianceDefinition(
        bool nestedIsCovariant)
    {
        using var stream = new MemoryStream(
            BuildNestedVarianceCollisionImage(
                nestedIsCovariant));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        MetadataOverrideSlot? slot =
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle);

        Assert.Equal(nestedIsCovariant, slot is not null);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesAmbiguousExactLocalGenericDefinition()
    {
        using var stream = new MemoryStream(
            BuildNestedVarianceCollisionImage(
                nestedIsCovariant: true,
                duplicateExactDefinition: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesArrayWrappedAmbiguousExactLocalGenericDefinition()
    {
        using var stream = new MemoryStream(
            BuildNestedVarianceCollisionImage(
                nestedIsCovariant: true,
                duplicateExactDefinition: true,
                wrapReturnsInArray: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsModifierWrappedExactLocalGenericCovariance()
    {
        using var stream = new MemoryStream(
            BuildNestedVarianceCollisionImage(
                nestedIsCovariant: true,
                wrapReturnsInOptionalModifier: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesModifierWrappedAmbiguousExactLocalGenericDefinition()
    {
        using var stream = new MemoryStream(
            BuildNestedVarianceCollisionImage(
                nestedIsCovariant: true,
                duplicateExactDefinition: true,
                wrapReturnsInOptionalModifier: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesPinnedWrappedAmbiguousExactLocalGenericDefinition()
    {
        using var stream = new MemoryStream(
            BuildNestedVarianceCollisionImage(
                nestedIsCovariant: true,
                duplicateExactDefinition: true,
                wrapReturnsPinned: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsCompilerProducedExternalGenericCovariance()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ExternalGenericCovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Values");

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameAssemblyOverrideSlot_DoesNotUseLocalVarianceForExternalGeneric(
        bool localShadowIsCovariant)
    {
        using var stream = new MemoryStream(
            BuildExternalGenericShadowImage(
                localShadowIsCovariant));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsCompilerProducedExternalConstructedConstraintVariance()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.ExternalConstructedConstraintCovariantReturnDerived<>));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "Values");

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesExternalConstructedConstraintForNonGenericCandidate()
    {
        using var stream = new MemoryStream(
            BuildExternalConstructedConstraintImage(
                genericCandidate: false));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(
        typeof(MetadataDeclarationQueryFixtures.CovariantPropertyDerived),
        "Value",
        "get_Value")]
    [InlineData(
        typeof(MetadataDeclarationQueryFixtures.CovariantIndexerDerived),
        "Item",
        "get_Item")]
    public void PropertyDeclaration_UsesCompilerProducedCovariantMethodImpl(
        Type derivedType,
        string propertyName,
        string accessorName)
    {
        var derivedHandle = GetTypeDefinitionHandle(derivedType);
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var property = GetProperty(derived, propertyName);
        var accessorHandle = GetMethodHandle(derived, accessorName);
        var accessor = Reader.GetMethodDefinition(accessorHandle);

        Assert.True((accessor.Attributes & MethodAttributes.NewSlot) != 0);
        Assert.Contains(
            derived.GetMethodImplementations()
                .Select(Reader.GetMethodImplementation),
            implementation => implementation.MethodBody == accessorHandle);

        var declaration =
            MetadataDeclarationQuery.GetProperty(
                Reader,
                derived,
                property);

        Assert.True(declaration.IsOverride);
        Assert.False(declaration.IsVirtual);
    }

    [Fact]
    public void PropertyDeclaration_SelfBasePropertyCycleFailsClosed()
    {
        using var stream = new MemoryStream(
            BuildPropertyOverrideCycleImage(
                PropertyOverrideCycleKind.SelfBase));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "Loop");
        var type = reader.GetTypeDefinition(typeHandle);
        var property = GetProperty(type, "Value", reader);

        var declaration = MetadataDeclarationQuery.GetProperty(
            reader,
            type,
            property);

        Assert.Equal("protected", declaration.Accessibility);
    }

    [Fact]
    public void PropertyDeclaration_TwoTypePropertyCycleFailsClosed()
    {
        using var stream = new MemoryStream(
            BuildPropertyOverrideCycleImage(
                PropertyOverrideCycleKind.TwoTypeCycle));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "First");
        var type = reader.GetTypeDefinition(typeHandle);
        var property = GetProperty(type, "Value", reader);

        var declaration = MetadataDeclarationQuery.GetProperty(
            reader,
            type,
            property);

        Assert.Equal("protected", declaration.Accessibility);
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesInterfaceMethodImpl()
    {
        string path = EmitNewSlotInterfaceMethodImpl();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var typeHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Implementation");
            var type = reader.GetTypeDefinition(typeHandle);
            var methodHandle = type.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == "Value");

            Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                typeHandle,
                methodHandle));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameAssemblyOverrideSlot_DeclinesUnauthenticatedClassMethodImpl(
        bool ambiguousBaseDeclarations)
    {
        using var stream = new MemoryStream(
            BuildUnauthenticatedClassMethodImplImage(ambiguousBaseDeclarations));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "Derived");
        var type = reader.GetTypeDefinition(typeHandle);
        var methodHandle = Assert.Single(type.GetMethods());

        Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            reader,
            typeHandle,
            methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesRefOutModifierMismatch()
    {
        string path = EmitRefOutKindMismatchOverride();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var derivedHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Derived");
            var derived = reader.GetTypeDefinition(derivedHandle);
            var methodHandle = derived.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == "M");

            Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesStaticMethod()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.StaticShadowDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);
        var methodHandle = GetMethodHandle(derived, "M");

        Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
            Reader,
            derivedHandle,
            methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesIncompatibleCovariantReturn()
    {
        string path = EmitIncompatibleReturnOverride();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var derivedHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Derived");
            var derived = reader.GetTypeDefinition(derivedHandle);
            var methodHandle = derived.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == "Value");

            Assert.Null(MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameAssemblyOverrideSlot_DeclinesIncompatibleStructuredReturn(
        bool arrayReturn)
    {
        string path =
            EmitIncompatibleStructuredReturnOverride(
                arrayReturn);
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var derivedHandle =
                reader.TypeDefinitions.Single(handle =>
                    reader.GetString(
                        reader.GetTypeDefinition(handle).Name)
                    == "Derived");
            var derived =
                reader.GetTypeDefinition(derivedHandle);
            var methodHandle =
                derived.GetMethods().Single(handle =>
                    reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                    == "Value");

            Assert.Null(
                MetadataDeclarationQuery
                    .GetSameAssemblyOverrideSlot(
                        reader,
                        derivedHandle,
                        methodHandle));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsMultiDimensionalArrayCovarianceWhenShapeMatches()
    {
        using var stream = new MemoryStream(
            BuildMultiDimensionalArrayCovarianceImage(
                differentShape: false));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesDifferentMultiDimensionalArrayShape()
    {
        using var stream = new MemoryStream(
            BuildMultiDimensionalArrayCovarianceImage(
                differentShape: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void StaticAbstractInterfaceMethod_IsNotClassifiedAsOverride()
    {
        var type = GetTypeDefinition(
            typeof(MetadataDeclarationQueryFixtures.IStaticContract));
        var method = GetMethod(type, "Create");

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        Assert.True(declaration.IsAbstract);
        Assert.False(declaration.IsOverride);
        Assert.False(declaration.IsSealed);
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

        var surfaceMethodAccessibility = typeof(ApiSurfaceExtractor).GetMethod(
            "GetAccessibility",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(MethodAttributes)]);
        var surfaceFieldAccessibility = typeof(ApiSurfaceExtractor).GetMethod(
            "GetFieldAccessibility",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(FieldAttributes)]);

        Assert.Equal(
            "private",
            surfaceMethodAccessibility!.Invoke(
                null,
                [MethodAttributes.PrivateScope]));
        Assert.Equal(
            "private",
            surfaceFieldAccessibility!.Invoke(
                null,
                [FieldAttributes.PrivateScope]));
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

    enum ScopedSignatureCollision
    {
        CrossAssemblyReturn,
        NestedVsNamespaceParameter,
    }

    enum GenericConstraintTarget
    {
        None,
        Animal,
        Dog,
        Interface,
    }

    enum PropertyOverrideCycleKind
    {
        SelfBase,
        TwoTypeCycle,
    }

    static (
        TypeDefinitionHandle Derived,
        MethodDefinitionHandle Method)
        GetSyntheticDerivedMethod(MetadataReader reader)
    {
        TypeDefinitionHandle derived =
            reader.TypeDefinitions.Single(handle =>
                reader.GetString(
                    reader.GetTypeDefinition(handle).Name)
                == "Derived");
        return (
            derived,
            Assert.Single(
                reader.GetTypeDefinition(derived).GetMethods()));
    }

    enum ConstructedGenericMethodImplShape
    {
        MatchingInstantiation,
        MismatchedInstantiation,
        ExternalDefinition,
    }

    /// <summary>
    /// Builds <c>Derived : Base&lt;Animal&gt;</c> where <c>Base&lt;T&gt;</c>
    /// declares <c>T Value()</c> and <c>Derived</c> declares
    /// <c>Dog Value()</c> with a class <c>MethodImpl</c> whose declaration is a
    /// <c>MemberRef</c> rooted in a <c>TypeSpec</c>. The shape controls which
    /// instantiation that <c>MemberRef</c> names, so a match can only come from
    /// substituting the exact base arguments rather than from the member's
    /// spelling.
    /// </summary>
    static byte[] BuildConstructedGenericMethodImplImage(
        ConstructedGenericMethodImplShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("ConstructedGenericMethodImpl");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle externalBase =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("External"),
                metadata.GetOrAddString("Base`1"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base`1"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        TypeSpecificationHandle extendsSpec =
            shape == ConstructedGenericMethodImplShape.ExternalDefinition
                ? AddSyntheticConstructedTypeSpec(
                    metadata,
                    externalBase,
                    animal)
                : AddSyntheticConstructedTypeSpec(
                    metadata,
                    baseType,
                    animal);
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                extendsSpec,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        metadata.AddGenericParameter(
            baseType,
            GenericParameterAttributes.ReferenceTypeConstraint,
            metadata.GetOrAddString("T"),
            index: 0);

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            AddSyntheticGenericParameterReturnSignature(metadata),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMethodSignature(metadata, dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));

        EntityHandle declarationParent = shape switch
        {
            ConstructedGenericMethodImplShape.MismatchedInstantiation =>
                AddSyntheticConstructedTypeSpec(metadata, baseType, dog),
            ConstructedGenericMethodImplShape.ExternalDefinition =>
                extendsSpec,
            _ => extendsSpec,
        };
        MemberReferenceHandle declaration =
            metadata.AddMemberReference(
                declarationParent,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericParameterReturnSignature(metadata));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            declaration);
        return SerializeSyntheticMetadata(metadata);
    }

    enum GenericParameterConstraintGraph
    {
        Chain,
        Dag,
    }

    public enum ConstructedGenericAncestryShape
    {
        MatchingArgument,
        DifferentArgument,
        Cyclic,
        Expanding,
        BranchingExpansion,
    }

    /// <summary>
    /// Builds <c>Derived : Base</c> whose <c>Dog Value()</c> carries a class
    /// <c>MethodImpl</c> onto a base slot, where the only ancestry evidence for
    /// the covariant return runs through <c>Dog</c>'s own constructed generic
    /// base. The shape controls that base and the declared return, so a
    /// definition-only ancestry answer and an instantiation-exact one differ:
    /// <see cref="ConstructedGenericAncestryShape.MatchingArgument"/> declares
    /// <c>Middle&lt;Dog&gt;</c> against <c>Dog : Middle&lt;Dog&gt;</c> and
    /// <see cref="ConstructedGenericAncestryShape.DifferentArgument"/> declares
    /// the same return against <c>Dog : Middle&lt;Animal&gt;</c>. The remaining
    /// shapes declare an unreachable <c>Animal</c> return over a cyclic and an
    /// endlessly expanding base chain.
    /// </summary>
    static byte[] BuildConstructedGenericAncestryImage(
        ConstructedGenericAncestryShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("ConstructedGenericAncestry");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        // TypeSpec rows have to name types whose definitions are added later,
        // so the row numbers are fixed up front and checked on the way out.
        TypeDefinitionHandle animal =
            MetadataTokens.TypeDefinitionHandle(2);
        TypeDefinitionHandle middle =
            MetadataTokens.TypeDefinitionHandle(3);
        TypeDefinitionHandle dog =
            MetadataTokens.TypeDefinitionHandle(4);
        TypeDefinitionHandle pair =
            MetadataTokens.TypeDefinitionHandle(5);

        EntityHandle middleBase = shape switch
        {
            ConstructedGenericAncestryShape.Cyclic => dog,
            ConstructedGenericAncestryShape.Expanding =>
                AddSyntheticNestedSelfInstantiationTypeSpec(
                    metadata,
                    middle),
            ConstructedGenericAncestryShape.BranchingExpansion =>
                AddSyntheticBranchingSelfInstantiationTypeSpec(
                    metadata,
                    middle,
                    pair),
            _ => objectType,
        };
        EntityHandle dogBase = AddSyntheticConstructedTypeSpec(
            metadata,
            middle,
            shape == ConstructedGenericAncestryShape.DifferentArgument
                ? animal
                : dog);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        Assert.Equal(
            animal,
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(
            middle,
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Middle`1"),
                middleBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1)));
        Assert.Equal(
            dog,
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                dogBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1)));
        if (shape == ConstructedGenericAncestryShape.BranchingExpansion)
        {
            Assert.Equal(
                pair,
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    default,
                    metadata.GetOrAddString("Pair`2"),
                    objectType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1)));
        }

        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        metadata.AddGenericParameter(
            middle,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        if (shape == ConstructedGenericAncestryShape.BranchingExpansion)
        {
            metadata.AddGenericParameter(
                pair,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TFirst"),
                index: 0);
            metadata.AddGenericParameter(
                pair,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TSecond"),
                index: 1);
        }

        BlobHandle declaredReturn = shape
            is ConstructedGenericAncestryShape.MatchingArgument
            or ConstructedGenericAncestryShape.DifferentArgument
            ? AddSyntheticGenericReturnSignature(metadata, middle, dog)
            : AddSyntheticMethodSignature(metadata, animal);
        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                declaredReturn,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMethodSignature(metadata, dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    /// <summary>
    /// The shapes of one generic parameter's constraint set: a valid explicit
    /// constraint that proves the covariant return, an over-deep
    /// <c>TypeSpec</c> constraint the shared signature guard refuses to decode,
    /// and both together in either metadata order.
    /// </summary>
    public enum DegradedConstraintSetShape
    {
        ValidOnly,
        DegradedOnly,
        DegradedFirst,
        ValidFirst,
    }

    /// <summary>
    /// Builds <c>Derived&lt;T&gt; : Base</c> whose <c>T Value()</c> carries a
    /// class <c>MethodImpl</c> onto <c>Animal Value()</c>, with <c>T</c>
    /// constrained by the requested mix of a valid <c>Dog</c> constraint and an
    /// undecodable over-deep <c>TypeSpec</c> constraint.
    /// </summary>
    static byte[] BuildDegradedConstraintSetImage(
        DegradedConstraintSetShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("DegradedConstraintSet");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                derivedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);

        foreach (bool degraded in ConstraintOrder(shape))
        {
            metadata.AddGenericParameterConstraint(
                parameter,
                degraded
                    ? AddSyntheticOverDeepArrayTypeSpec(
                        metadata,
                        animal,
                        depth: SignatureBlobGuard.DefaultMaxDepth + 64)
                    : dog);
        }

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMethodSignature(metadata, animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericParameterReturnSignature(metadata),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static IEnumerable<bool> ConstraintOrder(
        DegradedConstraintSetShape shape)
        => shape switch
        {
            DegradedConstraintSetShape.ValidOnly => [false],
            DegradedConstraintSetShape.DegradedOnly => [true],
            DegradedConstraintSetShape.DegradedFirst => [true, false],
            _ => [false, true],
        };

    /// <summary>
    /// A <c>TypeSpec</c> nesting <paramref name="depth"/> SZ arrays around
    /// <paramref name="elementType"/>, which exceeds
    /// <see cref="SignatureBlobGuard.DefaultMaxDepth"/> so every guarded decode
    /// of it degrades.
    /// </summary>
    static TypeSpecificationHandle AddSyntheticOverDeepArrayTypeSpec(
        MetadataBuilder metadata,
        EntityHandle elementType,
        int depth)
    {
        var blob = new BlobBuilder();
        SignatureTypeEncoder encoder =
            new BlobEncoder(blob).TypeSpecificationSignature();
        for (int index = 0; index < depth; index++)
            encoder = encoder.SZArray();
        encoder.Type(elementType, isValueType: false);
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(blob));
    }

    /// <summary>
    /// A <c>TypeSpec</c> for <c>Definition&lt;Definition&lt;!0&gt;&gt;</c>,
    /// which as a base row makes every ancestry step produce a new
    /// instantiation that no visited set can ever repeat.
    /// </summary>
    static TypeSpecificationHandle
        AddSyntheticNestedSelfInstantiationTypeSpec(
            MetadataBuilder metadata,
            EntityHandle definition)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .TypeSpecificationSignature()
            .GenericInstantiation(
                definition,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .GenericInstantiation(
                definition,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .GenericTypeParameter(0);
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(blob));
    }

    /// <summary>
    /// Builds <c>Derived&lt;T0..Tn&gt; : Base</c> whose <c>Value()</c> returns
    /// <c>!0</c> against a base slot returning <c>Animal</c>, with a
    /// type-parameter constraint graph no path of which reaches <c>Animal</c>.
    /// A chain forces one recursion per link; a DAG whose every parameter is
    /// constrained by its next two neighbours has a path count that grows like
    /// the Fibonacci numbers, so neither terminates in reasonable time or
    /// stack without a cumulative bound.
    /// </summary>
    static byte[] BuildGenericParameterConstraintGraphImage(
        int parameterCount,
        GenericParameterConstraintGraph graph)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("GenericParameterConstraintGraph");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        var parameters = new GenericParameterHandle[parameterCount];
        for (int index = 0; index < parameterCount; index++)
        {
            parameters[index] = metadata.AddGenericParameter(
                derivedType,
                GenericParameterAttributes.ReferenceTypeConstraint,
                metadata.GetOrAddString(
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"T{index}")),
                index);
        }

        var parameterSpecs = new TypeSpecificationHandle[parameterCount];
        for (int index = 0; index < parameterCount; index++)
        {
            parameterSpecs[index] =
                AddSyntheticGenericParameterTypeSpec(metadata, index);
        }

        int neighbours =
            graph == GenericParameterConstraintGraph.Dag ? 2 : 1;
        for (int index = 0; index < parameterCount; index++)
        {
            for (int step = 1; step <= neighbours; step++)
            {
                int target = index + step;
                if (target >= parameterCount)
                    continue;

                metadata.AddGenericParameterConstraint(
                    parameters[index],
                    parameterSpecs[target]);
            }
        }

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMethodSignature(metadata, animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericParameterReturnSignature(metadata),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static TypeSpecificationHandle AddSyntheticConstructedTypeSpec(
        MetadataBuilder metadata,
        EntityHandle definition,
        EntityHandle argument)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .TypeSpecificationSignature()
            .GenericInstantiation(
                definition,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .Type(argument, isValueType: false);
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(blob));
    }

    static TypeSpecificationHandle AddSyntheticGenericParameterTypeSpec(
        MetadataBuilder metadata,
        int index)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .TypeSpecificationSignature()
            .GenericTypeParameter(index);
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(blob));
    }

    static byte[] BuildScopedSignatureCollisionImage(
        ScopedSignatureCollision collision)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "ScopedSignatureCollision");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        AssemblyReferenceHandle firstAssembly =
            AddSyntheticAssemblyReference(
                metadata,
                "CollisionA");
        AssemblyReferenceHandle secondAssembly =
            AddSyntheticAssemblyReference(
                metadata,
                "CollisionB");

        EntityHandle baseReturn = default;
        EntityHandle derivedReturn = default;
        EntityHandle baseParameter = default;
        EntityHandle derivedParameter = default;
        int parameterCount = 0;
        if (collision
            == ScopedSignatureCollision.CrossAssemblyReturn)
        {
            baseReturn = metadata.AddTypeReference(
                firstAssembly,
                metadata.GetOrAddString("Collision"),
                metadata.GetOrAddString("Value"));
            derivedReturn = metadata.AddTypeReference(
                secondAssembly,
                metadata.GetOrAddString("Collision"),
                metadata.GetOrAddString("Value"));
        }
        else
        {
            TypeReferenceHandle outer =
                metadata.AddTypeReference(
                    firstAssembly,
                    metadata.GetOrAddString("Collision"),
                    metadata.GetOrAddString("Outer"));
            baseParameter = metadata.AddTypeReference(
                firstAssembly,
                metadata.GetOrAddString(
                    "Collision.Outer"),
                metadata.GetOrAddString("Value"));
            derivedParameter = metadata.AddTypeReference(
                outer,
                default,
                metadata.GetOrAddString("Value"));
            parameterCount = 1;
        }

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        BlobHandle baseSignature =
            AddSyntheticMethodSignature(
                metadata,
                baseReturn,
                baseParameter,
                parameterCount);
        BlobHandle derivedSignature =
            AddSyntheticMethodSignature(
                metadata,
                derivedReturn,
                derivedParameter,
                parameterCount);
        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                baseSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                derivedSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static byte[] BuildConstructedConstraintImage(
        GenericParameterAttributes variance)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "InvariantConstructedConstraint");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle invariant =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("IInvariant`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            invariant,
            variance,
            metadata.GetOrAddString("TValue"),
            index: 0);
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                derivedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        TypeSpecificationHandle dogContainer =
            AddSyntheticGenericTypeSpecification(
                metadata,
                invariant,
                dog);
        metadata.AddGenericParameterConstraint(
            parameter,
            dogContainer);

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericReturnSignature(
                    metadata,
                    invariant,
                    animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericParameterReturnSignature(
                    metadata),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static byte[] BuildGenericParameterCovarianceImage(
        bool referenceTypeConstraint,
        GenericConstraintTarget explicitConstraint,
        bool baseReturnsDog,
        bool wrapReturnInArray = false)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "GenericParameterCovariance");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle markerInterface =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("IConstraint"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                derivedType,
                referenceTypeConstraint
                    ? GenericParameterAttributes
                        .ReferenceTypeConstraint
                    : GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        EntityHandle explicitConstraintType =
            explicitConstraint switch
            {
                GenericConstraintTarget.Animal => animal,
                GenericConstraintTarget.Dog => dog,
                GenericConstraintTarget.Interface => markerInterface,
                _ => default,
            };
        if (!explicitConstraintType.IsNil)
        {
            metadata.AddGenericParameterConstraint(
                parameter,
                explicitConstraintType);
        }

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                wrapReturnInArray
                    ? AddSyntheticArrayReturnSignature(
                        metadata,
                        baseReturnsDog ? dog : animal)
                    : AddSyntheticMethodSignature(
                        metadata,
                        baseReturnsDog ? dog : animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                wrapReturnInArray
                    ? AddSyntheticGenericParameterArrayReturnSignature(
                        metadata)
                    : AddSyntheticGenericParameterReturnSignature(
                        metadata),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static byte[] BuildNestedVarianceCollisionImage(
        bool nestedIsCovariant,
        bool duplicateExactDefinition = false,
        bool wrapReturnsInArray = false,
        bool wrapReturnsInOptionalModifier = false,
        bool wrapReturnsPinned = false)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "NestedVarianceCollision");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle modifierType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IsVolatile"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle flattenedDecoy =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString(
                    "Collision.Outer"),
                metadata.GetOrAddString("Variant`2"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Collision"),
                metadata.GetOrAddString("Outer`1"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nested =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("Variant`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(nested, outer);
        TypeDefinitionHandle duplicateNested =
            duplicateExactDefinition
                ? metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract,
                    default,
                    metadata.GetOrAddString("Variant`1"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1))
                : default;
        if (!duplicateNested.IsNil)
            metadata.AddNestedType(duplicateNested, outer);
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        metadata.AddGenericParameter(
            flattenedDecoy,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            index: 0);
        metadata.AddGenericParameter(
            flattenedDecoy,
            nestedIsCovariant
                ? GenericParameterAttributes.None
                : GenericParameterAttributes.Covariant,
            metadata.GetOrAddString("TValue"),
            index: 1);
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            index: 0);
        metadata.AddGenericParameter(
            nested,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            index: 0);
        metadata.AddGenericParameter(
            nested,
            nestedIsCovariant
                ? GenericParameterAttributes.Covariant
                : GenericParameterAttributes.None,
            metadata.GetOrAddString("TValue"),
            index: 1);
        if (!duplicateNested.IsNil)
        {
            metadata.AddGenericParameter(
                duplicateNested,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TOuter"),
                index: 0);
            metadata.AddGenericParameter(
                duplicateNested,
                GenericParameterAttributes.Covariant,
                metadata.GetOrAddString("TValue"),
                index: 1);
        }

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                CreateNestedVarianceReturnSignature(
                    objectType,
                    animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                CreateNestedVarianceReturnSignature(
                    objectType,
                    dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);

        BlobHandle CreateNestedVarianceReturnSignature(
            EntityHandle outerArgument,
            EntityHandle valueArgument)
        {
            if (wrapReturnsInOptionalModifier)
            {
                return AddSyntheticModifiedGenericReturnSignature(
                    metadata,
                    modifierType,
                    isRequired: false,
                    nested,
                    outerArgument,
                    valueArgument);
            }

            if (wrapReturnsPinned)
            {
                return AddSyntheticPinnedGenericReturnSignature(
                    metadata,
                    nested,
                    outerArgument,
                    valueArgument);
            }

            return wrapReturnsInArray
                ? AddSyntheticGenericArrayReturnSignature(
                    metadata,
                    nested,
                    outerArgument,
                    valueArgument)
                : AddSyntheticGenericReturnSignature(
                    metadata,
                    nested,
                    outerArgument,
                    valueArgument);
        }
    }

    static byte[] BuildExternalGenericShadowImage(
        bool localShadowIsCovariant)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "ExternalGenericShadow");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        AssemblyReferenceHandle external =
            AddSyntheticAssemblyReference(
                metadata,
                "ExternalContracts");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle externalVariant =
            metadata.AddTypeReference(
                external,
                metadata.GetOrAddString("External"),
                metadata.GetOrAddString("Variant`1"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle localShadow =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("External"),
                metadata.GetOrAddString("Variant`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddGenericParameter(
            localShadow,
            localShadowIsCovariant
                ? GenericParameterAttributes.Covariant
                : GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericReturnSignature(
                    metadata,
                    externalVariant,
                    animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericReturnSignature(
                    metadata,
                    externalVariant,
                    dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static byte[] BuildExternalConstructedConstraintImage(
        bool genericCandidate)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "ExternalConstructedConstraint");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle enumerable =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System.Collections.Generic"),
                metadata.GetOrAddString("IEnumerable`1"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                derivedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            parameter,
            AddSyntheticGenericTypeSpecification(
                metadata,
                enumerable,
                dog));

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                genericCandidate
                    ? AddSyntheticGenericReturnSignature(
                        metadata,
                        enumerable,
                        animal)
                    : AddSyntheticMethodSignature(
                        metadata,
                        animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericParameterReturnSignature(
                    metadata),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static byte[] BuildMultiDimensionalArrayCovarianceImage(
        bool differentShape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                "MdArrayCovariance");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMdArrayReturnSignature(
                    metadata,
                    animal,
                    rank: 2,
                    sizes: [4],
                    lowerBounds: [1]),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMdArrayReturnSignature(
                    metadata,
                    dog,
                    rank: 2,
                    sizes: [4],
                    lowerBounds: differentShape
                        ? [0]
                        : [1]),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    static byte[] BuildPropertyOverrideCycleImage(
        PropertyOverrideCycleKind kind)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata(
                $"PropertyOverrideCycle{kind}");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(
                metadata,
                "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        if (kind == PropertyOverrideCycleKind.SelfBase)
        {
            ParameterHandle valueParameter =
                metadata.AddParameter(
                    ParameterAttributes.None,
                    metadata.GetOrAddString("value"),
                    sequenceNumber: 1);
            TypeDefinitionHandle loop =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    default,
                    metadata.GetOrAddString("Loop"),
                    MetadataTokens.TypeDefinitionHandle(2),
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            MethodDefinitionHandle setter =
                metadata.AddMethodDefinition(
                    MethodAttributes.Family
                        | MethodAttributes.Virtual
                        | MethodAttributes.SpecialName
                        | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("set_Value"),
                    AddSyntheticSetterSignature(
                        metadata),
                    bodyOffset: -1,
                    valueParameter);
            PropertyDefinitionHandle property =
                metadata.AddProperty(
                    PropertyAttributes.None,
                    metadata.GetOrAddString("Value"),
                    AddSyntheticPropertySignature(
                        metadata));
            metadata.AddPropertyMap(loop, property);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Setter,
                setter);
            return SerializeSyntheticMetadata(metadata);
        }

        ParameterHandle firstParameter =
            metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
        ParameterHandle secondParameter =
            metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
        TypeDefinitionHandle first =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("First"),
                MetadataTokens.TypeDefinitionHandle(3),
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle second =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Second"),
                MetadataTokens.TypeDefinitionHandle(2),
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        MethodDefinitionHandle firstSetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Family
                    | MethodAttributes.Virtual
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("set_Value"),
                AddSyntheticSetterSignature(
                    metadata),
                bodyOffset: -1,
                firstParameter);
        MethodDefinitionHandle secondSetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Family
                    | MethodAttributes.Virtual
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("set_Value"),
                AddSyntheticSetterSignature(
                    metadata),
                bodyOffset: -1,
                secondParameter);
        PropertyDefinitionHandle firstProperty =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                AddSyntheticPropertySignature(
                    metadata));
        PropertyDefinitionHandle secondProperty =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                AddSyntheticPropertySignature(
                    metadata));
        metadata.AddPropertyMap(first, firstProperty);
        metadata.AddPropertyMap(second, secondProperty);
        metadata.AddMethodSemantics(
            firstProperty,
            MethodSemanticsAttributes.Setter,
            firstSetter);
        metadata.AddMethodSemantics(
            secondProperty,
            MethodSemanticsAttributes.Setter,
            secondSetter);
        return SerializeSyntheticMetadata(metadata);
    }

    // ---- Round-27 authentication gates ----------------------------------

    [Theory]
    [InlineData(ReferenceConstraintAuthenticationShape.FlagOnly)]
    [InlineData(
        ReferenceConstraintAuthenticationShape.FlagWithValidConstraint)]
    public void SameAssemblyOverrideSlot_AllowsAuthenticatedReferenceConstraintForObjectReturn(
        ReferenceConstraintAuthenticationShape shape)
    {
        using var stream = new MemoryStream(
            BuildReferenceConstraintAuthenticationImage(
                shape,
                arrayReturn: false));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesDegradedSiblingConstraintForObjectReturn()
    {
        using var stream = new MemoryStream(
            BuildReferenceConstraintAuthenticationImage(
                ReferenceConstraintAuthenticationShape
                    .FlagWithDegradedConstraint,
                arrayReturn: false));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsReferenceFlagOnlyConstraintForArrayCovariance()
    {
        using var stream = new MemoryStream(
            BuildReferenceConstraintAuthenticationImage(
                ReferenceConstraintAuthenticationShape.FlagOnly,
                arrayReturn: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesDegradedSiblingConstraintForArrayCovariance()
    {
        using var stream = new MemoryStream(
            BuildReferenceConstraintAuthenticationImage(
                ReferenceConstraintAuthenticationShape
                    .FlagWithDegradedConstraint,
                arrayReturn: true));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_BranchingConstructedAncestryFailsClosed()
    {
        using var stream = new MemoryStream(
            BuildConstructedGenericAncestryImage(
                ConstructedGenericAncestryShape.BranchingExpansion));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    /// <summary>
    /// A base that squares its own instantiation each ancestry step expands the
    /// exact structural identity exponentially, which exhausted hundreds of
    /// megabytes before the node ceiling was ever reached. The child process
    /// runs under a hard GC heap limit far below what the unbounded expansion
    /// needed, so the containment is proved by the child completing rather than
    /// by a managed catch, which an <see cref="OutOfMemoryException"/> of that
    /// shape does not reliably permit.
    /// </summary>
    [Fact]
    public void SameAssemblyOverrideSlot_BranchingConstructedAncestryFailsClosedWithinMemory()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(
            typeof(MetadataDeclarationQueryTests).Assembly.Location);
        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add(
            $"*{nameof(BranchingConstructedAncestryWorker)}*");
        startInfo.Environment[BranchingAncestryWorkerVariable] =
            nameof(BranchingConstructedAncestryWorker);
        startInfo.Environment["DOTNET_GCHeapHardLimit"] = "0x10000000";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"The branching-ancestry worker exited {process.ExitCode}.\n"
                + $"stdout:\n{standardOutput}\n"
                + $"stderr:\n{standardError}");
    }

    /// <summary>
    /// Runs inside the child process started by
    /// <see cref="SameAssemblyOverrideSlot_BranchingConstructedAncestryFailsClosedWithinMemory"/>.
    /// The exact-instantiation control runs in the same constrained process, so
    /// a bound that declined everything could not pass this worker.
    /// </summary>
    [Fact]
    public void BranchingConstructedAncestryWorker()
    {
        if (Environment.GetEnvironmentVariable(
                BranchingAncestryWorkerVariable)
            != nameof(BranchingConstructedAncestryWorker))
        {
            return;
        }

        AssertConstructedAncestry(
            ConstructedGenericAncestryShape.BranchingExpansion,
            authenticated: false);
        AssertConstructedAncestry(
            ConstructedGenericAncestryShape.MatchingArgument,
            authenticated: true);

        static void AssertConstructedAncestry(
            ConstructedGenericAncestryShape shape,
            bool authenticated)
        {
            using var stream = new MemoryStream(
                BuildConstructedGenericAncestryImage(shape));
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var (derivedHandle, methodHandle) =
                GetSyntheticDerivedMethod(reader);

            MetadataOverrideSlot? slot =
                MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                    reader,
                    derivedHandle,
                    methodHandle);
            if (authenticated)
                Assert.NotNull(slot);
            else
                Assert.Null(slot);
        }
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsExactLocalReferenceReturnAgainstObjectDeclaration()
    {
        var derivedHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures
                .LocalReferenceObjectCovariantReturnDerived));
        var derived = Reader.GetTypeDefinition(derivedHandle);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                Reader,
                derivedHandle,
                GetMethodHandle(derived, "Value")));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsSyntheticExactLocalReferenceReturnAgainstObjectDeclaration()
    {
        using var stream = new MemoryStream(
            BuildObjectReturnConversionImage(
                ObjectReturnConversionShape.ExactLocalReference));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesModifiedReturnAgainstObjectDeclaration()
    {
        using var stream = new MemoryStream(
            BuildObjectReturnConversionImage(
                ObjectReturnConversionShape.ModifiedLocalReference));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesAmbiguousExactLocalReturnAgainstObjectDeclaration()
    {
        using var stream = new MemoryStream(
            BuildObjectReturnConversionImage(
                ObjectReturnConversionShape.AmbiguousLocalReference));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AuthenticatesInterfaceAncestryThroughInterfaceImplementation()
    {
        using var stream = new MemoryStream(
            BuildSupertypeRoleImage(
                SupertypeRoleShape.ValidInterfaceImplementation));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesInterfaceImplementationNamingAClass()
    {
        using var stream = new MemoryStream(
            BuildSupertypeRoleImage(
                SupertypeRoleShape.InterfaceImplementationNamesClass));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_DeclinesBaseTypeNamingAnInterface()
    {
        using var stream = new MemoryStream(
            BuildSupertypeRoleImage(
                SupertypeRoleShape.BaseTypeNamesInterface));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void ReusesInheritedVirtualSlot_DeclinesExplicitInterfaceOnlyMethodImpl()
    {
        var type = Reader.GetTypeDefinition(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures
                    .ExplicitInterfaceOnlyImplementation)));

        // The MethodImpl is present; what is absent is any inherited class
        // virtual slot for it to reuse.
        Assert.NotEmpty(type.GetMethodImplementations());
        Assert.False(
            MetadataDeclarationQuery.ReusesInheritedVirtualSlot(
                Reader,
                type));
    }

    [Fact]
    public void ReusesInheritedVirtualSlot_AcceptsAuthenticatedClassMethodImpl()
    {
        var type = Reader.GetTypeDefinition(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures
                    .CovariantReturnDerived)));

        // A covariant return is newslot virtual final plus a MethodImpl, so the
        // attribute test alone cannot see the reuse.
        Assert.NotEmpty(type.GetMethodImplementations());
        Assert.True(
            MetadataDeclarationQuery.ReusesInheritedVirtualSlot(
                Reader,
                type));
    }

    [Fact]
    public void ReusesInheritedVirtualSlot_AcceptsInheritedVirtualSlotFlags()
    {
        var type = Reader.GetTypeDefinition(
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures
                    .PlainOverrideDerived)));

        Assert.Empty(type.GetMethodImplementations());
        Assert.True(
            MetadataDeclarationQuery.ReusesInheritedVirtualSlot(
                Reader,
                type));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsSourceDeclarableSignatureHeader()
    {
        using var stream = new MemoryStream(
            BuildOverrideSignatureHeaderImage(
                OverrideSignatureHeaderShape.SourceDeclarable));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(OverrideSignatureHeaderShape.VarArg)]
    [InlineData(OverrideSignatureHeaderShape.ExplicitThis)]
    [InlineData(OverrideSignatureHeaderShape.MissingHasThis)]
    public void SameAssemblyOverrideSlot_DeclinesMalformedSignatureHeader(
        OverrideSignatureHeaderShape shape)
    {
        // Both sides carry the same malformed header, so the refusal cannot
        // come from the two headers merely disagreeing.
        using var stream = new MemoryStream(
            BuildOverrideSignatureHeaderImage(shape));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void AuthenticatedObjectSlotOverride_AcceptsSourceDeclarableSignatureHeader()
    {
        using var stream = new MemoryStream(
            BuildObjectIntrinsicSignatureHeaderImage(
                OverrideSignatureHeaderShape.SourceDeclarable));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.True(
            MetadataDeclarationQuery.IsAuthenticatedObjectSlotOverride(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(OverrideSignatureHeaderShape.VarArg)]
    [InlineData(OverrideSignatureHeaderShape.ExplicitThis)]
    [InlineData(OverrideSignatureHeaderShape.MissingHasThis)]
    public void AuthenticatedObjectSlotOverride_DeclinesMalformedSignatureHeader(
        OverrideSignatureHeaderShape shape)
    {
        using var stream = new MemoryStream(
            BuildObjectIntrinsicSignatureHeaderImage(shape));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.False(
            MetadataDeclarationQuery.IsAuthenticatedObjectSlotOverride(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void SameAssemblyOverrideSlot_AllowsMatchingExternalConstruction()
    {
        using var stream = new MemoryStream(
            BuildExternalConstructedArityImage(
                ExternalConstructedArityShape.MatchingArity));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        // The construction is external, so local metadata cannot decide the
        // hierarchy: the slot is preserved for the compiler to validate.
        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(ExternalConstructedArityShape.MismatchedArgumentCount)]
    [InlineData(ExternalConstructedArityShape.MismatchedEncodedCount)]
    public void SameAssemblyOverrideSlot_DeclinesExternalConstructedArityMismatch(
        ExternalConstructedArityShape shape)
    {
        using var stream = new MemoryStream(
            BuildExternalConstructedArityImage(shape));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(VarianceOwnerKind.Interface)]
    [InlineData(VarianceOwnerKind.Delegate)]
    public void SameAssemblyOverrideSlot_AllowsVarianceOnEligibleOwner(
        VarianceOwnerKind kind)
    {
        using var stream = new MemoryStream(
            BuildVarianceOwnerKindImage(kind));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.NotNull(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Theory]
    [InlineData(VarianceOwnerKind.Class)]
    [InlineData(VarianceOwnerKind.ImpersonatingDelegate)]
    public void SameAssemblyOverrideSlot_DeclinesVarianceOnIneligibleOwner(
        VarianceOwnerKind kind)
    {
        using var stream = new MemoryStream(
            BuildVarianceOwnerKindImage(kind));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (derivedHandle, methodHandle) =
            GetSyntheticDerivedMethod(reader);

        Assert.Null(
            MetadataDeclarationQuery.GetSameAssemblyOverrideSlot(
                reader,
                derivedHandle,
                methodHandle));
    }

    [Fact]
    public void PropertyDeclaration_UsesUniquePropertyAssociationForAccessor()
    {
        Assert.Equal(
            "public",
            ResolveDuplicateAssociationAccessibility(
                DuplicatePropertyAssociationShape.Unique));
    }

    [Theory]
    [InlineData(DuplicatePropertyAssociationShape.DecoyFirst)]
    [InlineData(DuplicatePropertyAssociationShape.DecoyLast)]
    public void PropertyDeclaration_DeclinesAccessorClaimedByMultipleProperties(
        DuplicatePropertyAssociationShape shape)
    {
        // Both row orders answer the same way: the accessor's association is
        // not unique, so the base property's accessibility is not adopted.
        Assert.Equal(
            "protected",
            ResolveDuplicateAssociationAccessibility(shape));
    }

    static string ResolveDuplicateAssociationAccessibility(
        DuplicatePropertyAssociationShape shape)
    {
        using var stream = new MemoryStream(
            BuildDuplicatePropertyAssociationImage(shape));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        TypeDefinitionHandle derivedHandle =
            reader.TypeDefinitions.Single(handle =>
                reader.GetString(
                    reader.GetTypeDefinition(handle).Name)
                == "Derived");
        TypeDefinition derived =
            reader.GetTypeDefinition(derivedHandle);

        return MetadataDeclarationQuery.GetProperty(
                reader,
                derived,
                GetProperty(derived, "Value", reader))
            .Accessibility;
    }

    const string BranchingAncestryWorkerVariable =
        "DOTNET_INSPECT_BRANCHING_ANCESTRY_WORKER";

    public enum ReferenceConstraintAuthenticationShape
    {
        FlagOnly,
        FlagWithValidConstraint,
        FlagWithDegradedConstraint,
    }

    /// <summary>
    /// Builds <c>Derived&lt;T&gt; : Base</c> whose <c>T Value()</c> -- or
    /// <c>T[] Value()</c> -- carries a class <c>MethodImpl</c> onto a base slot
    /// returning <c>object</c> (or <c>object[]</c>), with <c>T</c> always
    /// carrying the reference-type flag and the requested constraint set
    /// beside it. Only reference-ness authorizes either conversion, so a
    /// constraint the shared budget cannot decode is malformed evidence about
    /// the very parameter the answer rests on.
    /// </summary>
    static byte[] BuildReferenceConstraintAuthenticationImage(
        ReferenceConstraintAuthenticationShape shape,
        bool arrayReturn)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("ReferenceConstraintAuthentication");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                derivedType,
                GenericParameterAttributes.ReferenceTypeConstraint,
                metadata.GetOrAddString("T"),
                index: 0);
        switch (shape)
        {
            case ReferenceConstraintAuthenticationShape
                .FlagWithValidConstraint:
                metadata.AddGenericParameterConstraint(parameter, animal);
                break;
            case ReferenceConstraintAuthenticationShape
                .FlagWithDegradedConstraint:
                metadata.AddGenericParameterConstraint(
                    parameter,
                    AddSyntheticOverDeepArrayTypeSpec(
                        metadata,
                        animal,
                        depth: SignatureBlobGuard.DefaultMaxDepth + 64));
                break;
        }

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticObjectReturnSignature(metadata, arrayReturn),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                arrayReturn
                    ? AddSyntheticGenericParameterArrayReturnSignature(
                        metadata)
                    : AddSyntheticGenericParameterReturnSignature(metadata),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    public enum ObjectReturnConversionShape
    {
        ExactLocalReference,
        ModifiedLocalReference,
        AmbiguousLocalReference,
    }

    /// <summary>
    /// Builds <c>Derived : Base</c> whose <c>Animal Value()</c> carries a class
    /// <c>MethodImpl</c> onto a base slot returning plain <c>object</c>. The
    /// shape wraps that return in a custom modifier the declaration cannot
    /// carry, or adds a second <c>Animal</c> definition so the return no longer
    /// resolves to a unique current-image definition.
    /// </summary>
    static byte[] BuildObjectReturnConversionImage(
        ObjectReturnConversionShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("ObjectReturnConversion");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle modifierType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IsVolatile"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        if (shape == ObjectReturnConversionShape.AmbiguousLocalReference)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticObjectReturnSignature(
                    metadata,
                    wrapInArray: false),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                shape == ObjectReturnConversionShape.ModifiedLocalReference
                    ? AddSyntheticModifiedReturnSignature(
                        metadata,
                        modifierType,
                        animal)
                    : AddSyntheticMethodSignature(metadata, animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    public enum SupertypeRoleShape
    {
        ValidInterfaceImplementation,
        InterfaceImplementationNamesClass,
        BaseTypeNamesInterface,
    }

    /// <summary>
    /// Builds <c>Derived : Base</c> whose <c>Dog Value()</c> carries a class
    /// <c>MethodImpl</c> onto a base slot whose declared return is only
    /// reachable through one same-image supertype row of <c>Dog</c>. The shape
    /// controls whether that row's target is the kind the row claims: an
    /// interface reached through <c>InterfaceImpl</c>, a class written into
    /// <c>InterfaceImpl</c>, or an interface written into <c>Extends</c>.
    /// </summary>
    static byte[] BuildSupertypeRoleImage(SupertypeRoleShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("SupertypeRole");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle contract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("IContract"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                shape == SupertypeRoleShape.BaseTypeNamesInterface
                    ? contract
                    : objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        if (shape != SupertypeRoleShape.BaseTypeNamesInterface)
        {
            metadata.AddInterfaceImplementation(
                dog,
                shape == SupertypeRoleShape
                    .InterfaceImplementationNamesClass
                    ? animal
                    : contract);
        }

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMethodSignature(
                    metadata,
                    shape == SupertypeRoleShape
                        .InterfaceImplementationNamesClass
                        ? animal
                        : contract),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticMethodSignature(metadata, dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    public enum OverrideSignatureHeaderShape
    {
        SourceDeclarable,
        VarArg,
        ExplicitThis,
        MissingHasThis,
    }

    static byte SignatureHeaderByte(OverrideSignatureHeaderShape shape)
        => shape switch
        {
            // HASTHIS | VARARG
            OverrideSignatureHeaderShape.VarArg => 0x25,
            // HASTHIS | EXPLICITTHIS | DEFAULT
            OverrideSignatureHeaderShape.ExplicitThis => 0x60,
            // DEFAULT with no HASTHIS
            OverrideSignatureHeaderShape.MissingHasThis => 0x00,
            _ => 0x20,
        };

    /// <summary>
    /// Builds <c>Derived : Base</c> where both <c>Animal Value()</c>
    /// signatures carry the same calling-convention header, so a refusal can
    /// only come from that header not being one a source <c>override</c> can
    /// declare -- never from the two sides disagreeing.
    /// </summary>
    static byte[] BuildOverrideSignatureHeaderImage(
        OverrideSignatureHeaderShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("OverrideSignatureHeader");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        BlobHandle signature = AddSyntheticRawHeaderReturnSignature(
            metadata,
            SignatureHeaderByte(shape),
            animal);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            signature,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            signature,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        return SerializeSyntheticMetadata(metadata);
    }

    /// <summary>
    /// Builds <c>Derived : object</c> declaring a virtual, non-newslot
    /// <c>ToString</c> whose calling-convention header follows
    /// <paramref name="shape"/>. The base reference carries a real core-library
    /// public-key token, so the object root authenticates and the header is the
    /// only thing under test.
    /// </summary>
    static byte[] BuildObjectIntrinsicSignatureHeaderImage(
        OverrideSignatureHeaderShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("ObjectIntrinsicSignatureHeader");
        AssemblyReferenceHandle runtime =
            AddSyntheticCoreLibraryReference(metadata);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Derived"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ToString"),
            AddSyntheticRawHeaderStringReturnSignature(
                metadata,
                SignatureHeaderByte(shape)),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        return SerializeSyntheticMetadata(metadata);
    }

    public enum ExternalConstructedArityShape
    {
        MatchingArity,
        MismatchedArgumentCount,
        MismatchedEncodedCount,
    }

    /// <summary>
    /// Builds <c>Derived : Base</c> whose returns are constructions of an
    /// external generic definition this image cannot resolve. A matching
    /// construction stays eligible for compiler validation; a construction
    /// whose argument count disagrees with the other side, or with the arity
    /// its own encoded definition name declares, is structurally a different
    /// type and needs no resolution to disprove.
    /// </summary>
    static byte[] BuildExternalConstructedArityImage(
        ExternalConstructedArityShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("ExternalConstructedArity");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        AssemblyReferenceHandle external =
            AddSyntheticAssemblyReference(metadata, "ExternalContracts");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle externalGeneric =
            metadata.AddTypeReference(
                external,
                metadata.GetOrAddString("External"),
                metadata.GetOrAddString(
                    shape == ExternalConstructedArityShape
                        .MismatchedEncodedCount
                        ? "Variant`2"
                        : "Variant`1"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericReturnSignature(
                    metadata,
                    externalGeneric,
                    animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                shape == ExternalConstructedArityShape
                    .MismatchedArgumentCount
                    ? AddSyntheticGenericReturnSignature(
                        metadata,
                        externalGeneric,
                        dog,
                        animal)
                    : AddSyntheticGenericReturnSignature(
                        metadata,
                        externalGeneric,
                        dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    public enum VarianceOwnerKind
    {
        Interface,
        Delegate,
        Class,
        ImpersonatingDelegate,
    }

    /// <summary>
    /// Builds <c>Derived : Base</c> whose <c>Variant&lt;Dog&gt; Value()</c>
    /// carries a class <c>MethodImpl</c> onto <c>Variant&lt;Animal&gt;
    /// Value()</c>, where <c>Variant`1</c>'s single type parameter is always
    /// marked covariant and only the owning definition's kind changes. A class
    /// may not declare variance at all, and delegate-ness counts only when the
    /// base resolves through an authenticated core library, so a local type
    /// merely named <c>System.MulticastDelegate</c> does not buy it.
    /// </summary>
    static byte[] BuildVarianceOwnerKindImage(VarianceOwnerKind kind)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("VarianceOwnerKind");
        AssemblyReferenceHandle runtime =
            AddSyntheticCoreLibraryReference(metadata);
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
        TypeReferenceHandle multicastDelegate =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("MulticastDelegate"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle impersonatingDelegate =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("MulticastDelegate"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle variant =
            metadata.AddTypeDefinition(
                kind == VarianceOwnerKind.Interface
                    ? TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract
                    : TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Variant`1"),
                kind switch
                {
                    VarianceOwnerKind.Interface => default,
                    VarianceOwnerKind.Delegate => multicastDelegate,
                    VarianceOwnerKind.ImpersonatingDelegate =>
                        impersonatingDelegate,
                    _ => objectType,
                },
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dog =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Dog"),
                animal,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        metadata.AddGenericParameter(
            variant,
            GenericParameterAttributes.Covariant,
            metadata.GetOrAddString("T"),
            index: 0);

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        MethodDefinitionHandle baseMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericReturnSignature(
                    metadata,
                    variant,
                    animal),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedMethod =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                AddSyntheticGenericReturnSignature(
                    metadata,
                    variant,
                    dog),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            baseMethod);
        return SerializeSyntheticMetadata(metadata);
    }

    public enum DuplicatePropertyAssociationShape
    {
        Unique,
        DecoyFirst,
        DecoyLast,
    }

    /// <summary>
    /// Builds <c>Derived : Base</c> where <c>Base.Value</c> pairs a public
    /// getter with a protected setter and <c>Derived</c> overrides the setter
    /// alone. The reconstructed property is then as accessible as the base
    /// property -- public -- but only while the overridden accessor's property
    /// association is unique. The decoy shapes add a second <c>Base</c>
    /// property claiming that same setter, in either metadata row order. That
    /// decoy is public in its own right, so taking the first matching row
    /// answers <c>public</c> in both orders while a unique association is
    /// genuinely absent.
    /// </summary>
    static byte[] BuildDuplicatePropertyAssociationImage(
        DuplicatePropertyAssociationShape shape)
    {
        MetadataBuilder metadata =
            CreateSyntheticMetadata("DuplicatePropertyAssociation");
        AssemblyReferenceHandle runtime =
            AddSyntheticAssemblyReference(metadata, "System.Runtime");
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle animal =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Animal"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle derivedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Derived"),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(4));

        BlobHandle getterSignature =
            AddSyntheticMethodSignature(metadata, animal);
        BlobHandle setterSignature =
            AddSyntheticMethodSignature(
                metadata,
                default,
                animal,
                parameterCount: 1);
        const MethodAttributes accessorFlags =
            MethodAttributes.Virtual
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;
        MethodDefinitionHandle baseGetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.NewSlot
                    | accessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("get_Value"),
                getterSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle baseSetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Family
                    | MethodAttributes.NewSlot
                    | accessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("set_Value"),
                setterSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));

        // Declared in every shape so the three images differ only in their
        // property rows: the decoy pairs this public getter with the very
        // setter Derived overrides, which is what makes first-row-wins visible.
        MethodDefinitionHandle shadowGetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | accessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("get_Shadow"),
                getterSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle derivedSetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Family | accessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("set_Value"),
                setterSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));

        BlobHandle propertySignature =
            AddSyntheticClassPropertySignature(metadata, animal);
        var decoyProperties = new List<PropertyDefinitionHandle>();
        PropertyDefinitionHandle firstBaseProperty = default;
        if (shape == DuplicatePropertyAssociationShape.DecoyFirst)
        {
            firstBaseProperty =
                metadata.AddProperty(
                    PropertyAttributes.None,
                    metadata.GetOrAddString("Shadow"),
                    propertySignature);
            decoyProperties.Add(firstBaseProperty);
        }

        PropertyDefinitionHandle baseProperty =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                propertySignature);
        if (firstBaseProperty.IsNil)
            firstBaseProperty = baseProperty;
        if (shape == DuplicatePropertyAssociationShape.DecoyLast)
        {
            decoyProperties.Add(
                metadata.AddProperty(
                    PropertyAttributes.None,
                    metadata.GetOrAddString("Shadow"),
                    propertySignature));
        }

        PropertyDefinitionHandle derivedProperty =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                propertySignature);
        metadata.AddPropertyMap(baseType, firstBaseProperty);
        metadata.AddPropertyMap(derivedType, derivedProperty);

        // The decoy claims the very accessor Derived overrides, so the base
        // property the walk would adopt is no longer uniquely determined. Its
        // semantics rows are emitted in property-row order so the shapes differ
        // only in which row the reader reaches first.
        void AddBaseSemantics()
        {
            metadata.AddMethodSemantics(
                baseProperty,
                MethodSemanticsAttributes.Getter,
                baseGetter);
            metadata.AddMethodSemantics(
                baseProperty,
                MethodSemanticsAttributes.Setter,
                baseSetter);
        }

        void AddDecoySemantics()
        {
            foreach (PropertyDefinitionHandle decoy in decoyProperties)
            {
                metadata.AddMethodSemantics(
                    decoy,
                    MethodSemanticsAttributes.Getter,
                    shadowGetter);
                metadata.AddMethodSemantics(
                    decoy,
                    MethodSemanticsAttributes.Setter,
                    baseSetter);
            }
        }

        if (shape == DuplicatePropertyAssociationShape.DecoyFirst)
        {
            AddDecoySemantics();
            AddBaseSemantics();
        }
        else
        {
            AddBaseSemantics();
            AddDecoySemantics();
        }

        metadata.AddMethodSemantics(
            derivedProperty,
            MethodSemanticsAttributes.Setter,
            derivedSetter);
        return SerializeSyntheticMetadata(metadata);
    }

    static AssemblyReferenceHandle AddSyntheticCoreLibraryReference(
        MetadataBuilder metadata)
        => metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a,
                }),
            default,
            default);

    /// <summary>
    /// A parameterless instance method signature returning
    /// <c>ELEMENT_TYPE_OBJECT</c>, optionally as an SZ array. Encoding the
    /// primitive element type is what makes the return a plain <c>object</c>
    /// rather than a named reference to <c>System.Object</c>.
    /// </summary>
    static BlobHandle AddSyntheticObjectReturnSignature(
        MetadataBuilder metadata,
        bool wrapInArray)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        if (wrapInArray)
            signature.WriteByte(0x1d);
        signature.WriteByte(0x1c);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticModifiedReturnSignature(
        MetadataBuilder metadata,
        EntityHandle modifierType,
        EntityHandle returnType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x20);
        WriteTypeDefOrRefEncoded(signature, modifierType);
        WriteReferenceTypeSignature(signature, returnType);
        return metadata.GetOrAddBlob(signature);
    }

    /// <summary>
    /// A parameterless method signature whose calling-convention header byte is
    /// written verbatim, so a test can encode the vararg, explicit-<c>this</c>,
    /// and missing-<c>HASTHIS</c> headers no C# compiler emits for an override.
    /// </summary>
    static BlobHandle AddSyntheticRawHeaderReturnSignature(
        MetadataBuilder metadata,
        byte header,
        EntityHandle returnType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(header);
        signature.WriteCompressedInteger(0);
        WriteReferenceTypeSignature(signature, returnType);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticRawHeaderStringReturnSignature(
        MetadataBuilder metadata,
        byte header)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(header);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x0e);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticClassPropertySignature(
        MetadataBuilder metadata,
        EntityHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x28);
        signature.WriteCompressedInteger(0);
        WriteReferenceTypeSignature(signature, type);
        return metadata.GetOrAddBlob(signature);
    }

    /// <summary>
    /// A <c>TypeSpec</c> for <c>Definition&lt;Pair&lt;!0, !0&gt;&gt;</c>, which
    /// as a base row makes every ancestry step square the size of the previous
    /// instantiation rather than merely producing a new one.
    /// </summary>
    static TypeSpecificationHandle
        AddSyntheticBranchingSelfInstantiationTypeSpec(
            MetadataBuilder metadata,
            EntityHandle definition,
            EntityHandle pair)
    {
        var blob = new BlobBuilder();
        GenericTypeArgumentsEncoder pairArguments =
            new BlobEncoder(blob)
                .TypeSpecificationSignature()
                .GenericInstantiation(
                    definition,
                    genericArgumentCount: 1,
                    isValueType: false)
                .AddArgument()
                .GenericInstantiation(
                    pair,
                    genericArgumentCount: 2,
                    isValueType: false);
        pairArguments.AddArgument().GenericTypeParameter(0);
        pairArguments.AddArgument().GenericTypeParameter(0);
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(blob));
    }

    static MetadataBuilder CreateSyntheticMetadata(
        string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                $"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        return metadata;
    }

    static AssemblyReferenceHandle AddSyntheticAssemblyReference(
        MetadataBuilder metadata,
        string name)
        => metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

    static BlobHandle AddSyntheticMethodSignature(
        MetadataBuilder metadata,
        EntityHandle returnType,
        EntityHandle parameterType = default,
        int parameterCount = 0)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                parameterCount,
                encoder =>
                {
                    if (returnType.IsNil)
                        encoder.Void();
                    else
                    {
                        encoder.Type().Type(
                            returnType,
                            isValueType: false);
                    }
                },
                parameters =>
                {
                    if (parameterCount != 0)
                    {
                        parameters
                            .AddParameter()
                            .Type()
                            .Type(
                                parameterType,
                                isValueType: false);
                    }
                });
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticGenericParameterReturnSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType => returnType
                    .Type()
                    .GenericTypeParameter(0),
                _ => { });
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticGenericReturnSignature(
        MetadataBuilder metadata,
        EntityHandle genericType,
        params EntityHandle[] arguments)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType =>
                {
                    GenericTypeArgumentsEncoder encodedArguments =
                        returnType
                            .Type()
                            .GenericInstantiation(
                                genericType,
                                arguments.Length,
                                isValueType: false);
                    foreach (EntityHandle argument
                        in arguments)
                    {
                        encodedArguments
                            .AddArgument()
                            .Type(
                                argument,
                                isValueType: false);
                    }
                },
                _ => { });
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticGenericArrayReturnSignature(
        MetadataBuilder metadata,
        EntityHandle genericType,
        params EntityHandle[] arguments)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType =>
                {
                    GenericTypeArgumentsEncoder encodedArguments =
                        returnType
                            .Type()
                            .SZArray()
                            .GenericInstantiation(
                                genericType,
                                arguments.Length,
                                isValueType: false);
                    foreach (EntityHandle argument
                        in arguments)
                    {
                        encodedArguments
                            .AddArgument()
                            .Type(
                                argument,
                                isValueType: false);
                    }
                },
                _ => { });
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticArrayReturnSignature(
        MetadataBuilder metadata,
        EntityHandle elementType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x1d);
        WriteReferenceTypeSignature(signature, elementType);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticGenericParameterArrayReturnSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x1d);
        signature.WriteByte(0x13);
        signature.WriteCompressedInteger(0);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticModifiedGenericReturnSignature(
        MetadataBuilder metadata,
        EntityHandle modifierType,
        bool isRequired,
        EntityHandle genericType,
        params EntityHandle[] arguments)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(isRequired ? (byte)0x1f : (byte)0x20);
        WriteTypeDefOrRefEncoded(signature, modifierType);
        WriteGenericInstantiation(signature, genericType, arguments);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticPinnedGenericReturnSignature(
        MetadataBuilder metadata,
        EntityHandle genericType,
        params EntityHandle[] arguments)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x45);
        WriteGenericInstantiation(signature, genericType, arguments);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticMdArrayReturnSignature(
        MetadataBuilder metadata,
        EntityHandle elementType,
        int rank,
        int[]? sizes = null,
        int[]? lowerBounds = null)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x14);
        WriteReferenceTypeSignature(signature, elementType);
        signature.WriteCompressedInteger(rank);
        sizes ??= [];
        lowerBounds ??= [];
        signature.WriteCompressedInteger(sizes.Length);
        foreach (int size in sizes)
            signature.WriteCompressedInteger(size);
        signature.WriteCompressedInteger(lowerBounds.Length);
        foreach (int lowerBound in lowerBounds)
            signature.WriteCompressedSignedInteger(lowerBound);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticSetterSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSyntheticPropertySignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x28);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static void WriteGenericInstantiation(
        BlobBuilder signature,
        EntityHandle genericType,
        params EntityHandle[] arguments)
    {
        signature.WriteByte(0x15);
        WriteReferenceTypeSignature(signature, genericType);
        signature.WriteCompressedInteger(arguments.Length);
        foreach (EntityHandle argument in arguments)
            WriteReferenceTypeSignature(signature, argument);
    }

    static void WriteReferenceTypeSignature(
        BlobBuilder signature,
        EntityHandle type)
    {
        signature.WriteByte(0x12);
        WriteTypeDefOrRefEncoded(signature, type);
    }

    static void WriteTypeDefOrRefEncoded(
        BlobBuilder signature,
        EntityHandle type)
    {
        int tag = type.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                $"Unsupported type handle kind '{type.Kind}'."),
        };
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(type) << 2)
            | tag);
    }

    static TypeSpecificationHandle AddSyntheticGenericTypeSpecification(
        MetadataBuilder metadata,
        EntityHandle genericType,
        params EntityHandle[] arguments)
    {
        var signature = new BlobBuilder();
        GenericTypeArgumentsEncoder encodedArguments =
            new BlobEncoder(signature)
                .TypeSpecificationSignature()
                .GenericInstantiation(
                    genericType,
                    arguments.Length,
                    isValueType: false);
        foreach (EntityHandle argument in arguments)
        {
            encodedArguments
                .AddArgument()
                .Type(
                    argument,
                    isValueType: false);
        }
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(signature));
    }

    static byte[] SerializeSyntheticMetadata(
        MetadataBuilder metadata)
    {
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

    static string EmitAccessibilityChangingOverride()
    {
        var assemblyName = new AssemblyName("AccessibilityChangingOverride");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var baseType = module.DefineType("AccessBase", TypeAttributes.Public);
        var baseMethod = baseType.DefineMethod(
            "Value",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        var baseIl = baseMethod.GetILGenerator();
        baseIl.Emit(OpCodes.Ldc_I4_1);
        baseIl.Emit(OpCodes.Ret);
        var createdBase = baseType.CreateType();

        var derivedType = module.DefineType(
            "AccessDerived",
            TypeAttributes.Public,
            createdBase);
        var derivedMethod = derivedType.DefineMethod(
            "Value",
            MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(int),
            Type.EmptyTypes);
        var derivedIl = derivedMethod.GetILGenerator();
        derivedIl.Emit(OpCodes.Ldc_I4_2);
        derivedIl.Emit(OpCodes.Ret);
        derivedType.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"AccessibilityChangingOverride-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitRefOutKindMismatchOverride()
    {
        var assemblyName = new AssemblyName("RefOutKindMismatchOverride");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var byRefInt = typeof(int).MakeByRefType();

        var baseType = module.DefineType("Base", TypeAttributes.Public);
        var baseMethod = baseType.DefineMethod(
            "M",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(void),
            [byRefInt]);
        baseMethod.DefineParameter(1, ParameterAttributes.None, "value");
        baseMethod.GetILGenerator().Emit(OpCodes.Ret);
        var createdBase = baseType.CreateType();

        var derivedType = module.DefineType("Derived", TypeAttributes.Public, createdBase);
        var derivedMethod = derivedType.DefineMethod(
            "M",
            MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(void),
            [byRefInt]);
        derivedMethod.DefineParameter(1, ParameterAttributes.Out, "value");
        derivedMethod.GetILGenerator().Emit(OpCodes.Ret);
        derivedType.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"RefOutKindMismatchOverride-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitCovariantReturnOverride()
    {
        var assemblyName = new AssemblyName("CovariantReturnOverride");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);

        var baseType = module.DefineType("Base", TypeAttributes.Public);
        var baseMethod = baseType.DefineMethod(
            "Value",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(object),
            Type.EmptyTypes);
        baseMethod.GetILGenerator().Emit(OpCodes.Ldstr, "base");
        baseMethod.GetILGenerator().Emit(OpCodes.Ret);
        var createdBase = baseType.CreateType();

        var derivedType = module.DefineType("Derived", TypeAttributes.Public, createdBase);
        var derivedMethod = derivedType.DefineMethod(
            "Value",
            MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(string),
            Type.EmptyTypes);
        derivedMethod.GetILGenerator().Emit(OpCodes.Ldstr, "derived");
        derivedMethod.GetILGenerator().Emit(OpCodes.Ret);
        derivedType.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"CovariantReturnOverride-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitNewSlotInterfaceMethodImpl()
    {
        var assemblyName = new AssemblyName("NewSlotInterfaceMethodImpl");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);

        var interfaceType = module.DefineType(
            "IContract",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var declaration = interfaceType.DefineMethod(
            "Value",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            typeof(object),
            Type.EmptyTypes);
        var createdInterface = interfaceType.CreateType();

        var implementationType = module.DefineType("Implementation", TypeAttributes.Public);
        implementationType.AddInterfaceImplementation(createdInterface);
        var body = implementationType.DefineMethod(
            "Value",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(object),
            Type.EmptyTypes);
        body.GetILGenerator().Emit(OpCodes.Ldnull);
        body.GetILGenerator().Emit(OpCodes.Ret);
        implementationType.DefineMethodOverride(body, declaration);
        implementationType.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"NewSlotInterfaceMethodImpl-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static byte[] BuildUnauthenticatedClassMethodImplImage(
        bool ambiguousBaseDeclarations)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ClassMethodImpl.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ClassMethodImpl"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        var objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var baseType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Base"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var middleOrUnrelatedType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString(
                ambiguousBaseDeclarations ? "Middle" : "Unrelated"),
            ambiguousBaseDeclarations ? baseType : objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        var derivedType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Derived"),
            ambiguousBaseDeclarations ? middleOrUnrelatedType : baseType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        var signatureHandle = metadata.GetOrAddBlob(signature);
        var attributes = MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot;
        var baseMethod = metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            signatureHandle,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        var middleOrUnrelatedMethod = metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            signatureHandle,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        var derivedMethod = metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Value"),
            signatureHandle,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        metadata.AddMethodImplementation(
            derivedType,
            derivedMethod,
            middleOrUnrelatedMethod);
        if (ambiguousBaseDeclarations)
            metadata.AddMethodImplementation(derivedType, derivedMethod, baseMethod);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static string EmitIncompatibleReturnOverride()
    {
        var assemblyName = new AssemblyName("IncompatibleReturnOverride");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);

        var baseType = module.DefineType("Base", TypeAttributes.Public);
        var baseMethod = baseType.DefineMethod(
            "Value",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(string),
            Type.EmptyTypes);
        baseMethod.GetILGenerator().Emit(OpCodes.Ldstr, "base");
        baseMethod.GetILGenerator().Emit(OpCodes.Ret);
        var createdBase = baseType.CreateType();

        var derivedType = module.DefineType("Derived", TypeAttributes.Public, createdBase);
        var derivedMethod = derivedType.DefineMethod(
            "Value",
            MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(object),
            Type.EmptyTypes);
        derivedMethod.GetILGenerator().Emit(OpCodes.Ldstr, "derived");
        derivedMethod.GetILGenerator().Emit(OpCodes.Ret);
        derivedType.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"IncompatibleReturnOverride-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitIncompatibleStructuredReturnOverride(
        bool arrayReturn)
    {
        var assemblyName = new AssemblyName(
            "IncompatibleStructuredReturnOverride");
        var assembly = new PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(
            assemblyName.Name!);

        Type first = module
            .DefineType("First", TypeAttributes.Public)
            .CreateType();
        Type second = module
            .DefineType("Second", TypeAttributes.Public)
            .CreateType();
        Type baseReturn;
        Type derivedReturn;
        if (arrayReturn)
        {
            baseReturn = first.MakeArrayType();
            derivedReturn = second.MakeArrayType();
        }
        else
        {
            var boxBuilder = module.DefineType(
                "Box`1",
                TypeAttributes.Public);
            boxBuilder.DefineGenericParameters("T");
            Type box = boxBuilder.CreateType();
            baseReturn = box.MakeGenericType(first);
            derivedReturn = box.MakeGenericType(second);
        }

        var baseBuilder = module.DefineType(
            "Base",
            TypeAttributes.Public);
        var baseMethod = baseBuilder.DefineMethod(
            "Value",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            baseReturn,
            Type.EmptyTypes);
        baseMethod.GetILGenerator().Emit(OpCodes.Ldnull);
        baseMethod.GetILGenerator().Emit(OpCodes.Ret);
        Type baseType = baseBuilder.CreateType();

        var derivedBuilder = module.DefineType(
            "Derived",
            TypeAttributes.Public,
            baseType);
        var derivedMethod = derivedBuilder.DefineMethod(
            "Value",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            derivedReturn,
            Type.EmptyTypes);
        derivedMethod.GetILGenerator().Emit(OpCodes.Ldnull);
        derivedMethod.GetILGenerator().Emit(OpCodes.Ret);
        derivedBuilder.DefineMethodOverride(
            derivedMethod,
            baseMethod);
        derivedBuilder.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"IncompatibleStructuredReturnOverride-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static TypeDefinition GetTypeDefinition(Type type)
        => Reader.GetTypeDefinition(GetTypeDefinitionHandle(type));

    static TypeDefinitionHandle GetTypeDefinitionHandle(Type type)
    {
        var metadataName = type.FullName!.Replace('+', '.');
        metadataName = StripGenericArity(metadataName);
        foreach (var handle in Reader.TypeDefinitions)
        {
            var definition = Reader.GetTypeDefinition(handle);
            if (StripGenericArity(TypeResolver.GetFullName(Reader, definition)) == metadataName)
                return handle;
        }

        throw new InvalidOperationException($"Type '{metadataName}' was not found.");
    }

    static MethodDefinition GetMethod(TypeDefinition type, string name)
        => Reader.GetMethodDefinition(GetMethodHandle(type, name));

    static MethodDefinitionHandle GetMethodHandle(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetMethods())
        {
            var method = Reader.GetMethodDefinition(handle);
            if (Reader.GetString(method.Name) == name)
                return handle;
        }

        throw new InvalidOperationException($"Method '{name}' was not found.");
    }

    static PropertyDefinition GetProperty(TypeDefinition type, string name)
        => GetProperty(type, name, Reader);

    static PropertyDefinition GetProperty(
        TypeDefinition type,
        string name,
        MetadataReader reader)
    {
        foreach (var handle in type.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            if (reader.GetString(property.Name) == name)
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

    public int @event = 1;

    public void QualifiedKeyword(@namespace @class, string text = ".namespace") { }

    public void GlobalKeyword(
        [global::GlobalType(typeof(global::@class), (global::@event)1)] global::@class value,
        List<global::@class>? values = null,
        global::@event mode = (global::@event)1,
        string text = "a\"b.class")
    {
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

    public class OverrideBase
    {
        internal virtual int Value() => 1;
    }

    public class OverrideDerived : OverrideBase
    {
        internal override int Value() => 2;
    }

    public class NewSlotDerived : OverrideBase
    {
        internal new virtual int Value() => 3;
    }

    public class ExternalOverride : Exception
    {
        public override string ToString() => nameof(ExternalOverride);
    }

    public class OverrideGrandBase
    {
        internal virtual int Value() => 1;
    }

    public class OverrideMiddle : OverrideGrandBase
    {
        internal new virtual int Value() => 2;
    }

    public class OverrideLeaf : OverrideMiddle
    {
        internal override int Value() => 3;
    }

    public class SetterOnlyOverrideBase
    {
        public virtual int Value
        {
            get => 0;
            protected set
            {
            }
        }
    }

    public class SetterOnlyOverrideDerived : SetterOnlyOverrideBase
    {
        public override int Value
        {
            protected set
            {
            }
        }
    }

    public class SetterOnlyOverrideLeaf : SetterOnlyOverrideDerived
    {
        public override int Value
        {
            protected set
            {
            }
        }
    }

    public class CovariantAnimal
    {
    }

    public class CovariantDog : CovariantAnimal
    {
    }

    public class CovariantReturnBase
    {
        public virtual CovariantAnimal Value() => new();
    }

    public class CovariantReturnDerived : CovariantReturnBase
    {
        public override CovariantDog Value() => new();
    }

    public class ScopedParameterCovariantReturnBase
    {
        public virtual CovariantAnimal Value(
            CovariantAnimal input) => input;
    }

    public class ScopedParameterCovariantReturnDerived
        : ScopedParameterCovariantReturnBase
    {
        public override CovariantDog Value(
            CovariantAnimal input) => new();
    }

    public class GenericCovariantReturnDerived<T> : ObjectCovariantReturnBase
        where T : class
    {
        public override T Value() => default!;
    }

    public class AnimalCovariantReturnBase
    {
        public virtual CovariantAnimal Value() => new();
    }

    public class ExplicitConstraintCovariantReturnDerived<T>
        : AnimalCovariantReturnBase
        where T : CovariantDog
    {
        public override T Value() => default!;
    }

    public class AnimalArrayCovariantReturnBase
    {
        public virtual CovariantAnimal[] Values() =>
            Array.Empty<CovariantAnimal>();
    }

    public class ExplicitConstraintArrayCovariantReturnDerived<T>
        : AnimalArrayCovariantReturnBase
        where T : CovariantDog
    {
        public override T[] Values() => Array.Empty<T>();
    }

    public class VariantOuter<TOuter>
    {
        public interface IProducer<out TValue>
        {
        }

        public sealed class Producer<TValue>
            : IProducer<TValue>
        {
        }
    }

    public class NestedVariantReturnBase
    {
        public virtual VariantOuter<int>
            .IProducer<CovariantAnimal> Value() =>
            new VariantOuter<int>
                .Producer<CovariantAnimal>();
    }

    public class NestedVariantReturnDerived
        : NestedVariantReturnBase
    {
        public override VariantOuter<int>
            .IProducer<CovariantDog> Value() =>
            new VariantOuter<int>
                .Producer<CovariantDog>();
    }

    public class ExternalGenericCovariantReturnBase
    {
        public virtual System.Collections.Generic
            .IEnumerable<CovariantAnimal> Values() => [];
    }

    public class ExternalGenericCovariantReturnDerived
        : ExternalGenericCovariantReturnBase
    {
        public override System.Collections.Generic
            .IEnumerable<CovariantDog> Values() => [];
    }

    public class ExternalConstructedConstraintCovariantReturnBase
    {
        public virtual System.Collections.Generic
            .IEnumerable<CovariantAnimal> Values() => [];
    }

    public class ExternalConstructedConstraintCovariantReturnDerived<T>
        : ExternalConstructedConstraintCovariantReturnBase
        where T : System.Collections.Generic
            .List<CovariantDog>
    {
        public override T Values() =>
            throw new NotSupportedException();
    }

    public class CovariantPropertyBase
    {
        public virtual CovariantAnimal Value => new();
    }

    public class CovariantPropertyDerived : CovariantPropertyBase
    {
        public override CovariantDog Value => new();
    }

    public class CovariantIndexerBase
    {
        public virtual CovariantAnimal this[int index] => new();
    }

    public class CovariantIndexerDerived : CovariantIndexerBase
    {
        public override CovariantDog this[int index] => new();
    }

    public class ObjectCovariantReturnBase
    {
        public virtual object Value() => "";
    }

    public class ObjectCovariantReturnDerived : ObjectCovariantReturnBase
    {
        public override string Value() => "";
    }

    /// <summary>
    /// A compiler-produced covariant return whose implementation type is an
    /// unwrapped current-image definition, which is the shape the
    /// return-to-object conversion has to keep accepting.
    /// </summary>
    public class LocalReferenceObjectCovariantReturnDerived
        : ObjectCovariantReturnBase
    {
        public override CovariantDog Value() => new();
    }

    public interface IExplicitOnlyContract
    {
        void Run();
    }

    /// <summary>
    /// An explicit interface implementation is a private newslot virtual final
    /// method plus a <c>MethodImpl</c>, so it carries the row an override
    /// carries while reusing no inherited class virtual slot.
    /// </summary>
    public class ExplicitInterfaceOnlyImplementation
        : IExplicitOnlyContract
    {
        void IExplicitOnlyContract.Run()
        {
        }
    }

    public class PlainVirtualBase
    {
        public virtual void Run()
        {
        }
    }

    public class PlainOverrideDerived : PlainVirtualBase
    {
        public override void Run()
        {
        }
    }

    public class ConstructedGenericCovariantReturnBase<TItem>
        where TItem : CovariantAnimal
    {
        public virtual CovariantAnimal Value() => new();
    }

    /// <summary>
    /// Roslyn encodes this base as a <c>TypeSpec</c> and the covariant-return
    /// <c>MethodImpl</c> declaration as a <c>MemberRef</c> rooted in that
    /// <c>TypeSpec</c>, which is the compiler-produced shape same-image
    /// override authentication has to read.
    /// </summary>
    public class ConstructedGenericCovariantReturnDerived<TItem>
        : ConstructedGenericCovariantReturnBase<TItem>
        where TItem : CovariantDog
    {
        public override TItem Value() => default!;
    }

    public class ConstructedGenericSubstitutionBase<TItem>
    {
        public virtual string Describe(TItem value) => "";
    }

    /// <summary>
    /// A plain override on a constructed generic base. Roslyn emits no
    /// <c>MethodImpl</c> here, so the slot is only reachable by walking the
    /// <c>TypeSpec</c> base and substituting <c>string</c> for
    /// <c>TItem</c> in the base signature.
    /// </summary>
    public class ConstructedGenericSubstitutionDerived
        : ConstructedGenericSubstitutionBase<string>
    {
        public override string Describe(string value) => value;
    }

    public interface ICovariantContract<TValue>
    {
    }

    public class CovariantConstructedMiddle<TValue>
        : CovariantAnimal, ICovariantContract<TValue>
    {
    }

    /// <summary>
    /// Roslyn writes this base as a <c>TypeSpec</c>, so every path from this
    /// type to <see cref="CovariantAnimal"/> and to
    /// <c>ICovariantContract&lt;int&gt;</c> runs through a constructed generic
    /// step. An ancestry walk restricted to <c>TypeDef</c> rows sees neither.
    /// </summary>
    public class CovariantDogThroughConstructedMiddle
        : CovariantConstructedMiddle<int>
    {
    }

    public class ConstructedAncestryCovariantReturnBase
    {
        public virtual CovariantAnimal Value() => new();

        public virtual ICovariantContract<int> Contract() =>
            new CovariantConstructedMiddle<int>();
    }

    public class ConstructedAncestryCovariantReturnDerived
        : ConstructedAncestryCovariantReturnBase
    {
        public override CovariantDogThroughConstructedMiddle Value() =>
            new();

        public override CovariantDogThroughConstructedMiddle Contract() =>
            new();
    }

    public class SameImageObjectSlotBase
    {
    }

    public class SameImageObjectSlotDerived : SameImageObjectSlotBase
    {
        public override string ToString() => nameof(SameImageObjectSlotDerived);

        public override int GetHashCode() => 0;

        public override bool Equals(object? obj) => false;
    }

    public class NewSlotToStringDeclarer
    {
        public new virtual string ToString() => "";
    }

    public class StaticShadowBase
    {
        public virtual int M() => 1;
    }

    public class StaticShadowDerived : StaticShadowBase
    {
        public new static int M() => 2;
    }

    public interface IStaticContract
    {
        static abstract int Create();
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
