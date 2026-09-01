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

interface PrismCSharpTokenizer {
  languages: {
    csharp?: unknown;
  };
  tokenize(value: string, grammar: unknown): readonly (string | PrismToken)[];
}

interface PrismCSharpHighlighter extends PrismCSharpTokenizer {
  highlight(value: string, grammar: unknown, language: string): string;
}

declare global {
  interface Window {
    Prism?: PrismCSharpHighlighter;
  }
}

export interface CSharpRangeHighlighter {
  render(start: number, length: number): string;
}

interface HighlightRun {
  start: number;
  end: number;
  classes: readonly string[];
}

export function createCSharpRangeHighlighter(
  source: string,
  prism: PrismCSharpTokenizer | undefined,
  escapeHtml: EscapeHtml,
  tokenizationSource: string = source,
): CSharpRangeHighlighter {
  if (tokenizationSource.length !== source.length) {
    return plainHighlighter(source, escapeHtml);
  }
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
        const text = escapeHtml(source.slice(sliceStart, sliceEnd));
        const classes = uniqueCssClasses(run.classes);
        html += classes.length > 0
          ? `<span class="token ${classes.join(" ")}">${text}</span>`
          : text;
      }
      return html;
    },
  };
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
