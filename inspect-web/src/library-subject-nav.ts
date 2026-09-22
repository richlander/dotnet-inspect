import { renderContentNavigationCloseButton } from "./content-frame.ts";

export interface LibrarySubjectNavItem {
  id: string;
  name: string;
  asset: string;
  types: number;
  members: number;
}

export interface LibrarySubjectNavOptions {
  libraries: readonly LibrarySubjectNavItem[];
  selectedLibraryId: string | null;
  escapeHtml: (value: unknown) => string;
}

export interface LibrarySubjectNavActions {
  onSelect: (libraryId: string | null) => void;
}

const aggregateValue = "all";

type LibrarySubjectIdentity = Pick<
  LibrarySubjectNavItem,
  "id" | "name" | "asset"
>;

export function alphabetizeLibrarySubjects<T extends LibrarySubjectIdentity>(
  libraries: readonly T[],
): T[] {
  return [...libraries].sort((left, right) =>
    left.name.localeCompare(
      right.name,
      undefined,
      { sensitivity: "base" },
    )
    || left.name.localeCompare(right.name)
    || left.asset.localeCompare(right.asset)
    || left.id.localeCompare(right.id));
}

export function preferredLibrarySubjectId(
  libraries: readonly LibrarySubjectIdentity[],
  packageId: string,
): string | null {
  const ordered = alphabetizeLibrarySubjects(libraries);
  const normalizedPackageId = packageId.toLocaleLowerCase();
  return ordered.find(library =>
    library.name.toLocaleLowerCase() === normalizedPackageId)?.id
    ?? ordered[0]?.id
    ?? null;
}

export function librarySubjectDisplayLabels(
  libraries: readonly LibrarySubjectNavItem[],
): ReadonlyMap<string, string> {
  const nameCounts = new Map<string, number>();
  for (const library of libraries) {
    const key = library.name.toLocaleLowerCase();
    nameCounts.set(key, (nameCounts.get(key) ?? 0) + 1);
  }
  return new Map(libraries.map(library => [
    library.id,
    nameCounts.get(library.name.toLocaleLowerCase())! > 1
      ? `${library.name} · ${library.asset}`
      : library.name,
  ]));
}

export function renderLibrarySubjectNav(
  options: LibrarySubjectNavOptions,
): string {
  const { libraries, selectedLibraryId, escapeHtml } = options;
  const displayLabels = librarySubjectDisplayLabels(libraries);
  const subjects = [
    {
      value: aggregateValue,
      name: "All libraries",
      detail: `${libraries.length} admitted`,
      selected: selectedLibraryId === null,
    },
    ...libraries.map(library => ({
      value: library.id,
      name: displayLabels.get(library.id) ?? library.name,
      detail: `${library.types} type${library.types === 1 ? "" : "s"} · ${library.members.toLocaleString()} members`,
      selected: library.id === selectedLibraryId,
    })),
  ];
  const selectedIndex = Math.max(
    subjects.findIndex(subject => subject.selected),
    0);

  return `
    <aside id="content-navigation-pane" class="type-browser library-subject-nav" aria-label="Libraries">
      <div class="browser-head">
        <div>
          <span class="pane-label">LIBRARIES</span>
          <span class="result-count">${subjects.length}</span>
        </div>
        ${renderContentNavigationCloseButton()}
      </div>
      <div class="type-list library-subject-list" role="listbox" aria-label="Libraries" tabindex="0" aria-activedescendant="library-subject-option-${selectedIndex}" data-nav-scope="libraries" data-nav-selection="library:${escapeHtml(subjects[selectedIndex]?.value ?? aggregateValue)}">
        ${subjects.map((subject, index) =>
          `<div id="library-subject-option-${index}" class="type-row library-subject-row${subject.selected ? " selected active" : ""}" role="option" aria-selected="${subject.selected}" data-library-subject="${escapeHtml(subject.value)}" title="${subject.value === aggregateValue ? "Inspect all admitted libraries" : `Inspect ${escapeHtml(subject.name)}`}">
            <span class="kind-icon">${subject.value === aggregateValue ? "◫" : "L"}</span>
            <span class="type-name">${escapeHtml(subject.name)}</span>
            <small>${escapeHtml(subject.detail)}</small>
          </div>`).join("")}
      </div>
      <footer class="pane-footer"><span>choose a Library subject</span><span>↵ open</span></footer>
    </aside>`;
}

export function bindLibrarySubjectNav(
  root: ParentNode,
  actions: LibrarySubjectNavActions,
): void {
  const list = root.querySelector<HTMLElement>(".library-subject-list");
  if (!list) return;
  const rows = [
    ...list.querySelectorAll<HTMLElement>("[data-library-subject]"),
  ];
  if (!rows.length) return;
  const selectedIndex = Math.max(
    rows.findIndex(row => row.getAttribute("aria-selected") === "true"),
    0);
  let activeIndex = selectedIndex;
  let typeahead = "";
  let typeaheadTimer: number | null = null;

  const setActive = (index: number) => {
    activeIndex = Math.max(0, Math.min(rows.length - 1, index));
    rows.forEach((row, rowIndex) =>
      row.classList.toggle("active", rowIndex === activeIndex));
    const active = rows[activeIndex];
    if (!active) return;
    list.setAttribute("aria-activedescendant", active.id);
    active.scrollIntoView({ block: "nearest" });
  };
  const resetActive = () => setActive(selectedIndex);
  const commit = (row: HTMLElement | undefined) => {
    if (!row || row.getAttribute("aria-selected") === "true") return;
    const value = row.dataset.librarySubject;
    if (!value) return;
    actions.onSelect(value === aggregateValue ? null : value);
  };

  rows.forEach((row, index) => {
    row.onclick = () => {
      setActive(index);
      commit(row);
    };
  });
  list.onblur = resetActive;
  list.onkeydown = event => {
    const key = event.key;
    if (key === "ArrowDown" || key === "ArrowUp"
      || key === "Home" || key === "End") {
      event.preventDefault();
      event.stopPropagation();
      if (key === "Home") setActive(0);
      else if (key === "End") setActive(rows.length - 1);
      else setActive(activeIndex + (key === "ArrowDown" ? 1 : -1));
      return;
    }
    if (key === "Enter") {
      event.preventDefault();
      event.stopPropagation();
      commit(rows[activeIndex]);
      return;
    }
    if (key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      resetActive();
      return;
    }
    if (key.length !== 1 || event.metaKey || event.ctrlKey || event.altKey)
      return;
    event.preventDefault();
    event.stopPropagation();
    typeahead += key.toLocaleLowerCase();
    if (typeaheadTimer !== null) window.clearTimeout(typeaheadTimer);
    typeaheadTimer = window.setTimeout(() => {
      typeahead = "";
      typeaheadTimer = null;
    }, 700);
    const start = (activeIndex + 1) % rows.length;
    for (let offset = 0; offset < rows.length; offset++) {
      const index = (start + offset) % rows.length;
      const label = rows[index]
        ?.querySelector<HTMLElement>(".type-name")
        ?.textContent
        ?.trim()
        .toLocaleLowerCase();
      if (label?.startsWith(typeahead)) {
        setActive(index);
        break;
      }
    }
  };
}
