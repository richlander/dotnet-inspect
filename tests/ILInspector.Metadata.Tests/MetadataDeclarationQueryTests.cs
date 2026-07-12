using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Reflection;
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
    public void IsVolatileField_DetectsVolatileModreq()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var context = GenericContext.ForType(Reader, type);
        Assert.True(MetadataDeclarationQuery.IsVolatileField(GetField(type, "VolatileField"), context));
        Assert.False(MetadataDeclarationQuery.IsVolatileField(GetField(type, "PlainField"), context));
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

    public volatile int VolatileField;

    public int PlainField;

    public void StructConstraint<T>() where T : struct { }

    public void ClassNewConstraint<T>() where T : class, new() { }

    public abstract class AbstractBase
    {
        protected abstract string Name { get; set; }
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
