using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using QuerySpace.Vocabulary;

namespace QuerySpace.Explanation;

/// <summary>A finite scalar value carried by an explanation value.</summary>
public sealed class ExplanationScalarValue :
    IEquatable<ExplanationScalarValue>
{
    [JsonConstructor]
    public ExplanationScalarValue(
        ExplanationScalarKind kind,
        bool? boolean,
        BigInteger? integer,
        decimal? @decimal,
        double? binaryFloatingPoint,
        string? text,
        ImmutableArray<byte> octets)
    {
        Kind = kind;
        Boolean = boolean;
        Integer = integer;
        Decimal = @decimal;
        BinaryFloatingPoint = binaryFloatingPoint;
        Text = text;
        Octets = kind == ExplanationScalarKind.Octets && octets.IsDefault
            ? []
            : octets;
        Validate();
    }

    public ExplanationScalarKind Kind { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Boolean { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BigInteger? Integer { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Decimal { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BinaryFloatingPoint { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<byte> Octets { get; }

    public static ExplanationScalarValue FromBoolean(bool value) =>
        new(
            ExplanationScalarKind.Boolean,
            value,
            null,
            null,
            null,
            null,
            default);

    public static ExplanationScalarValue FromInteger(BigInteger value) =>
        new(
            ExplanationScalarKind.Integer,
            null,
            value,
            null,
            null,
            null,
            default);

    public static ExplanationScalarValue FromDecimal(decimal value) =>
        new(
            ExplanationScalarKind.Decimal,
            null,
            null,
            value,
            null,
            null,
            default);

    public static ExplanationScalarValue FromBinaryFloatingPoint(
        double value) =>
        new(
            ExplanationScalarKind.BinaryFloatingPoint,
            null,
            null,
            null,
            value,
            null,
            default);

    public static ExplanationScalarValue FromText(string value) =>
        new(
            ExplanationScalarKind.Text,
            null,
            null,
            null,
            null,
            value,
            default);

    public static ExplanationScalarValue FromOctets(
        IEnumerable<byte> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(
            ExplanationScalarKind.Octets,
            null,
            null,
            null,
            null,
            null,
            [.. value]);
    }

    public bool Equals(ExplanationScalarValue? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || Kind != other.Kind)
            return false;

        return Kind switch
        {
            ExplanationScalarKind.Boolean =>
                Boolean == other.Boolean,
            ExplanationScalarKind.Integer =>
                Integer == other.Integer,
            ExplanationScalarKind.Decimal =>
                Decimal == other.Decimal,
            ExplanationScalarKind.BinaryFloatingPoint =>
                BinaryFloatingPoint == other.BinaryFloatingPoint,
            ExplanationScalarKind.Text =>
                string.Equals(Text, other.Text, StringComparison.Ordinal),
            ExplanationScalarKind.Octets =>
                Octets.AsSpan().SequenceEqual(other.Octets.AsSpan()),
            _ => false,
        };
    }

    public override bool Equals(object? obj) =>
        Equals(obj as ExplanationScalarValue);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        switch (Kind)
        {
            case ExplanationScalarKind.Boolean:
                hash.Add(Boolean);
                break;
            case ExplanationScalarKind.Integer:
                hash.Add(Integer);
                break;
            case ExplanationScalarKind.Decimal:
                hash.Add(Decimal);
                break;
            case ExplanationScalarKind.BinaryFloatingPoint:
                hash.Add(BinaryFloatingPoint);
                break;
            case ExplanationScalarKind.Text:
                hash.Add(Text, StringComparer.Ordinal);
                break;
            case ExplanationScalarKind.Octets:
                foreach (byte value in Octets)
                    hash.Add(value);
                break;
        }
        return hash.ToHashCode();
    }

    internal int CanonicalByteCount() =>
        Kind switch
        {
            ExplanationScalarKind.Boolean => 2,
            ExplanationScalarKind.Integer =>
                1 + Encoding.UTF8.GetByteCount(
                    Integer!.Value.ToString(
                        CultureInfo.InvariantCulture)),
            ExplanationScalarKind.Decimal =>
                1 + Encoding.UTF8.GetByteCount(
                    Decimal!.Value.ToString(
                        "G29",
                        CultureInfo.InvariantCulture)),
            ExplanationScalarKind.BinaryFloatingPoint =>
                1 + Encoding.UTF8.GetByteCount(
                    BinaryFloatingPoint!.Value.ToString(
                        "R",
                        CultureInfo.InvariantCulture)),
            ExplanationScalarKind.Text =>
                1 + Encoding.UTF8.GetByteCount(Text!),
            ExplanationScalarKind.Octets => 1 + Octets.Length,
            _ => throw new InvalidOperationException(
                "Unknown explanation scalar kind."),
        };

    private void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        if (BinaryFloatingPoint is { } floating
            && !double.IsFinite(floating))
        {
            throw new ArgumentOutOfRangeException(
                nameof(BinaryFloatingPoint),
                "Explanation floating-point values must be finite.");
        }
        if (Text is not null)
            ExplanationContract.ValidateText(Text, nameof(Text));

        int populated =
            (Boolean is null ? 0 : 1)
            + (Integer is null ? 0 : 1)
            + (Decimal is null ? 0 : 1)
            + (BinaryFloatingPoint is null ? 0 : 1)
            + (Text is null ? 0 : 1)
            + (Octets.IsDefault ? 0 : 1);
        bool valid = Kind switch
        {
            ExplanationScalarKind.Boolean =>
                Boolean is not null && populated == 1,
            ExplanationScalarKind.Integer =>
                Integer is not null && populated == 1,
            ExplanationScalarKind.Decimal =>
                Decimal is not null && populated == 1,
            ExplanationScalarKind.BinaryFloatingPoint =>
                BinaryFloatingPoint is not null && populated == 1,
            ExplanationScalarKind.Text =>
                Text is not null && populated == 1,
            ExplanationScalarKind.Octets =>
                populated == 1,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "Exactly the carrier selected by the scalar kind must be "
                + "populated.");
        }
    }
}

/// <summary>One field and its ordered values in a record value.</summary>
public sealed class ExplanationRecordFieldValue :
    IEquatable<ExplanationRecordFieldValue>
{
    [JsonConstructor]
    public ExplanationRecordFieldValue(
        ExplanationFieldIdentity field,
        ImmutableArray<ExplanationValue> values)
    {
        ExplanationContract.ValidateIdentity(
            field.Value,
            nameof(field));
        Field = field;
        Values = values.IsDefault ? [] : values;
        if (Values.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Record field values must not contain null.",
                nameof(values));
        }
    }

    public ExplanationRecordFieldValue(
        ExplanationFieldIdentity field,
        IEnumerable<ExplanationValue>? values)
        : this(field, [.. values ?? []])
    {
    }

    public ExplanationFieldIdentity Field { get; }

    public ImmutableArray<ExplanationValue> Values { get; }

    public bool Equals(ExplanationRecordFieldValue? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && Field == other.Field
            && Values.SequenceEqual(other.Values));

    public override bool Equals(object? obj) =>
        Equals(obj as ExplanationRecordFieldValue);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Field);
        foreach (ExplanationValue value in Values)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>One schema-conforming embedded explanation value.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExplanationValue.Scalar), "scalar")]
[JsonDerivedType(typeof(ExplanationValue.VocabularyTerm), "vocabularyTerm")]
[JsonDerivedType(typeof(ExplanationValue.Record), "record")]
[JsonDerivedType(typeof(ExplanationValue.Choice), "choice")]
public abstract class ExplanationValue : IEquatable<ExplanationValue>
{
    private ExplanationValue()
    {
    }

    public sealed class Scalar : ExplanationValue
    {
        public Scalar(ExplanationScalarValue value)
        {
            Value = value
                ?? throw new ArgumentNullException(nameof(value));
        }

        public ExplanationScalarValue Value { get; }

        protected override bool EqualsCore(ExplanationValue other) =>
            Value.Equals(((Scalar)other).Value);

        protected override int GetHashCodeCore() => Value.GetHashCode();
    }

    public sealed class VocabularyTerm : ExplanationValue
    {
        public VocabularyTerm(VocabularyTermIdentity identity)
        {
            ExplanationContract.ValidateIdentity(
                identity.Value,
                nameof(identity));
            Identity = identity;
        }

        public VocabularyTermIdentity Identity { get; }

        protected override bool EqualsCore(ExplanationValue other) =>
            Identity == ((VocabularyTerm)other).Identity;

        protected override int GetHashCodeCore() =>
            Identity.GetHashCode();
    }

    public sealed class Record : ExplanationValue
    {
        [JsonConstructor]
        public Record(ImmutableArray<ExplanationRecordFieldValue> fields)
        {
            Fields = fields.IsDefault ? [] : fields;
            if (Fields.Any(static field => field is null)
                || Fields.Select(static field => field.Field)
                    .Distinct()
                    .Count()
                    != Fields.Length)
            {
                throw new ArgumentException(
                    "Record values must contain unique non-null fields.",
                    nameof(fields));
            }
        }

        public Record(IEnumerable<ExplanationRecordFieldValue> fields)
            : this(
                [
                    .. fields
                        ?? throw new ArgumentNullException(nameof(fields)),
                ])
        {
        }

        public ImmutableArray<ExplanationRecordFieldValue> Fields { get; }

        protected override bool EqualsCore(ExplanationValue other) =>
            Fields.SequenceEqual(((Record)other).Fields);

        protected override int GetHashCodeCore()
        {
            var hash = new HashCode();
            foreach (ExplanationRecordFieldValue field in Fields)
                hash.Add(field);
            return hash.ToHashCode();
        }
    }

    public sealed class Choice : ExplanationValue
    {
        public Choice(
            ExplanationChoiceCaseIdentity @case,
            ExplanationValue? value = null)
        {
            ExplanationContract.ValidateIdentity(
                @case.Value,
                nameof(@case));
            Case = @case;
            Value = value;
        }

        public ExplanationChoiceCaseIdentity Case { get; }

        public ExplanationValue? Value { get; }

        protected override bool EqualsCore(ExplanationValue other)
        {
            var choice = (Choice)other;
            return Case == choice.Case
                && Equals(Value, choice.Value);
        }

        protected override int GetHashCodeCore() =>
            HashCode.Combine(Case, Value);
    }

    /// <summary>One text scalar value.</summary>
    public static ExplanationValue Text(string value) =>
        new Scalar(ExplanationScalarValue.FromText(value));

    /// <summary>One integer scalar value.</summary>
    public static ExplanationValue Integer(BigInteger value) =>
        new Scalar(ExplanationScalarValue.FromInteger(value));

    /// <summary>One Boolean scalar value.</summary>
    public static ExplanationValue Boolean(bool value) =>
        new Scalar(ExplanationScalarValue.FromBoolean(value));

    public bool Equals(ExplanationValue? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && GetType() == other.GetType()
            && EqualsCore(other));

    public sealed override bool Equals(object? obj) =>
        Equals(obj as ExplanationValue);

    public sealed override int GetHashCode() =>
        HashCode.Combine(GetType(), GetHashCodeCore());

    protected abstract bool EqualsCore(ExplanationValue other);

    protected abstract int GetHashCodeCore();
}

/// <summary>Finite structural measurements of one embedded value.</summary>
public readonly record struct ExplanationValueMeasurement(
    int CanonicalByteCount,
    int Depth,
    int NodeCount);
