namespace ILInspector.Decompiler.Tests;

// Declaration placement (#3591). A local the source declared inside a nested block
// is emitted as a bare declaration hoisted to the top of the method, because
// MetadataSource.LocalNames reads the portable PDB's LocalScope table for names only
// and drops each scope's StartOffset/EndOffset. These two shapes are the discriminator
// the PDB records and the printer currently ignores: in CreateNarrow the local's scope
// is the try block, in CreateHoisted it is the whole method, and both print the same
// way. Modeled on Azure.Data.Tables `TableClient.Create`. The related source shapes
// stay together here as one compiler-fixture group.
public sealed class DeclScopeGuard : IDisposable
{
    public void Failed(Exception e) { }

    public void Dispose() { }
}

public sealed class DeclScopeResult
{
    public string Value = "v";
    public int Raw;
}

public sealed class DeclScopeOps
{
    public DeclScopeResult Create(string name, int timeout) => new DeclScopeResult { Value = name, Raw = timeout };
}

public sealed class DeclScopeClient
{
    readonly DeclScopeOps _ops = new();

    // The local is read twice (so it survives as a slot rather than inlining) and is
    // never referenced outside the try, so the PDB scopes it to the try block alone.
    public string CreateNarrow(string name, int timeout)
    {
        using (DeclScopeGuard scope = new DeclScopeGuard())
        {
            try
            {
                DeclScopeResult response = _ops.Create(name, timeout);
                return response.Value + response.Raw;
            }
            catch (Exception ex)
            {
                new DeclScopeGuard().Failed(ex);
                throw;
            }
        }
    }

    // Control for the same shape: the catch arm reads the local, so the source
    // genuinely must declare it above the try and the PDB scopes it to the whole
    // method. Today's hoisting emitter is accidentally right here, which is why
    // placement alone cannot be read off the current output.
    public string CreateHoisted(string name, int timeout)
    {
        DeclScopeResult? response = null;
        try
        {
            response = _ops.Create(name, timeout);
            return response.Value + response.Raw;
        }
        catch (Exception ex)
        {
            new DeclScopeGuard().Failed(ex);
            return response is null ? "none" : response.Value;
        }
    }
}

public sealed class DeclScopeLoopClient
{
    readonly DeclScopeOps _ops = new();

    // The source declares the local inside the loop body and every use stays there,
    // so the PDB scope and the IR agree and the declaration sinks to its store.
    public int SumNarrow(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            DeclScopeResult step = _ops.Create("s", i);
            total += step.Raw + step.Value.Length;
        }
        return total;
    }

    // Close negative for the same PDB evidence. The scope is again nested (the using
    // block), but the first store sits inside one arm of the if while the read is
    // after it, so sinking the declaration onto that store would not compile. The IR
    // guard must decline and leave the hoisted declaration alone.
    public string CreateBranched(string name, int timeout, bool flag)
    {
        using (DeclScopeGuard scope = new DeclScopeGuard())
        {
            DeclScopeResult response;
            if (flag)
                response = _ops.Create(name, timeout);
            else
                response = _ops.Create(name, timeout + 1);
            return response.Value + response.Raw;
        }
    }
}
