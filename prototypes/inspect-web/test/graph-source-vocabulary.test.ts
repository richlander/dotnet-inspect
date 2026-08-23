import assert from "node:assert/strict";
import test from "node:test";

import {
  graphSourceIsOpen,
  graphSourceStatuses,
  type GraphSourceStatus,
} from "../src/data.ts";
import {
  graphSourceAutoLoad,
  type GraphSourceState,
} from "../src/source-inspection.ts";
import type { BrowserSource } from "../src/inspect-web-engine.d.ts";

function source(text: string): BrowserSource {
  return {
    provider: "pdb",
    provenance: "SourceLink",
    url: "https://example.test/source.cs",
    pdbSourceLimitation: null,
    text,
  };
}

// `data.ts` owns the graph-source status vocabulary so that visibility and cancellation
// can reason about the modal without depending on its request and payload types, while
// `source-inspection.ts` owns the full discriminated union built on those statuses. That
// split is deliberate, and it means the two declarations can drift: the vocabulary could
// gain a status the union never declares, or the union could gain one `graphSourceIsOpen`
// has never heard of.
//
// The first version of this gate read the union out of the source with a regular
// expression and compared the two lists. Adversarial review defeated it in one move: a
// status added to the vocabulary, plus a comment placed where the regex stopped
// capturing, and the gate passed while the two lists disagreed. Any gate that parses its
// own source text has that failure mode, because the thing it actually checks is the
// formatting.
//
// So let the compiler answer instead. Assigning each type to the other in turn is exactly
// the claim "these two describe the same set of statuses", and it is not sensitive to
// layout, comments, or declaration order.

// `[A] extends [B]` rather than `A extends B`: the bare form distributes over a union and
// collapses a partial match to `boolean`, which would accept `true` and make the gate
// vacuous. The tuple wrapper compares the sets whole, so a mismatch is `false` and
// assigning `true` to it is a compile error.
type Covers<A, B> = [A] extends [B] ? true : false;

// Every status the union declares is in the vocabulary. Adding a variant to
// `GraphSourceState` without adding its status to `graphSourceStatuses` fails here.
const unionStatusesAreInVocabulary: Covers<GraphSourceState["status"], GraphSourceStatus> =
  true;

// And every status in the vocabulary is one the union declares, so a status added to
// `graphSourceStatuses` alone -- which is what review used to defeat the regex -- fails
// here instead.
const vocabularyStatusesAreInUnion: Covers<GraphSourceStatus, GraphSourceState["status"]> =
  true;

test("the graph source union and its vocabulary describe the same statuses", () => {
  // The two declarations above are the gate and they run at compile time; `npm test`
  // typechecks this project before executing it. This case exists so the file is not
  // silently dropped from the suite, and so the vocabulary is also non-empty at runtime
  // -- a compiler asked to compare two empty sets will happily agree.
  assert.equal(unionStatusesAreInVocabulary, true);
  assert.equal(vocabularyStatusesAreInUnion, true);
  assert.ok(graphSourceStatuses.length > 0, "graph source statuses");
  assert.ok(
    graphSourceStatuses.includes("closed"),
    "a closed status the modal can return to");
});

test("the graph modal is open for every status except closed", () => {
  const decisions = {
    closed: false,
    loading: true,
    ready: true,
    failed: true,
    cancelled: true,
  } satisfies Record<GraphSourceStatus, boolean>;

  assert.equal(graphSourceIsOpen(null), false);
  assert.equal(graphSourceIsOpen(undefined), false);
  for (const status of graphSourceStatuses) {
    assert.equal(
      graphSourceIsOpen({ status }),
      decisions[status],
      `graphSourceIsOpen handles ${status}`);
  }
});

// The auto-load decision, tested by outcome for every variant of the union.
//
// Round 2 review (GPT-5.6 Sol) defeated the source-text gate this replaces. That gate
// isolated the auto-load branch and collected `state.graphSource.status === "..."`
// comparisons, requiring the set to be exactly `["cancelled"]`. Writing the widening
// Yoda-style -- `"failed" === state.graphSource.status` -- added `failed` to the branch
// without adding a match, and the whole suite stayed green while a settled failure
// rendered, was treated as reloadable, and started another request forever.
//
// A regex over one comparison spelling can only ever pin that spelling. Asking the
// decision itself removes the spelling from the question.
test("the graph modal auto-loads exactly the state that has no result coming", () => {
  const request = {
    packageId: "Contoso.Widgets",
    version: "1.0.0",
    framework: "net10.0",
    assembly: "Contoso.Widgets.dll",
    type: "Contoso.Widgets.Widget",
    member: "Build",
    selectorKey: "method:Build",
    metadataToken: 100663297,
  };
  const title = "Widget.Build";

  // One representative state per status, and the answer expected for it. `satisfies`
  // rather than a type annotation so the members stay literal and a status that is not in
  // the union is a compile error here, not a silently accepted extra row.
  const decisions = {
    closed: [{ status: "closed" }, false],
    loading: [{ status: "loading", request, title }, false],
    ready: [
      { status: "ready", request, title, source: source("class Widget {}") },
      false,
    ],
    failed: [{ status: "failed", request, title, error: "engine failed" }, false],
    cancelled: [{ status: "cancelled", request, title }, true],
  } satisfies Record<GraphSourceStatus, readonly [GraphSourceState, boolean]>;

  // `satisfies Record<GraphSourceStatus, ...>` already makes a missing status a compile
  // error, but only while every key is spelled statically. Assert the coverage at runtime
  // too, so the table cannot be rebuilt dynamically and quietly shrink.
  assert.deepEqual(
    Object.keys(decisions).sort(),
    [...graphSourceStatuses].sort(),
    "auto-load decisions cover the graph-source vocabulary",
  );

  for (const [status, [state, expected]] of Object.entries(decisions)) {
    const reload = graphSourceAutoLoad(state);
    assert.equal(
      reload !== null,
      expected,
      `graphSourceAutoLoad reloads ${status}`,
    );
    if (reload) {
      // The reload has to carry the request the state belongs to. Returning the work
      // rather than a boolean is what keeps the caller from picking its own.
      assert.equal(reload.request, request, `${status} reloads its own request`);
      assert.equal(reload.title, title, `${status} reloads its own title`);
    }
  }

  // Non-vacuity: the table above is only evidence if at least one status answers each
  // way. A table that said `false` everywhere would pass every assertion in the loop.
  const answers = Object.values(decisions).map(([, expected]) => expected);
  assert.ok(answers.includes(true), "some status auto-loads");
  assert.ok(answers.includes(false), "some status does not auto-load");
});
