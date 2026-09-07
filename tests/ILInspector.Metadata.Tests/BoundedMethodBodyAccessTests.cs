using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for the bounded method-body and user-string seam on
/// <see cref="MethodBodySource"/>, plus <see cref="AssemblyInspectionSession.ModuleVersionId"/>.
///
/// These are the facts a producer needs before it charges for work: how many
/// <c>MethodDef</c> rows exist and which of them carry IL (without decoding a single name), the
/// exact IL of an admitted body, a refusal that names the size it would have cost, and the exact
/// raw content of a <c>#US</c> entry inside a declared character budget. The inspected image is
/// this test assembly itself, compiled by the ordinary build — no crafted binary is involved, and
/// the malformed-input arms of the contract are exercised only where an ordinary image can reach
/// them (see <see cref="BoundedBody_DistinguishesNoBodyFromAMissingRow"/>).
/// </summary>
public class BoundedMethodBodyAccessTests
{
    static string SelfPath => typeof(BoundedMethodBodyAccessTests).Assembly.Location;

    // Generous enough that an ordinary sample body or literal is admitted on its own merits.
    const int AmpleILBytes = 4096;
    const int AmpleCharacters = 1024;

    [Fact]
    public void MethodRows_CountAndDescribeMatchTheEnumeratedInventory()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;

        IReadOnlyList<MethodBodyMember> enumerated = source.EnumerateMethods();
        Assert.Equal(enumerated.Count, source.MethodDefinitionCount);

        var scanned = new List<MethodRowDescription>(source.MethodDefinitionCount);
        for (int row = 1; row <= source.MethodDefinitionCount; row++)
        {
            Assert.True(source.TryDescribeMethod(row, out MethodRowDescription description));
            Assert.Equal(row, description.RowNumber);
            scanned.Add(description);
        }

        // Row order is table order; EnumerateMethods walks types, so compare as sets keyed by
        // token. The point is that the cheap scan sees exactly the same rows and body states.
        Assert.Equal(
            enumerated.OrderBy(method => method.MetadataToken)
                .Select(method => (method.MetadataToken, method.HasBody))
                .ToList(),
            scanned.OrderBy(description => description.MetadataToken)
                .Select(description => (description.MetadataToken, description.HasBody))
                .ToList());
    }

    [Fact]
    public void MethodRows_RejectRowsOutsideTheTable()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;

        Assert.False(source.TryDescribeMethod(0, out _));
        Assert.False(source.TryDescribeMethod(-1, out _));
        Assert.False(source.TryDescribeMethod(source.MethodDefinitionCount + 1, out _));
    }

    [Theory]
    [InlineData(nameof(BoundedSample.Add))]        // tiny header
    [InlineData(nameof(BoundedSample.Guarded))]    // fat header with exception clauses
    public void BoundedBody_ReturnsExactILWithinTheLimit(string methodName)
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int token = TokenOf(methodName);

        Assert.True(source.TryRead(token, out MethodBodyData? legacy, out string? error), error);
        var read = Assert.IsType<BoundedMethodBodyRead.Available>(
            source.ReadBounded(token, AmpleILBytes));

        // The platform reader is the oracle for the bytes: the bounded path reads the body's IL
        // extent itself, so it must agree with GetMethodBody for both header formats. It
        // deliberately does not materialize the exception regions the legacy path returns.
        Assert.Equal(legacy!.IL, read.IL);
        Assert.True(read.IL.Length <= AmpleILBytes);
    }

    [Fact]
    public void BoundedBody_MaterializesILWithoutExceptionRegions()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int token = TokenOf(nameof(BoundedSample.Guarded));

        // The sample has clauses, so the legacy whole-body path returns them...
        Assert.True(source.TryRead(token, out MethodBodyData? legacy, out string? error), error);
        Assert.NotEmpty(legacy!.ExceptionRegions);

        // ...while the bounded outcome carries IL and nothing else — the record has no
        // exception-region member to populate — which is what makes its working set a function of
        // the caller's byte limit rather than of the image.
        var read = Assert.IsType<BoundedMethodBodyRead.Available>(
            source.ReadBounded(token, AmpleILBytes));
        Assert.Equal(legacy.IL, read.IL);
    }

    [Fact]
    public void BoundedBody_RefusesAnOverLimitBodyWithItsTrueSize()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int token = TokenOf(nameof(BoundedSample.Add));
        Assert.True(source.TryRead(token, out MethodBodyData? legacy, out string? error), error);
        int actual = legacy!.IL.Length;

        var refused = Assert.IsType<BoundedMethodBodyRead.ByteLimitExceeded>(
            source.ReadBounded(token, actual - 1));
        Assert.Equal(actual, refused.ILByteCount);
        Assert.Equal(actual - 1, refused.MaxILBytes);

        // A zero budget is a legal "nothing left to spend", not an argument error.
        Assert.IsType<BoundedMethodBodyRead.ByteLimitExceeded>(source.ReadBounded(token, 0));

        // The exact-fit boundary is admitted, so the refusal is a limit and not an off-by-one.
        Assert.IsType<BoundedMethodBodyRead.Available>(source.ReadBounded(token, actual));

        Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBounded(token, -1));
    }

    [Fact]
    public void BoundedBody_DistinguishesNoBodyFromAMissingRow()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;

        // An abstract declaration has a definite absence of IL...
        Assert.IsType<BoundedMethodBodyRead.NoBody>(
            source.ReadBounded(
                TokenOf(typeof(BoundedSample.Shape), nameof(BoundedSample.Shape.Describe)),
                AmpleILBytes));

        // ...which is not the same answer as a row this image does not have,
        int missingRow = MetadataTokens.GetToken(
            MetadataTokens.MethodDefinitionHandle(source.MethodDefinitionCount + 1));
        var outOfRange = Assert.IsType<BoundedMethodBodyRead.Unreadable>(
            source.ReadBounded(missingRow, AmpleILBytes));
        Assert.Equal(MethodBodyReadFailure.RowOutOfRange, outOfRange.Reason);

        // nor a token that is not a MethodDef at all.
        var wrongKind = Assert.IsType<BoundedMethodBodyRead.Unreadable>(
            source.ReadBounded(typeof(BoundedSample).MetadataToken, AmpleILBytes));
        Assert.Equal(MethodBodyReadFailure.NotMethodDefinitionToken, wrongKind.Reason);

        var negative = Assert.IsType<BoundedMethodBodyRead.Unreadable>(
            source.ReadBounded(-1, AmpleILBytes));
        Assert.Equal(MethodBodyReadFailure.NotMethodDefinitionToken, negative.Reason);
    }

    [Fact]
    public void BoundedUserString_ReturnsRawContentWithinTheLimit()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int literalToken = LiteralOperand(source);

        var read = Assert.IsType<BoundedUserStringRead.Available>(
            source.ReadBoundedUserString(literalToken, AmpleCharacters));

        // Exact ordinal content: the accessor neither escapes, trims, nor folds case.
        Assert.Equal(BoundedSample.Literal, read.Value);
        Assert.Equal(source.ResolveUserString(literalToken), read.Value);
    }

    [Fact]
    public void BoundedUserString_RefusesAnOverLimitEntryWithItsTrueLength()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int literalToken = LiteralOperand(source);
        int length = BoundedSample.Literal.Length;

        var refused = Assert.IsType<BoundedUserStringRead.CharacterLimitExceeded>(
            source.ReadBoundedUserString(literalToken, length - 1));
        Assert.Equal(length, refused.CharacterCount);
        Assert.Equal(length - 1, refused.MaxCharacters);

        Assert.IsType<BoundedUserStringRead.CharacterLimitExceeded>(
            source.ReadBoundedUserString(literalToken, 0));

        var admitted = Assert.IsType<BoundedUserStringRead.Available>(
            source.ReadBoundedUserString(literalToken, length));
        Assert.Equal(BoundedSample.Literal, admitted.Value);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => source.ReadBoundedUserString(literalToken, -1));
    }

    [Fact]
    public void BoundedUserString_ReportsTokenAndRangeFailuresDistinctly()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;

        var wrongKind = Assert.IsType<BoundedUserStringRead.Unreadable>(
            source.ReadBoundedUserString(typeof(BoundedSample).MetadataToken, AmpleCharacters));
        Assert.Equal(UserStringReadFailure.NotUserStringToken, wrongKind.Reason);

        var past = Assert.IsType<BoundedUserStringRead.Unreadable>(
            source.ReadBoundedUserString(unchecked((int)0x70FFFFFF), AmpleCharacters));
        Assert.Equal(UserStringReadFailure.OffsetOutOfRange, past.Reason);

        // Offset zero is the heap's nil entry in every image, which reads as the empty string
        // rather than as a failure — matching System.Reflection.Metadata.
        var nil = Assert.IsType<BoundedUserStringRead.Available>(
            source.ReadBoundedUserString(unchecked((int)0x70000000), 0));
        Assert.Equal(string.Empty, nil.Value);
    }

    [Fact]
    public void ModuleVersionId_MatchesTheInspectedModule()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        Assert.Equal(
            typeof(BoundedMethodBodyAccessTests).Module.ModuleVersionId,
            session.ModuleVersionId());
        Assert.NotEqual(Guid.Empty, session.ModuleVersionId());
    }

    [Fact]
    public void BoundedReads_RejectUseAfterSessionDisposal()
    {
        var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int token = TokenOf(nameof(BoundedSample.Add));
        int literalToken = LiteralOperand(source);
        var body = Assert.IsType<BoundedMethodBodyRead.Available>(
            source.ReadBounded(token, AmpleILBytes));

        session.Dispose();

        // Already-materialized data is copied and outlives the session; new reads do not.
        Assert.NotEmpty(body.IL);
        Assert.Throws<ObjectDisposedException>(() => source.MethodDefinitionCount);
        Assert.Throws<ObjectDisposedException>(() => source.TryDescribeMethod(1, out _));
        Assert.Throws<ObjectDisposedException>(() => source.ReadBounded(token, AmpleILBytes));
        Assert.Throws<ObjectDisposedException>(
            () => source.ReadBoundedUserString(literalToken, AmpleCharacters));
        Assert.Throws<ObjectDisposedException>(() => session.ModuleVersionId());
    }

    [Fact]
    public void BoundedReads_RejectUseAfterTheLenderIsDisposed()
    {
        var context = PdbContext.Open(SelfPath);
        var session = AssemblyInspectionSession.Borrow(context);
        MethodBodySource source = session.MethodBodies;
        Assert.IsType<BoundedMethodBodyRead.Available>(
            source.ReadBounded(TokenOf(nameof(BoundedSample.Add)), AmpleILBytes));

        context.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => source.ReadBounded(TokenOf(nameof(BoundedSample.Add)), AmpleILBytes));
        Assert.Throws<ObjectDisposedException>(() => session.ModuleVersionId());
        session.Dispose();
    }

    /// <summary>
    /// The <c>#US</c> token carried by the <c>ldstr</c> that opens
    /// <see cref="BoundedSample.Literal"/>'s accessor, read out of the bounded body itself. This
    /// is how the named consumer reaches a literal, so the test reaches it the same way instead
    /// of guessing a heap offset.
    /// </summary>
    static int LiteralOperand(MethodBodySource source)
    {
        var read = Assert.IsType<BoundedMethodBodyRead.Available>(
            source.ReadBounded(TokenOf(nameof(BoundedSample.Text)), AmpleILBytes));
        ImmutableArray<byte> il = read.IL;

        Assert.Equal(0x72, il[0]);  // ldstr
        return BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(1, 4));
    }

    static int TokenOf(string methodName) => TokenOf(typeof(BoundedSample), methodName);

    static int TokenOf(Type declaringType, string methodName) =>
        declaringType.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Instance)!.MetadataToken;

    /// <summary>
    /// Ordinary compiled inputs: one tiny body, one literal, one bodyless declaration, and one
    /// fat body with exception clauses.
    /// </summary>
    public static class BoundedSample
    {
        public const string Literal = " bounded\0metadata\r\n\tCafe\u0301-\U0001F642 ";

        public static int Add(int left, int right) => left + right;

        public static string Text() => Literal;

        /// <summary>
        /// Compiles to a fat header with exception clauses, so the bounded read is exercised
        /// against both header formats and against a body whose clauses it declines to
        /// materialize.
        /// </summary>
        public static int Guarded(int value)
        {
            try
            {
                return checked(value + 1);
            }
            catch (OverflowException)
            {
                return -1;
            }
            finally
            {
                GC.KeepAlive(Literal);
            }
        }

        public abstract class Shape
        {
            public abstract string Describe();
        }
    }
}
