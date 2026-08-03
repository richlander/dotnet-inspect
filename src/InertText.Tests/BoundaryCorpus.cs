namespace InertText.Tests;

/// <summary>
/// The values the boundary sweeps run over: every <see cref="AdversarialCorpus"/> payload
/// encoded directly, plus the values only composition can produce.
/// </summary>
/// <remarks>
/// Direct encoding cannot emit two adjacent <c>\uXXXX</c> escapes that together spell one astral
/// scalar. A paired scalar is spelled as a single <c>\U</c> escape, and the corpus's lone
/// surrogates have no partner to pair with. That token is twelve characters wide and is the only
/// one in the alphabet whose width is not settled by its own prefix, so sweeping directly-encoded
/// payloads alone leaves the walker's subtlest arm to whatever hand-written cases exist —
/// breaking pair atomicity reddens those and nothing else, while every property sweep stays
/// green.
///
/// Composition reaches it, because <see cref="InertString.Join"/> encodes each fragment on its
/// own, so a surrogate pair split across two fragments arrives as two escapes.
/// </remarks>
public static class BoundaryCorpus
{
    private static readonly (string Name, InertString Value)[] Composed =
    [
        ("ComposedSurrogatePair", Compose("\uD83D", "\uDE00")),

        // The grouping here is greedy but not blind: the first escape spells a high surrogate
        // and so does the second, so they are not a pair and the first stands alone at six wide,
        // while the second and third are a pair at twelve. Reading left to right and taking any
        // two adjacent escapes as a pair would divide the real one.
        ("ComposedLoneHighBeforePair", Compose("\uD834", "\uD834", "\uDD73")),

        // The pair next to every other spelling in the alphabet, so a window can open and close
        // on either side of it rather than only at the ends of the value.
        ("ComposedPairAmongSpellings", Compose("a\u202E", "\uD83D", "\uDE00", "\\b\u0001")),
    ];

    public static TheoryData<string> Names
    {
        get
        {
            TheoryData<string> names = [];

            foreach (Adversary adversary in AdversarialCorpus.All)
            {
                names.Add(adversary.Name);
            }

            foreach ((string name, _) in Composed)
            {
                names.Add(name);
            }

            return names;
        }
    }

    public static InertString ByName(string name)
    {
        foreach ((string composed, InertString value) in Composed)
        {
            if (composed == name)
            {
                return value;
            }
        }

        return new InertString(TextPolicy.Field, AdversarialCorpus.ByName(name).Payload);
    }

    private static InertString Compose(params string[] fragments)
    {
        InertString[] parts = new InertString[fragments.Length];

        for (int i = 0; i < fragments.Length; i++)
        {
            parts[i] = new InertString(TextPolicy.Field, fragments[i]);
        }

        return InertString.Join(string.Empty, TextPolicy.Field, parts);
    }
}
