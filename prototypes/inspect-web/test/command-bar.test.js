import assert from "node:assert/strict";
import test from "node:test";
import {
  applyCommandCompletion,
  commandBarHtml,
  commandCompletions,
  commandSuggestionsHtml,
} from "../src/command-bar.ts";

const lenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"],
];

function commandState(command, types = []) {
  return {
    command,
    completionIndex: 0,
    promptOpen: true,
    package: {
      id: "System.Text.Json",
      activeFramework: "net10.0",
      types,
      frameworks: ["net8.0", "net9.0", "net10.0"],
    },
  };
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

test("the empty command bar offers the root command grammar", () => {
  const items = commandCompletions(commandState(""), lenses);

  assert.deepEqual(
    items.map(item => item.value),
    ["type", "types", "show", "framework", "find", "clear", "share"],
  );
  assert.ok(items.every(item => item.kind === "command"));
});

test("type completion filters package types and caps an open argument list", () => {
  const types = Array.from({ length: 10 }, (_, index) => ({
    id: `Example.Type${index}`,
    name: index === 9 ? "JsonSerializer" : `Type${index}`,
    namespace: "Example",
    kind: "class",
  }));

  assert.deepEqual(
    commandCompletions(commandState("type json", types), lenses)
      .map(item => item.value),
    ["JsonSerializer"],
  );
  assert.equal(
    commandCompletions(commandState("type ", types), lenses).length,
    8,
  );
});

test("lens, framework, and type-filter arguments retain their command-specific metadata", () => {
  assert.deepEqual(
    commandCompletions(commandState("show "), lenses),
    [
      { value: "api", hint: "API lens", kind: "lens" },
      { value: "metadata", hint: "Metadata lens", kind: "lens" },
      { value: "source", hint: "Source lens", kind: "lens" },
    ],
  );
  assert.deepEqual(
    commandCompletions(commandState("framework net9"), lenses),
    [{ value: "net9.0", hint: "compile assets", kind: "framework" }],
  );
  assert.deepEqual(
    commandCompletions(commandState("types kind"), lenses),
    [{ value: "kind", hint: "filter by class, struct, interface, or enum", kind: "filter" }],
  );
});

test("completion replaces the active token and preserves the existing append behavior", () => {
  assert.equal(applyCommandCompletion("", "type"), "type ");
  assert.equal(
    applyCommandCompletion("type Json", "JsonSerializer"),
    "type JsonSerializer ",
  );
  assert.equal(
    applyCommandCompletion("  type ", "JsonSerializer"),
    "  type JsonSerializer ",
  );
});

test("suggestion markup escapes values and marks only the selected row", () => {
  const html = commandSuggestionsHtml(
    [
      { value: 'Type"<T>', hint: "A&B", kind: "class" },
      { value: "Other", hint: "Example", kind: "struct" },
    ],
    1,
    escapeHtml,
  );

  assert.match(html, /data-completion="Type&quot;&lt;T&gt;"/);
  assert.match(html, /<span>A&amp;B<\/span>/);
  assert.equal(html.match(/suggestion selected/g)?.length, 1);
  assert.match(html, /suggestion selected[^>]*data-completion="Other"/);
});

test("command bar markup keeps package scope, command text, and open state inert", () => {
  const state = commandState('type Type"<T>');
  state.package.id = "Example&Package";
  const html = commandBarHtml(state, [], escapeHtml);

  assert.match(html, /class="command-panel open"/);
  assert.match(html, /Example&amp;Package:net10\.0/);
  assert.match(html, /value="type Type&quot;&lt;T&gt;"/);
  assert.doesNotMatch(html, /value="type Type"<T>"/);
});
