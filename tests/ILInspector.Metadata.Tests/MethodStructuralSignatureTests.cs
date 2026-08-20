using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MethodStructuralSignatureTests
{
    [Fact]
    public void Build_EncodesGenericParametersPositionally_NotByName()
    {
        string first = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.Generic),
            methodNameOverride: "Generic");
        string renamed = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.GenericRenamed),
            methodNameOverride: "Generic");

        Assert.Equal(first, renamed);
    }

    [Fact]
    public void Build_IncludesReturnType()
    {
        string integer = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.Transform),
            methodNameOverride: "Transform");
        string text = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.TransformText),
            methodNameOverride: "Transform");

        Assert.NotEqual(integer, text);
    }

    [Fact]
    public void Build_PreservesByReferenceParameters()
    {
        string byReference = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ByRef),
            methodNameOverride: "Parameter");
        string byValue = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ByValue),
            methodNameOverride: "Parameter");

        Assert.NotEqual(byReference, byValue);
    }

    [Fact]
    public void Build_PreservesCustomModifiersOnInParameters()
    {
        string modified = BuildFor(
            nameof(IStructuralSignatureFixture),
            nameof(IStructuralSignatureFixture.Consume),
            methodNameOverride: "Consume");
        string unmodified = BuildFor(
            nameof(IStructuralSignatureFixture),
            nameof(IStructuralSignatureFixture.ConsumeRef),
            methodNameOverride: "Consume");

        Assert.NotEqual(modified, unmodified);
    }

    [Fact]
    public void BuildShape_PreservesCustomModifiersOnInParameters()
    {
        var modified = BuildShapeFor(
            nameof(IStructuralSignatureFixture),
            nameof(IStructuralSignatureFixture.Consume));
        var unmodified = BuildShapeFor(
            nameof(IStructuralSignatureFixture),
            nameof(IStructuralSignatureFixture.ConsumeRef));

        Assert.False(
            modified.ParameterTypes.SequenceEqual(
                unmodified.ParameterTypes,
                StringComparer.Ordinal));
    }

    [Fact]
    public void Build_DistinguishesInstanceFromStatic()
    {
        string instance = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.InstanceNoArgs),
            methodNameOverride: "NoArgs");
        string @static = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.StaticNoArgs),
            methodNameOverride: "NoArgs");

        Assert.NotEqual(instance, @static);
    }

    [Fact]
    public void Build_PreservesFunctionPointerCallingConvention()
    {
        string managed = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.FnPtrManaged),
            methodNameOverride: "FnPtr");
        string unmanaged = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.FnPtrUnmanaged),
            methodNameOverride: "FnPtr");

        // Managed and unmanaged function pointers with the same parameters and
        // return type are distinct CLR types and must not share a key.
        Assert.NotEqual(managed, unmanaged);
    }

    [Fact]
    public void Build_DistinguishesSameNameTypesFromDifferentAssemblies()
    {
        // DiffAsmTarget.Api has two Ping overloads whose parameter types share
        // the FQN Shared.Token but come from different assemblies (LibA vs LibB).
        // The structural key must include the defining assembly identity so the
        // overloads never collide.
        string targetPath = Path.Combine(AppContext.BaseDirectory, "DiffAsmTarget.dll");
        using var image = new MetadataImage(targetPath);
        var pings = FindMethods(image.Reader, "Api", "Ping");

        Assert.Equal(2, pings.Count);
        string first = MethodStructuralSignature.Build(image.Reader, image.Reader.GetMethodDefinition(pings[0]));
        string second = MethodStructuralSignature.Build(image.Reader, image.Reader.GetMethodDefinition(pings[1]));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BuildShape_DistinguishesSameNameTypesFromDifferentAssemblies()
    {
        string targetPath = Path.Combine(AppContext.BaseDirectory, "DiffAsmTarget.dll");
        using var image = new MetadataImage(targetPath);
        var pings = FindMethods(image.Reader, "Api", "Ping");

        Assert.Equal(2, pings.Count);
        var first = MethodStructuralSignature.BuildShape(
            image.Reader,
            image.Reader.GetMethodDefinition(pings[0]));
        var second = MethodStructuralSignature.BuildShape(
            image.Reader,
            image.Reader.GetMethodDefinition(pings[1]));

        Assert.False(
            first.ParameterTypes.SequenceEqual(
                second.ParameterTypes,
                StringComparer.Ordinal));
    }

    [Fact]
    public void BuildShape_NormalizesLiftedTypeParametersToMethodParameters()
    {
        var marker = BuildShapeFor(
            typeof(StructuralShapeMarker<object, object>).Name,
            nameof(StructuralShapeMarker<object, object>.Match),
            typeGenericParameterMethodOffset: 1);
        var implementation = BuildShapeFor(
            typeof(StructuralShapeImplementation<object>).Name,
            nameof(StructuralShapeImplementation<object>.Match));

        Assert.Equal(marker, implementation);
    }

    [Fact]
    public void Build_PreservesNestedVersusNamespaceBoundary()
    {
        using var nestedImage = new MetadataImage(
            Path.Combine(AppContext.BaseDirectory, "DiffAsmLibA.dll"));
        using var namespaceImage = new MetadataImage(
            Path.Combine(AppContext.BaseDirectory, "DiffAsmLibB.dll"));

        string nested = BuildFor(
            nestedImage.Reader,
            FindType(nestedImage.Reader, "Api"),
            "Accept",
            methodNameOverride: "Accept",
            typeNameOverrides: null);
        string namespaced = BuildFor(
            namespaceImage.Reader,
            FindType(namespaceImage.Reader, "Api"),
            "Accept",
            methodNameOverride: "Accept",
            typeNameOverrides: null);

        Assert.NotEqual(nested, namespaced);
    }

    [Fact]
    public void Build_PreservesTwoParameterBinarySignature()
    {
        var method = typeof(MethodStructuralSignature).GetMethod(
            nameof(MethodStructuralSignature.Build),
            [typeof(MetadataReader), typeof(MethodDefinition)]);

        Assert.NotNull(method);
        Assert.Equal(typeof(string), method.ReturnType);
    }

    [Fact]
    public void Build_DistinguishesMethodConstraintAttributes()
    {
        string reference = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ReferenceConstrained),
            methodNameOverride: "Constrained");
        string value = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ValueConstrained),
            methodNameOverride: "Constrained");
        string equivalent = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ReferenceConstrainedAgain),
            methodNameOverride: "Constrained");

        Assert.NotEqual(reference, value);
        Assert.Equal(reference, equivalent);
    }

    [Fact]
    public void Build_DistinguishesMethodConstraintTypes()
    {
        string disposable = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.DisposableConstrained),
            methodNameOverride: "Constrained");
        string comparable = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ComparableConstrained),
            methodNameOverride: "Constrained");
        string equivalent = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.DisposableConstrainedAgain),
            methodNameOverride: "Constrained");

        Assert.NotEqual(disposable, comparable);
        Assert.Equal(disposable, equivalent);
    }

    [Fact]
    public void Build_CanonicalizesConstraintRowOrder()
    {
        string first = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ConstraintsOrderOne),
            methodNameOverride: "Constrained");
        string reordered = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ConstraintsOrderTwo),
            methodNameOverride: "Constrained");

        Assert.Equal(first, reordered);
    }

    [Fact]
    public void Build_PreservesConstraintParameterPosition()
    {
        string first = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ConstraintsByPositionOne),
            methodNameOverride: "Constrained");
        string swapped = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ConstraintsByPositionTwo),
            methodNameOverride: "Constrained");

        Assert.NotEqual(first, swapped);
    }

    [Fact]
    public void Build_DistinguishesDeclaringTypeConstraints()
    {
        using var image = new MetadataImage(
            typeof(MethodStructuralSignatureTests).Assembly.Location);
        var referenceType = FindType(
            image.Reader,
            typeof(ReferenceConstrainedFixture<object>).Name);
        var valueType = FindType(
            image.Reader,
            typeof(ValueConstrainedFixture<int>).Name);
        var equivalentType = FindType(
            image.Reader,
            typeof(ReferenceConstrainedFixtureAgain<object>).Name);
        var overrides = new Dictionary<TypeDefinitionHandle, string>
        {
            [referenceType] = "Constrained`1",
            [valueType] = "Constrained`1",
            [equivalentType] = "Constrained`1",
        };

        string reference = BuildFor(
            image.Reader,
            referenceType,
            nameof(ReferenceConstrainedFixture<object>.M),
            methodNameOverride: "M",
            typeNameOverrides: overrides);
        string value = BuildFor(
            image.Reader,
            valueType,
            nameof(ValueConstrainedFixture<int>.M),
            methodNameOverride: "M",
            typeNameOverrides: overrides);
        string equivalent = BuildFor(
            image.Reader,
            equivalentType,
            nameof(ReferenceConstrainedFixtureAgain<object>.M),
            methodNameOverride: "M",
            typeNameOverrides: overrides);

        Assert.NotEqual(reference, value);
        Assert.Equal(reference, equivalent);
    }

    [Fact]
    public void Build_NameOverrideChangesOnlyTheRequestedName()
    {
        string first = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ByValue),
            methodNameOverride: "Same");
        string second = BuildFor(
            nameof(StructuralSignatureFixture),
            nameof(StructuralSignatureFixture.ByValueAgain),
            methodNameOverride: "Same");

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildMethodKey_SharesDeclaringTypeKeyAcrossMethods()
    {
        using var image = new MetadataImage(
            typeof(MethodStructuralSignatureTests).Assembly.Location);
        var type = FindType(
            image.Reader,
            nameof(StructuralSignatureFixture));
        var overrides = new CountingReadOnlyDictionary<TypeDefinitionHandle, string>(
            new Dictionary<TypeDefinitionHandle, string>
            {
                [type] = "Fixture",
            });
        var builder = new StructuralSignatureBuilder(
            image.Reader,
            overrides);

        var firstMethod = image.Reader.GetMethodDefinition(
            FindMethod(
                image.Reader,
                type,
                nameof(StructuralSignatureFixture.ByValue)));
        var secondMethod = image.Reader.GetMethodDefinition(
            FindMethod(
                image.Reader,
                type,
                nameof(StructuralSignatureFixture.ByValueAgain)));
        Assert.Equal(firstMethod.Signature, secondMethod.Signature);

        StructuralMethodKey first = builder.BuildMethodKey(firstMethod);
        int lookupsAfterFirstMethod = overrides.LookupCount;
        StructuralMethodKey second = builder.BuildMethodKey(secondMethod);

        Assert.True(lookupsAfterFirstMethod > 0);
        Assert.Equal(lookupsAfterFirstMethod, overrides.LookupCount);
        Assert.Same(first.DeclaringType, second.DeclaringType);
        Assert.Same(first.Component.Signature, second.Component.Signature);
    }

    [Fact]
    public void BuildTypeKey_SharesAncestorAcrossNestedTypes()
    {
        using var image = new MetadataImage(
            typeof(MethodStructuralSignatureTests).Assembly.Location);
        var outer = FindType(
            image.Reader,
            nameof(StructuralSignatureFixture));
        var inner = FindNestedType(
            image.Reader,
            outer,
            nameof(StructuralSignatureFixture.Inner));
        var builder = new StructuralSignatureBuilder(image.Reader);

        StructuralTypeKey innerKey = builder.BuildTypeKey(inner);
        StructuralTypeKey outerKey = builder.BuildTypeKey(outer);

        Assert.Same(outerKey, innerKey.DeclaringType);
    }

    [Fact]
    public void Build_TypeNameOverridesApplyOnlyToTheDeclaringChain()
    {
        using var image = new MetadataImage(
            typeof(MethodStructuralSignatureTests).Assembly.Location);
        var referenceType = FindType(
            image.Reader,
            typeof(ReferenceConstrainedFixture<object>).Name);
        var valueType = FindType(
            image.Reader,
            typeof(ValueConstrainedFixture<int>).Name);
        var overrides = new Dictionary<TypeDefinitionHandle, string>
        {
            [referenceType] = "Generated`1",
            [valueType] = "Generated`1",
        };
        var fixture = FindType(
            image.Reader,
            nameof(StructuralSignatureFixture));

        string reference = BuildFor(
            image.Reader,
            fixture,
            nameof(StructuralSignatureFixture.ReferenceParameter),
            methodNameOverride: "Parameter",
            typeNameOverrides: overrides);
        string value = BuildFor(
            image.Reader,
            fixture,
            nameof(StructuralSignatureFixture.ValueParameter),
            methodNameOverride: "Parameter",
            typeNameOverrides: overrides);

        Assert.NotEqual(reference, value);
    }

    [Fact]
    public void Build_LengthPrefixesEveryNameSegmentation()
    {
        using var image = new MetadataImage(
            typeof(MethodStructuralSignatureTests).Assembly.Location);
        var firstType = FindType(image.Reader, nameof(StructuralKeyFixtureA));
        const string combined = "ABCD";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int split = 0; split <= combined.Length; split++)
        {
            string key = BuildFor(
                image.Reader,
                firstType,
                nameof(StructuralKeyFixtureA.M),
                methodNameOverride: combined[split..],
                typeNameOverrides: new Dictionary<TypeDefinitionHandle, string>
                {
                    [firstType] = combined[..split],
                });
            Assert.True(keys.Add(key), $"Split point {split} produced a duplicate key.");
        }
    }

    static string BuildFor(
        string typeName,
        string methodName,
        string? methodNameOverride = null)
    {
        using var image = new MetadataImage(typeof(MethodStructuralSignatureTests).Assembly.Location);
        var typeHandle = FindType(image.Reader, typeName);
        return BuildFor(
            image.Reader,
            typeHandle,
            methodName,
            methodNameOverride,
            typeNameOverrides: null);
    }

    static MethodStructuralSignatureShape BuildShapeFor(
        string typeName,
        string methodName,
        int? typeGenericParameterMethodOffset = null)
    {
        using var image = new MetadataImage(
            typeof(MethodStructuralSignatureTests).Assembly.Location);
        var typeHandle = FindType(image.Reader, typeName);
        var methodHandle = FindMethod(
            image.Reader,
            typeHandle,
            methodName);
        return MethodStructuralSignature.BuildShape(
            image.Reader,
            image.Reader.GetMethodDefinition(methodHandle),
            typeGenericParameterMethodOffset);
    }

    static string BuildFor(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string methodName,
        string? methodNameOverride,
        IReadOnlyDictionary<TypeDefinitionHandle, string>? typeNameOverrides)
    {
        var handle = FindMethod(reader, typeHandle, methodName);
        return MethodStructuralSignature.Build(
            reader,
            reader.GetMethodDefinition(handle),
            methodNameOverride,
            typeNameOverrides);
    }

    static TypeDefinitionHandle FindType(MetadataReader reader, string typeName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) == typeName)
                return typeHandle;
        }

        throw new InvalidOperationException($"Type '{typeName}' was not found.");
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string methodName)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        foreach (var methodHandle in type.GetMethods())
        {
            if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                return methodHandle;
        }

        throw new InvalidOperationException(
            $"Method '{reader.GetString(type.Name)}::{methodName}' was not found.");
    }

    static TypeDefinitionHandle FindNestedType(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        string typeName)
    {
        var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
        foreach (var typeHandle in declaringType.GetNestedTypes())
        {
            if (reader.GetString(reader.GetTypeDefinition(typeHandle).Name) == typeName)
                return typeHandle;
        }

        throw new InvalidOperationException(
            $"Nested type '{reader.GetString(declaringType.Name)}+{typeName}' was not found.");
    }

    static List<MethodDefinitionHandle> FindMethods(MetadataReader reader, string typeName, string methodName)
    {
        List<MethodDefinitionHandle> matches = [];
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    matches.Add(methodHandle);
            }
        }

        return matches;
    }

    sealed class MetadataImage : IDisposable
    {
        readonly Stream _stream;
        readonly PEReader _pe;

        public MetadataImage(string path)
        {
            _stream = File.OpenRead(path);
            _pe = new PEReader(_stream);
            Reader = _pe.GetMetadataReader();
        }

        public MetadataReader Reader { get; }

        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }

    sealed class CountingReadOnlyDictionary<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> inner)
        : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        public int LookupCount { get; private set; }

        public TValue this[TKey key] => inner[key];
        public IEnumerable<TKey> Keys => inner.Keys;
        public IEnumerable<TValue> Values => inner.Values;
        public int Count => inner.Count;

        public bool ContainsKey(TKey key)
            => inner.ContainsKey(key);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            => inner.GetEnumerator();

        public bool TryGetValue(TKey key, out TValue value)
        {
            LookupCount++;
            return inner.TryGetValue(key, out value!);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

public sealed class StructuralSignatureFixture
{
    public sealed class Inner;

    public int Transform(string value) => value.Length;

    public void Generic<TItem>(TItem item) => _ = item;

    public void GenericRenamed<TOther>(TOther item) => _ = item;

    public string TransformText(string value) => value;

    public void ByRef(ref int value) => value++;

    public void ByValue(int value) => _ = value;

    public void ByValueAgain(int value) => _ = value;

    public void In(in int value) => _ = value;

    public void Nested(Inner inner) => _ = inner;

    public static int StaticNoArgs() => 0;

    public int InstanceNoArgs() => 0;

    public unsafe void FnPtrManaged(delegate*<int, void> callback) => _ = (nint)callback;

    public unsafe void FnPtrUnmanaged(delegate* unmanaged<int, void> callback) => _ = (nint)callback;

    public void ReferenceConstrained<T>() where T : class { }

    public void ReferenceConstrainedAgain<T>() where T : class { }

    public void ValueConstrained<T>() where T : struct { }

    public void DisposableConstrained<T>() where T : IDisposable { }

    public void DisposableConstrainedAgain<T>() where T : IDisposable { }

    public void ComparableConstrained<T>() where T : IComparable<T> { }

    public void ConstraintsOrderOne<T>()
        where T : IDisposable, IComparable<T> { }

    public void ConstraintsOrderTwo<T>()
        where T : IComparable<T>, IDisposable { }

    public void ConstraintsByPositionOne<T, U>()
        where T : IDisposable
        where U : ICloneable { }

    public void ConstraintsByPositionTwo<T, U>()
        where T : ICloneable
        where U : IDisposable { }

    public void ReferenceParameter(ReferenceConstrainedFixture<object> value)
        => _ = value;

    public void ValueParameter(ValueConstrainedFixture<int> value)
        => _ = value;
}

public interface IStructuralSignatureFixture
{
    void Consume(in int value);

    void ConsumeRef(ref int value);

    void ConsumeValue(int value);
}

public sealed class StructuralShapeMarker<TOuter, TMethod>
{
    public void Match(TOuter outer, TMethod method)
    {
        _ = outer;
        _ = method;
    }
}

public static class StructuralShapeImplementation<TOuter>
{
    public static void Match<TMethod>(
        TOuter outer,
        TMethod method)
    {
        _ = outer;
        _ = method;
    }
}

public sealed class ReferenceConstrainedFixture<T>
    where T : class
{
    public void M() { }
}

public sealed class ReferenceConstrainedFixtureAgain<T>
    where T : class
{
    public void M() { }
}

public sealed class ValueConstrainedFixture<T>
    where T : struct
{
    public void M() { }
}

public sealed class StructuralKeyFixtureA
{
    public void M() { }
}
