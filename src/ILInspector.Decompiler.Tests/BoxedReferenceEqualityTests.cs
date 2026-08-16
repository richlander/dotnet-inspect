using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class BoxedReferenceEqualityTests
{
    [Fact]
    public void StringReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.StringReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void DynamicReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void DynamicMemberReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicMemberReferenceEquals));

        Assert.Contains("return (object)(value.Member) == (object)right;", output);
    }

    [Fact]
    public void DynamicPropertyReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicPropertyReferenceEquals));

        Assert.Contains("return (object)carrier.Property == (object)right;", output);
    }

    [Fact]
    public void DynamicMethodReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicMethodReferenceEquals));

        Assert.Contains("return (object)carrier.Method() == (object)right;", output);
    }

    [Fact]
    public void ByRefDynamicMethodReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ByRefDynamicMethodReferenceEquals));

        Assert.Contains("return (object)(DynamicReference()) == (object)right;", output);
    }

    [Fact]
    public void ByRefObjectMethodReferenceEquality_RemainsPlainObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ByRefObjectMethodReferenceEquals));

        Assert.Contains("ObjectReference()", output);
        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void DynamicFieldReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicFieldReferenceEquals));

        Assert.Contains("return (object)carrier.Field == (object)right;", output);
    }

    [Fact]
    public void ByRefDynamicFieldReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ByRefDynamicFieldReferenceEquals));

        Assert.Contains("return (object)(carrier.Field) == (object)right;", output);
    }

    [Fact]
    public void ByRefObjectFieldReferenceEquality_RemainsPlainObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ByRefObjectFieldReferenceEquals));

        Assert.Contains("(carrier.Field) == right", output);
        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void DynamicConditionalReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicConditionalReferenceEquals));

        Assert.Contains("(object)(choose ? carrier.Property : carrier.Method()) == (object)right", output);
    }

    [Fact]
    public void GenericDynamicPropertyReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.GenericDynamicPropertyReferenceEquals));

        Assert.Contains("return (object)carrier.Value == (object)right;", output);
    }

    [Fact]
    public void GenericDynamicFieldReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.GenericDynamicFieldReferenceEquals));

        Assert.Contains("return (object)carrier.Slot == (object)right;", output);
    }

    [Fact]
    public void GenericByRefDynamicPropertyReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.GenericByRefDynamicPropertyReferenceEquals));

        Assert.Contains("return (object)(carrier.Reference) == (object)right;", output);
    }

    [Fact]
    public void DynamicArrayElementReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicArrayElementReferenceEquals));

        Assert.Contains("return (object)values[index] == (object)right;", output);
    }

    [Fact]
    public void ObjectArrayElementReferenceEquality_RemainsPlainObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ObjectArrayElementReferenceEquals));

        Assert.Contains("return values[index] == right;", output);
        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void DynamicAwaitReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicAwaitReferenceEquals));

        Assert.Contains("return (object)(await task) == (object)right;", output);
    }

    [Fact]
    public void DynamicSwitchExpressionReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicSwitchExpressionReferenceEquals));

        Assert.Contains(
            "(object)(selector switch { 0 => carrier.Field, 1 => carrier.Property, _ => carrier.Method() }) == (object)right",
            output);
    }

    [Fact]
    public void UserOperatorReferenceInequality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.UserReferenceNotEquals));

        Assert.Contains("return (object)left != (object)right;", output);
    }

    [Fact]
    public void InheritedUserOperatorReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DerivedReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void CrossAssemblyInheritedOperatorReferenceInequality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DelegateReferenceNotEquals));

        Assert.Contains("return (object)left != (object)right;", output);
    }

    [Fact]
    public void MixedBoxedReferenceEquality_CastsBothOperands()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.MixedBoxedReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void UserOperatorCall_RemainsAnOperatorCall()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.UserOperatorEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void OperatorFreeClassReferenceEquality_RemainsUncast()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.PlainReferenceEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void ObjectReferenceEquality_RemainsUncast()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ObjectReferenceEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void CrossAssemblyOperatorFreeClassReferenceEquality_RemainsUncast()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.StringBuilderReferenceEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void UnrelatedOperatorFreeReferenceEquality_CastsBothOperands()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.UnrelatedReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void NullComparison_RemainsAnIsPattern()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.StringIsNull));

        Assert.Contains("return value is null;", output);
        Assert.DoesNotContain("(object)value", output);
    }

    [Fact]
    public void BoxedGenericEquality_PreservesObjectCasts()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.IndexOfReference));

        Assert.Contains("if ((object)item == (object)list[i])", output);
        Assert.DoesNotContain("if ((item) == (list[i]))", output);
    }

    [Fact]
    public void BoxedGenericInequality_PreservesObjectCasts()
    {
        var generic = TypeRef.MethodGenericParameter(0, "T");
        var comparison = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new Box(generic, new LoadArgument(0, "left", generic)),
            new Box(generic, new LoadArgument(1, "right", generic)));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [new Parameter("left", generic), new Parameter("right", generic)],
                HasThis: false,
                GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("return (object)left != (object)right;", output);
        Assert.DoesNotContain("return left != right;", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedReferenceProducingExpression_PreservesObjectComparison(bool castClass)
    {
        var unresolved = TypeRef.Definition("External", "External", "OperatorType");
        var rightType = unresolved.WithValueTypeHint(ValueTypeHint.ReferenceType);
        IrExpression left = castClass
            ? new CastClass(unresolved, new LoadArgument(0, "value", TypeRef.CoreLib("System", "Object")))
            : new IsInstance(unresolved, new LoadArgument(0, "value", TypeRef.CoreLib("System", "Object")));
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            left,
            new LoadArgument(1, "right", rightType));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [
                    new Parameter("value", TypeRef.CoreLib("System", "Object")),
                    new Parameter("right", rightType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        string output = CSharpPrinter.Print(function).Output!;

        string expectedLeft = castClass
            ? "(object)((OperatorType)value)"
            : "(object)(value as OperatorType)";
        Assert.Contains($"return {expectedLeft} == (object)right;", output);
    }

    [Fact]
    public void NativeIntegerNewObjectComparison_RemainsAValueComparison()
    {
        var nativeInt = TypeRef.CoreLib("System", "IntPtr").WithValueTypeHint(ValueTypeHint.ValueType);
        var int32 = TypeRef.CoreLib("System", "Int32");
        var constructor = new MethodRef(
            nativeInt,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [int32],
            HasThis: true);
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new NewObject(constructor, [new Constant(1, int32)]),
            new NewObject(constructor, [new Constant(2, int32)]));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        string output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    static string Print(string methodName)
    {
        using var context = new MetadataContext(TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());
        using var source = MetadataSource.Open(
            typeof(BoxedReferenceEqualitySpecimens).Assembly.Location,
            context: context);
        var function = IrImporter.Import(
            source,
            typeof(BoxedReferenceEqualitySpecimens).FullName!,
            methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }
}

public static class BoxedReferenceEqualitySpecimens
{
    static dynamic s_dynamicReference = new UserEquality();
    static object s_objectReference = new UserEquality();

    public static bool StringReferenceEquals(string left, string right)
        => (object)left == right;

    public static bool DynamicReferenceEquals(dynamic left, dynamic right)
        => (object)left == (object)right;

    public static bool DynamicMemberReferenceEquals(dynamic value, object right)
        => (object)value.Member == right;

    public static bool DynamicPropertyReferenceEquals(DynamicCarrier carrier, object right)
        => (object)carrier.Property == right;

    public static bool DynamicMethodReferenceEquals(DynamicCarrier carrier, object right)
        => (object)carrier.Method() == right;

    public static ref dynamic DynamicReference() => ref s_dynamicReference;

    public static bool ByRefDynamicMethodReferenceEquals(object right)
        => (object)DynamicReference() == right;

    public static ref object ObjectReference() => ref s_objectReference;

    public static bool ByRefObjectMethodReferenceEquals(object right)
        => (object)ObjectReference() == right;

    public static bool DynamicFieldReferenceEquals(DynamicCarrier carrier, object right)
        => (object)carrier.Field == right;

    public static bool ByRefDynamicFieldReferenceEquals(
        ref RefDynamicFieldCarrier carrier,
        object right)
        => (object)carrier.Field == right;

    public static bool ByRefObjectFieldReferenceEquals(
        ref RefObjectFieldCarrier carrier,
        object right)
        => (object)carrier.Field == right;

    public static bool DynamicConditionalReferenceEquals(DynamicCarrier carrier, bool choose, object right)
        => (object)(choose ? carrier.Property : carrier.Method()) == right;

    public static bool GenericDynamicPropertyReferenceEquals(
        GenericDynamicCarrier<dynamic> carrier,
        object right)
        => (object)carrier.Value == right;

    public static bool GenericDynamicFieldReferenceEquals(
        GenericDynamicCarrier<dynamic> carrier,
        object right)
        => (object)carrier.Slot == right;

    public static bool GenericByRefDynamicPropertyReferenceEquals(
        GenericDynamicCarrier<dynamic> carrier,
        object right)
        => (object)carrier.Reference == right;

    public static bool DynamicArrayElementReferenceEquals(
        dynamic[] values,
        int index,
        object right)
        => (object)values[index] == right;

    public static bool ObjectArrayElementReferenceEquals(
        object[] values,
        int index,
        object right)
        => (object)values[index] == right;

    public static async System.Threading.Tasks.Task<bool> DynamicAwaitReferenceEquals(
        System.Threading.Tasks.Task<dynamic> task,
        object right)
        => (object)(await task) == right;

    public static bool DynamicSwitchExpressionReferenceEquals(
        DynamicCarrier carrier,
        int selector,
        object right)
        => (object)(selector switch
        {
            0 => carrier.Field,
            1 => carrier.Property,
            _ => carrier.Method(),
        }) == right;

    public static bool UserReferenceNotEquals(UserEquality left, UserEquality right)
        => (object)left != right;

    public static bool DerivedReferenceEquals(DerivedEquality left, DerivedEquality right)
        => (object)left == right;

    public static bool DelegateReferenceNotEquals(EventHandler left, EventHandler right)
        => (object)left != right;

    public static bool MixedBoxedReferenceEquals<T>(T left, MixedEquality right)
        => (object?)left == (object)right;

    public static bool UserOperatorEquals(UserEquality left, UserEquality right)
        => left == right;

    public static bool PlainReferenceEquals(PlainReference left, PlainReference right)
        => left == right;

    public static bool ObjectReferenceEquals(object left, object right)
        => left == right;

    public static bool StringBuilderReferenceEquals(
        System.Text.StringBuilder left,
        System.Text.StringBuilder right)
        => (object)left == right;

    public static bool UnrelatedReferenceEquals(
        FirstPlainReference left,
        SecondPlainReference right)
        => (object)left == (object)right;

    public static bool StringIsNull(string value)
        => value is null;

    public static int IndexOfReference<T>(System.Collections.Generic.List<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if ((object?)item == (object?)list[i])
                return i;
        }

        return -1;
    }
}

public sealed class DynamicCarrier
{
    readonly UserEquality _value = new();
    public dynamic Field = new UserEquality();

    public dynamic Property => _value;

    public dynamic Method() => _value;
}

public ref struct RefDynamicFieldCarrier
{
    public ref dynamic Field;

    public RefDynamicFieldCarrier(ref dynamic field)
    {
        Field = ref field;
    }
}

public ref struct RefObjectFieldCarrier
{
    public ref object Field;

    public RefObjectFieldCarrier(ref object field)
    {
        Field = ref field;
    }
}

public sealed class GenericDynamicCarrier<T>
{
    public T Value { get; init; } = default!;
    public T Slot = default!;
    T _reference = default!;
    public ref T Reference => ref _reference;
}

public sealed class UserEquality
{
    public int Value { get; init; }

    public static bool operator ==(UserEquality? left, UserEquality? right)
        => left?.Value == right?.Value;

    public static bool operator !=(UserEquality? left, UserEquality? right)
        => !(left == right);

    public override bool Equals(object? obj)
        => obj is UserEquality other && this == other;

    public override int GetHashCode()
        => Value;
}

public sealed class PlainReference;

public class BaseEquality
{
    public static bool operator ==(BaseEquality? left, BaseEquality? right)
        => ReferenceEquals(left, right);

    public static bool operator !=(BaseEquality? left, BaseEquality? right)
        => !ReferenceEquals(left, right);

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj);

    public override int GetHashCode()
        => 0;
}

public sealed class DerivedEquality : BaseEquality;

public sealed class MixedEquality
{
    public static bool operator ==(object? left, MixedEquality? right)
        => false;

    public static bool operator !=(object? left, MixedEquality? right)
        => true;

    public override bool Equals(object? obj)
        => false;

    public override int GetHashCode()
        => 0;
}

public sealed class FirstPlainReference;

public sealed class SecondPlainReference;
