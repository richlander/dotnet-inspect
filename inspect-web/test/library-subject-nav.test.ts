import assert from "node:assert/strict";
import test from "node:test";
import {
  alphabetizeLibrarySubjects,
  bindLibrarySubjectNav,
  librarySubjectDisplayLabels,
  preferredLibrarySubjectId,
  renderLibrarySubjectNav,
} from "../src/library-subject-nav.ts";
import { fakeDom } from "./fake-dom.ts";

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

class FakeClassList {
  readonly values = new Set<string>();

  toggle(value: string, force: boolean) {
    if (force) this.values.add(value);
    else this.values.delete(value);
  }
}

class FakeRow {
  readonly id: string;
  readonly dataset: { librarySubject: string };
  readonly classList = new FakeClassList();
  onclick: (() => void) | null = null;
  private readonly selected: boolean;

  constructor(id: string, value: string, selected: boolean) {
    this.id = id;
    this.selected = selected;
    this.dataset = { librarySubject: value };
  }

  getAttribute(name: string) {
    return name === "aria-selected" ? String(this.selected) : null;
  }

  scrollIntoView() {}
}

class FakeList {
  readonly attributes = new Map<string, string>();
  onblur: (() => void) | null = null;
  onkeydown: ((event: KeyboardEvent) => void) | null = null;
  private readonly rows: readonly FakeRow[];

  constructor(rows: readonly FakeRow[]) {
    this.rows = rows;
  }

  querySelectorAll() {
    return this.rows;
  }

  setAttribute(name: string, value: string) {
    this.attributes.set(name, value);
  }
}

class FakeRoot {
  private readonly list: FakeList;

  constructor(list: FakeList) {
    this.list = list;
  }

  querySelector(selector: string) {
    return selector === ".library-subject-list" ? this.list : null;
  }
}

function keyEvent(key: string) {
  let prevented = false;
  let stopped = false;
  return {
    event: fakeDom.keyboardEvent({
      key,
      metaKey: false,
      ctrlKey: false,
      altKey: false,
      preventDefault: () => {
        prevented = true;
      },
      stopPropagation: () => {
        stopped = true;
      },
    }),
    prevented: () => prevented,
    stopped: () => stopped,
  };
}

test("Library navigation renders the aggregate first and keeps empty Libraries", () => {
  const html = renderLibrarySubjectNav({
    libraries: [
      {
        id: "Example.Core",
        name: "Example.Core",
        asset: "lib/net10.0/Example.Core.dll",
        types: 3,
        members: 12,
      },
      {
        id: "Example.Empty",
        name: "Example.Empty",
        asset: "lib/net10.0/Example.Empty.dll",
        types: 0,
        members: 0,
      },
    ],
    selectedLibraryId: null,
    escapeHtml,
  });

  assert.match(html,
    /aria-selected="true" data-library-subject="all"[\s\S]*All libraries/);
  assert.match(html,
    /data-library-subject="Example\.Core"[\s\S]*3 types · 12 members/);
  assert.match(html,
    /data-library-subject="Example\.Empty"[\s\S]*0 types · 0 members/);
  assert.match(html, /role="listbox"[\s\S]*aria-activedescendant=/);
});

test("Library subjects sort alphabetically and prefer a case-insensitive namesake", () => {
  const libraries = [
    {
      id: "asset:zulu",
      name: "Example.Zulu",
      asset: "lib/net10.0/Example.Zulu.dll",
    },
    {
      id: "asset:namesake",
      name: "example.package",
      asset: "lib/net10.0/example.package.dll",
    },
    {
      id: "asset:alpha",
      name: "Example.Alpha",
      asset: "lib/net10.0/Example.Alpha.dll",
    },
  ];

  assert.deepEqual(
    alphabetizeLibrarySubjects(libraries).map(library => library.id),
    ["asset:alpha", "asset:namesake", "asset:zulu"]);
  assert.equal(
    preferredLibrarySubjectId(libraries, "Example.Package"),
    "asset:namesake");
  assert.equal(
    preferredLibrarySubjectId(libraries, "Missing.Package"),
    "asset:alpha");
  assert.equal(preferredLibrarySubjectId([], "Example.Package"), null);
});

test("Library navigation qualifies duplicate names with product-owned assets", () => {
  const libraries = [
    {
      id: "asset:left",
      name: "Example.Shared",
      asset: "lib/net10.0/left/Example.Shared.dll",
      types: 1,
      members: 1,
    },
    {
      id: "asset:right",
      name: "Example.Shared",
      asset: "lib/net10.0/right/Example.Shared.dll",
      types: 1,
      members: 1,
    },
    {
      id: "asset:unique",
      name: "Example.Unique",
      asset: "lib/net10.0/Example.Unique.dll",
      types: 1,
      members: 1,
    },
  ];

  assert.deepEqual(
    [...librarySubjectDisplayLabels(libraries)],
    [
      [
        "asset:left",
        "Example.Shared · lib/net10.0/left/Example.Shared.dll",
      ],
      [
        "asset:right",
        "Example.Shared · lib/net10.0/right/Example.Shared.dll",
      ],
      ["asset:unique", "Example.Unique"],
    ]);

  const html = renderLibrarySubjectNav({
    libraries,
    selectedLibraryId: null,
    escapeHtml,
  });
  assert.match(html, /Example\.Shared · lib\/net10\.0\/left\/Example\.Shared\.dll/);
  assert.match(html, /Example\.Shared · lib\/net10\.0\/right\/Example\.Shared\.dll/);
  assert.match(html, />Example\.Unique<\/span>/);
});

test("Library navigation filters exact IDs and retains the current subject", () => {
  const libraries = [
    {
      id: "asset:left",
      name: "Example.Shared",
      asset: "lib/net10.0/left/Example.Shared.dll",
      types: 1,
      members: 2,
    },
    {
      id: "asset:right",
      name: "Example.Shared",
      asset: "lib/net10.0/right/Example.Shared.dll",
      types: 3,
      members: 4,
    },
    {
      id: "asset:other",
      name: "Example.Other",
      asset: "lib/net10.0/Example.Other.dll",
      types: 5,
      members: 6,
    },
  ];

  const html = renderLibrarySubjectNav({
    libraries,
    selectedLibraryId: "asset:right",
    matchingLibraryIds: new Set(["asset:left"]),
    queryControlsHtml: '<form data-library-query-form></form>',
    queryStatusHtml: '<p class="library-query-status">1 match</p>',
    escapeHtml,
  });

  assert.match(html, /All libraries/);
  assert.match(
    html,
    /data-library-subject="asset:left"[\s\S]*Example\.Shared · lib\/net10\.0\/left\/Example\.Shared\.dll/);
  assert.match(
    html,
    /class="type-row library-subject-row selected active retained-current"[\s\S]*data-library-subject="asset:right"[\s\S]*current selection/);
  assert.doesNotMatch(html, /data-library-subject="asset:other"/);
  assert.ok(
    html.indexOf("data-library-query-form")
      < html.indexOf('role="listbox"'));
});

test("Library navigation distinguishes no settled query from zero matches", () => {
  const libraries = [{
    id: "asset:only",
    name: "Example.Only",
    asset: "lib/net10.0/Example.Only.dll",
    types: 1,
    members: 1,
  }];

  const unfiltered = renderLibrarySubjectNav({
    libraries,
    selectedLibraryId: null,
    escapeHtml,
  });
  const noMatches = renderLibrarySubjectNav({
    libraries,
    selectedLibraryId: null,
    matchingLibraryIds: new Set(),
    escapeHtml,
  });

  assert.match(unfiltered, /data-library-subject="asset:only"/);
  assert.doesNotMatch(noMatches, /data-library-subject="asset:only"/);
  assert.match(noMatches, /data-library-subject="all"/);
});

test("Library navigation moves locally and commits only on Enter", () => {
  const rows = [
    new FakeRow("library-subject-option-0", "all", true),
    new FakeRow("library-subject-option-1", "Example.Core", false),
    new FakeRow("library-subject-option-2", "Example.Empty", false),
  ];
  const list = new FakeList(rows);
  const selections: Array<string | null> = [];

  bindLibrarySubjectNav(
    fakeDom.parentNode(new FakeRoot(list)),
    { onSelect: value => selections.push(value) });

  const down = keyEvent("ArrowDown");
  list.onkeydown?.(down.event);
  assert.equal(list.attributes.get("aria-activedescendant"),
    "library-subject-option-1");
  assert.deepEqual(selections, []);
  assert.equal(down.prevented(), true);
  assert.equal(down.stopped(), true);

  const enter = keyEvent("Enter");
  list.onkeydown?.(enter.event);
  assert.deepEqual(selections, ["Example.Core"]);

  list.onblur?.();
  assert.equal(list.attributes.get("aria-activedescendant"),
    "library-subject-option-0");
});
