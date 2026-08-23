import assert from "node:assert/strict";
import test from "node:test";

import { graphSourceStatuses, type GraphSourceStatus } from "../src/data.ts";
import type { GraphSourceState } from "../src/source-inspection.ts";

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
