# Compiled inspection domain model

`CompiledInspectionDomain.tla` models the interaction owned by
[Compiled Inspection Domain Composition](../../section-pipeline.md#compiled-inspection-domain-composition):
multiple immutable section lenses share one immutable typed-query domain while
independent requests plan, execute, cancel, and release caller-owned contexts.

The model is a design specification. TLC checks that the design's rules are
consistent over the bounded interleavings below; it does not prove that the C#
implementation conforms to them.

## Model boundary

The model contains:

- one producer domain with three queries;
- two valid section lenses with overlapping demand;
- one lens containing a foreign query that binding must reject;
- two independent requests;
- one optional host-demand query per request;
- two caller-owned contexts; and
- one owner-issued prerequisite closure in which `QueryB` requires `QueryA`.

Lens binding and query-plan construction are atomic abstractions. Request
execution has explicit planned, running, succeeded, and cancelled states.
Running execution borrows one context; terminal execution must release that
borrow without disposing the context.

The model deliberately excludes query algorithms, dependency execution steps,
result payloads, acquisition, workspace admission, resource budgets, row
selection, rendering, and sink behavior. Those remain with their existing
owners.

## Checked properties

| Property | Claim |
| --- | --- |
| `TypeOK` | Every state remains within the declared finite shape. |
| `LensBindingsUseOnlyDomainQueries` | A compiled lens cannot bind a query outside its producer domain. |
| `RejectedLensesStayUnbound` | A rejected lens never becomes executable. |
| `PlanMatchesOwnDemand` | Each request receives the query owner's closure over that request's own lens and host demand. |
| `PlansUseOnlyDomainQueries` | Every executable query belongs to the producer domain. |
| `BorrowedContextOnlyWhileRunning` | Composition retains a supplied context only during active execution. |
| `CompositionNeverDisposesContext` | The composition owner never disposes caller-owned context. |
| `CancellationSuppressesSuccess` | Once cancellation wins, the request cannot later publish success. |
| `PublishedResultMatchesOwnPlan` | A published result belongs to the exact request plan that produced it. |
| `ResultShapeIsConsistent` | Success and result publication occur together. |
| `EveryActiveRequestEventuallyTerminates` | Under weak fairness, planned or running internal work eventually succeeds or cancels. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks all safety and liveness properties over two interleaved requests. |
| `BrokenForeignLens.cfg` | Admits the lens containing a foreign query; TLC must violate `LensBindingsUseOnlyDomainQueries`. |
| `BrokenCancelledSuccess.cfg` | Allows cancelled execution to publish success; TLC must violate `CancellationSuppressesSuccess`. |
| `BrokenRetainedContext.cfg` | Retains the supplied context after success; TLC must violate `BorrowedContextOnlyWhileRunning`. |
| `BrokenContextDisposal.cfg` | Disposes caller context during cancellation; TLC must violate `CompositionNeverDisposesContext`. |
| `BrokenPlanIsolation.cfg` | Reuses another request's plan; TLC must violate `PlanMatchesOwnDemand`. |

## Running TLC

Use the repository-pinned `v1.8.0` `tla2tools.jar`:

```bash
cd docs/design/models/compiled-inspection-domain
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -coverage 1 -config Safety.cfg \
  CompiledInspectionDomain.tla
for config in BrokenForeignLens BrokenCancelledSuccess \
  BrokenRetainedContext BrokenContextDisposal BrokenPlanIsolation; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
    CompiledInspectionDomain.tla
done
```

Run the commands sequentially because TLC processes in one directory share the
default checkpoint path unless each receives a separate `-metadir`.

## Implementation alignment

The model and Release tests provide different evidence:

| Model rule | Implementation gate |
| --- | --- |
| Lens queries belong to one domain | `CompiledLens_RejectsQueryOutsideProducerDomain` |
| Multiple lenses share immutable producer declarations | `CompiledDomain_MultipleLensesShareOneQueryCatalog` |
| Each request gets its own selected plan | `CompiledLens_LowersEmptySingleAndMultiQueryDemand` |
| Results remain exact L1 values | `CompiledExecution_DoesNotTransformTypedQueryResults` |
| Cancellation cannot become success | `CompiledExecution_ForwardsAsyncCancellation` |
| Context ownership stays with caller | `CompiledExecution_DoesNotRetainOrDisposeSuppliedContext` |

## TLC evidence

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned TLA+ `v1.8.0`
prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 5,781 | 1,368 | 10 |
| `BrokenForeignLens.cfg` | `LensBindingsUseOnlyDomainQueries` violated | 9 | 8 | 4 |
| `BrokenCancelledSuccess.cfg` | `CancellationSuppressesSuccess` violated | 1,135 | 468 | 7 |
| `BrokenRetainedContext.cfg` | `BorrowedContextOnlyWhileRunning` violated | 736 | 356 | 8 |
| `BrokenContextDisposal.cfg` | `CompositionNeverDisposesContext` violated | 212 | 109 | 6 |
| `BrokenPlanIsolation.cfg` | `PlanMatchesOwnDemand` violated | 261 | 143 | 6 |

The normal configuration exhausted its complete bounded state graph and
checked two temporal-property branches over 2,736 total distinct
behavior-checking states. Action coverage was nonzero for lens settlement,
request planning, start, cancellation, and completion. Each broken
configuration stopped at its expected named counterexample.

The checked model produced no material counterexample. The mutations establish
that its safety claims depend on foreign-query rejection, request-local
planning, cancellation finality, and caller-owned context lifetime; they do
not establish implementation conformance.
