export type EscapeHtml = (value: unknown) => string;

interface PrismToken {
  type: string;
  alias?: string | readonly string[];
  content: PrismTokenContent;
}

type PrismTokenContent =
  | string
  | PrismToken
  | readonly (string | PrismToken)[];

export interface PrismCSharpTokenizer {
  // Prism's registry is keyed by language name and populated by whichever grammar modules
  // were imported, so it is spelled as the open registry it is rather than as a record
  // with one optional `csharp`. The latter is a weak type: an all-optional shape shares no
  // properties with Prism's own index-signature type and will not accept it.
  languages: { readonly [language: string]: unknown };
  tokenize(value: string, grammar: unknown): readonly (string | PrismToken)[];
}

export interface PrismCSharpHighlighter extends PrismCSharpTokenizer {
  highlight(value: string, grammar: unknown, language: string): string;
}

export interface CSharpRangeHighlighter {
  render(start: number, length: number): string;
}

export interface CSharpHighlightExclusion {
  start: number;
  length: number;
}

interface HighlightRun {
  start: number;
  end: number;
  classes: readonly string[];
}

interface NormalizedExclusion {
  start: number;
  end: number;
}

export function createCSharpRangeHighlighter(
  source: string,
  prism: PrismCSharpTokenizer | undefined,
  escapeHtml: EscapeHtml,
  tokenizationSource: string = source,
  excludedRanges: readonly CSharpHighlightExclusion[] = [],
): CSharpRangeHighlighter {
  if (tokenizationSource.length !== source.length) {
    return plainHighlighter(source, escapeHtml);
  }
  const exclusions = normalizeExclusions(source, excludedRanges);
  if (!exclusions) return plainHighlighter(source, escapeHtml);
  const grammar = prism?.languages.csharp;
  if (!prism || !grammar) return plainHighlighter(source, escapeHtml);

  const runs: HighlightRun[] = [];
  let tokenizedText = "";
  let offset = 0;
  const append = (
    content: PrismTokenContent,
    classes: readonly string[] = [],
  ): void => {
    if (typeof content === "string") {
      if (content.length > 0) {
        tokenizedText += content;
        runs.push({
          start: offset,
          end: offset + content.length,
          classes,
        });
        offset += content.length;
      }
      return;
    }
    if (isTokenList(content)) {
      for (const item of content) append(item, classes);
      return;
    }

    const token = content;
    append(token.content, [
      ...classes,
      token.type,
      ...normalizeAliases(token.alias),
    ]);
  };

  append(prism.tokenize(tokenizationSource, grammar));
  if (tokenizedText !== tokenizationSource
    || offset !== tokenizationSource.length
    || runs.some(run => run.end > tokenizationSource.length)) {
    return plainHighlighter(source, escapeHtml);
  }

  return {
    render(start, length) {
      const end = checkedRangeEnd(source, start, length);
      let html = "";
      for (const run of runs) {
        if (run.end <= start) continue;
        if (run.start >= end) break;
        const sliceStart = Math.max(start, run.start);
        const sliceEnd = Math.min(end, run.end);
        const classes = uniqueCssClasses(run.classes);
        html += renderRun(
          source,
          sliceStart,
          sliceEnd,
          classes,
          exclusions,
          escapeHtml);
      }
      return html;
    },
  };
}

function renderRun(
  source: string,
  start: number,
  end: number,
  classes: readonly string[],
  exclusions: readonly NormalizedExclusion[],
  escapeHtml: EscapeHtml,
): string {
  let html = "";
  let cursor = start;
  for (const exclusion of exclusions) {
    if (exclusion.end <= cursor) continue;
    if (exclusion.start >= end) break;
    const exclusionStart = Math.max(cursor, exclusion.start);
    if (cursor < exclusionStart) {
      html += renderStyledText(
        source.slice(cursor, exclusionStart),
        classes,
        escapeHtml);
    }
    const exclusionEnd = Math.min(end, exclusion.end);
    html += escapeHtml(source.slice(exclusionStart, exclusionEnd));
    cursor = exclusionEnd;
    if (cursor >= end) return html;
  }
  return html + renderStyledText(source.slice(cursor, end), classes, escapeHtml);
}

function renderStyledText(
  source: string,
  classes: readonly string[],
  escapeHtml: EscapeHtml,
): string {
  const text = escapeHtml(source);
  return classes.length > 0
    ? `<span class="token ${classes.join(" ")}">${text}</span>`
    : text;
}

function normalizeExclusions(
  source: string,
  ranges: readonly CSharpHighlightExclusion[],
): readonly NormalizedExclusion[] | null {
  const sorted: NormalizedExclusion[] = [];
  for (const range of ranges) {
    if (!Number.isInteger(range.start)
      || !Number.isInteger(range.length)
      || range.start < 0
      || range.length <= 0
      || range.start + range.length > source.length) {
      return null;
    }
    sorted.push({
      start: range.start,
      end: range.start + range.length,
    });
  }
  sorted.sort((left, right) => left.start - right.start || left.end - right.end);

  const normalized: NormalizedExclusion[] = [];
  for (const range of sorted) {
    const previous = normalized.at(-1);
    if (previous && range.start <= previous.end) {
      previous.end = Math.max(previous.end, range.end);
    } else {
      normalized.push({ ...range });
    }
  }
  return normalized;
}

function plainHighlighter(
  source: string,
  escapeHtml: EscapeHtml,
): CSharpRangeHighlighter {
  return {
    render(start, length) {
      const end = checkedRangeEnd(source, start, length);
      return escapeHtml(source.slice(start, end));
    },
  };
}

function checkedRangeEnd(source: string, start: number, length: number): number {
  if (!Number.isInteger(start)
    || !Number.isInteger(length)
    || start < 0
    || length < 0
    || start + length > source.length) {
    throw new RangeError(
      `C# highlight range ${start}..${start + length} is outside source length ${source.length}.`);
  }
  return start + length;
}

function normalizeAliases(
  alias: PrismToken["alias"],
): readonly string[] {
  if (typeof alias === "string") return [alias];
  return alias ?? [];
}

function isTokenList(
  content: PrismTokenContent,
): content is readonly (string | PrismToken)[] {
  return Array.isArray(content);
}

function uniqueCssClasses(classes: readonly string[]): string[] {
  return [...new Set(classes.filter(value => /^[A-Za-z0-9_-]+$/.test(value)))];
}
