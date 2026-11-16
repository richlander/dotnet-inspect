#!/usr/bin/env python3
"""Render each arm of the Member diff printer trial from scenarios.json."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SPEC = json.loads((ROOT / 'scenarios.json').read_text())
NOTE_COLUMN = 44
LEGEND = "# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared"

INTRO = (
    "Each scenario below is the complete output of one command that compares one "
    "Member of the Example library between version 1.0.0 (baseline) and version "
    "2.0.0 (current).\n"
)


def lead_entries(scenario):
    for entry in scenario['lead']:
        yield (entry[0], entry[1]) if isinstance(entry, list) else (entry, None)


def with_note(text, note):
    if not note:
        return text
    return text.ljust(max(NOTE_COLUMN, len(text) + 4)) + note


def inspect(scenario, enclosure_marker, legend, hunk_header):
    out = []
    if legend:
        out.append(LEGEND)
    for text, note in lead_entries(scenario):
        out.append(with_note(f"{enclosure_marker}  {text}", note))
    if hunk_header and scenario['hunk']:
        out.append(scenario['hunk'])
    for line in scenario['lines']:
        row = f"{line['kind']}  {line['text']}"
        out.append(with_note(row, line.get('note')))
        for caret in line.get('carets', []):
            column = 3 + line['text'].index(caret['span'])
            out.append(' ' * column + '^' * len(caret['span']) + ' ' + caret['text'])
    for text in scenario['trail']:
        out.append(f"{enclosure_marker}  {text}")
    return '\n'.join(line.rstrip() for line in out)


def git(scenario):
    if not scenario['git']:
        return '(no output)'
    return '\n'.join([f"--- Example {SPEC['baseline']}", f"+++ Example {SPEC['current']}", *scenario['git']])


ARMS = {
    'G': git,
    'U': lambda s: inspect(s, ' ', legend=False, hunk_header=False),
    'M': lambda s: inspect(s, '·', legend=False, hunk_header=False),
    'ML': lambda s: inspect(s, '·', legend=True, hunk_header=False),
    'MLH': lambda s: inspect(s, '·', legend=True, hunk_header=True),
}


def escape(text, separator):
    if separator == 'pipe':
        return text.replace('|', '\\|')
    if separator == 'bracket':
        return text.replace('[', '\\[').replace(']', '\\]')
    if separator == 'comment':
        return text.replace('//', '\\/\\/')
    raise ValueError(separator)


def header(hunk, separator):
    parts = [hunk['range']]
    if hunk['heading']:
        parts.append(escape(hunk['heading'], separator))
    if hunk['label']:
        label = escape(hunk['label'], separator)
        parts.append({'pipe': f"| {label}", 'bracket': f"[{label}]", 'comment': f"// {label}"}[separator])
    return ' '.join(parts)


def separator_arm(separator):
    blocks = []
    for index, hunk in enumerate(SPEC['separator']['hunks'], 1):
        blocks.append(
            f"Output {index}:\n```text\n--- Example {SPEC['baseline']}\n+++ Example {SPEC['current']}\n"
            f"{header(hunk, separator)}\n ...\n```")
    return ("Each output below is the beginning of a unified diff that compares one Member of the "
            "Example library between version 1.0.0 and 2.0.0. Only the file headers and the first "
            "hunk header are shown; `...` stands for the hunk's lines.\n\n" + '\n\n'.join(blocks) + '\n')


def main():
    arms = ROOT / 'arms'
    arms.mkdir(exist_ok=True)
    for name, render in ARMS.items():
        blocks = [f"Scenario {s['id']}: {s['member']}\n```text\n{render(s)}\n```" for s in SPEC['scenarios']]
        (arms / f'{name}.txt').write_text(INTRO + '\n' + '\n\n'.join(blocks) + '\n')
    for separator in ('pipe', 'bracket', 'comment'):
        (arms / f'sep-{separator}.txt').write_text(separator_arm(separator))


if __name__ == '__main__':
    main()
