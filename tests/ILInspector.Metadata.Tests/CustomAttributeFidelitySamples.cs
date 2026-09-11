using System.Collections.Immutable;
using System.Reflection.Metadata;
using AttributeEnumFixtures;

namespace ILInspector.Metadata.Tests;

internal static class CustomAttributeFidelitySamples
{
    const string LongText =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789--"
        + "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789--"
        + "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789--";

    public static IEnumerable<object[]> IndependentCases =>
    [
        [typeof(Primitives)],
        [typeof(Strings)],
        [typeof(Types)],
        [typeof(Arrays)],
        [typeof(NullArrays)],
        [typeof(Boxed)],
        [typeof(NestedArrays)],
        [typeof(Named)],
    ];

    public static IEnumerable<object[]> EnumCases =>
    [
        [typeof(FixedEnum), Value(
            [Arg(ProducerTruth.WideName, ProducerTruth.Negative), Arg("int", 17)], [])],
        [typeof(NamedEnums), Value(
            [Arg(ProducerTruth.WideName, ProducerTruth.Positive), Arg("int", 19)],
            [
                new("Field", CustomAttributeNamedArgumentKind.Field,
                    ProducerTruth.WideName, ProducerTruth.Negative),
                new("Values", CustomAttributeNamedArgumentKind.Property,
                    ProducerTruth.WideName + "[]", WideValues()),
                new("Boxed", CustomAttributeNamedArgumentKind.Property,
                    ProducerTruth.WideName, ProducerTruth.Positive),
            ])],
        [typeof(EnumArray), Value(
            [Arg(ProducerTruth.WideName + "[]", WideValues()), Arg("int", 23)], [])],
        [typeof(ByteEnum), Value(
            [Arg(ProducerTruth.NarrowName, ProducerTruth.NarrowValue), Arg("int", 29)], [])],
    ];

    static CustomAttributeValue<string> Value(
        ImmutableArray<CustomAttributeTypedArgument<string>> fixedArguments,
        ImmutableArray<CustomAttributeNamedArgument<string>> namedArguments)
        => new(fixedArguments, namedArguments);

    static CustomAttributeTypedArgument<string> Arg(string type, object? value)
        => new(type, value);

    static ImmutableArray<CustomAttributeTypedArgument<string>> WideValues() =>
    [
        Arg(ProducerTruth.WideName, ProducerTruth.Negative),
        Arg(ProducerTruth.WideName, ProducerTruth.Positive),
    ];

    [Primitive(true, '\u03bb', -101, 201, -30_001, 60_001,
        -2_000_000_001, 4_000_000_001U, -5_000_000_001L, 12_000_000_001UL,
        -0.0f, double.NaN)]
    public sealed class Primitives;

    [Text(null, "", "small", LongText)]
    public sealed class Strings;

    [TypeValues(typeof(int), typeof(string[]), typeof(Dictionary<string, int>), null)]
    public sealed class Types;

    [ArrayValues(new[] { -3, 0, 7 }, new string?[] { null, "", LongText })]
    public sealed class Arrays;

    [ArrayValues(null, null)]
    public sealed class NullArrays;

    [Objects(7, "text", typeof(int), null, new int[0])]
    public sealed class Boxed;

    [Objects(new object[] { new int[] { 3, 5 }, new object[] { "nested", null! } })]
    public sealed class NestedArrays;

    [NamedValues(Field = 31, Text = "named", Values = new object[] { 37, "value" })]
    public sealed class Named;

    [WideValue(Wide.Negative, 17)]
    public sealed class FixedEnum;

    [WideValue(Wide.Positive, 19, Field = Wide.Negative,
        Values = new[] { Wide.Negative, Wide.Positive }, Boxed = Wide.Positive)]
    public sealed class NamedEnums;

    [WideArray(new[] { Wide.Negative, Wide.Positive }, 23)]
    public sealed class EnumArray;

    [NarrowValue(Narrow.Value, 29)]
    public sealed class ByteEnum;

    sealed class PrimitiveAttribute : Attribute
    {
        public PrimitiveAttribute(bool boolean, char character, sbyte signedByte, byte unsignedByte,
            short signedShort, ushort unsignedShort, int signedInt, uint unsignedInt,
            long signedLong, ulong unsignedLong, float single, double @double) { }
    }

    sealed class TextAttribute : Attribute
    {
        public TextAttribute(params string?[] values) { }
    }

    sealed class TypeValuesAttribute : Attribute
    {
        public TypeValuesAttribute(params Type?[] values) { }
    }

    sealed class ArrayValuesAttribute : Attribute
    {
        public ArrayValuesAttribute(int[]? numbers, string?[]? strings) { }
    }

    sealed class ObjectsAttribute : Attribute
    {
        public ObjectsAttribute(params object?[] values) { }
    }

    sealed class NamedValuesAttribute : Attribute
    {
        public int Field = 0;
        public string? Text { get; set; }
        public object[]? Values { get; set; }
    }

    sealed class WideValueAttribute : Attribute
    {
        public WideValueAttribute(Wide value, int following) { }
        public Wide Field = 0;
        public Wide[]? Values { get; set; }
        public object? Boxed { get; set; }
    }

    sealed class WideArrayAttribute : Attribute
    {
        public WideArrayAttribute(Wide[] values, int following) { }
    }

    sealed class NarrowValueAttribute : Attribute
    {
        public NarrowValueAttribute(Narrow value, int following) { }
    }
}
