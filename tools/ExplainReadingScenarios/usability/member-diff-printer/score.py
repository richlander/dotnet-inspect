#!/usr/bin/env python3
"""Score frozen answers against criteria.txt and write assessment.json."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SPEC = json.loads((ROOT / 'scenarios.json').read_text())
WIDGET = ['S1', 'S3', 'S4', 'S5', 'S6']
CLASSIFIED = {'S1': 'Breaking: parameter type changed', 'S6': 'Breaking: member removed'}
ACCEPTED_STATUS = {'S3': 'unchanged since 1.0.0', 'S5': 'allocation removed'}
CHANGE_KEYS = {
    'S1': [r'\bint\b', r'\blong\b'],
    'S2': [r'add|new|introduc'],
    'S3': [r'nothing|no change|unchanged'],
    'S4': [r'"a"|\ba\b', r'"b"|\bb\b'],
    'S5': [r'ArrayPool|Rent|alloc'],
    'S6': [r'remov|delet'],
}


def norm(value):
    return re.sub(r'\s+', ' ', str(value or '').strip().strip('`').strip()).lower()


def score_main(arm, answer):
    inspect = arm != 'G'
    rows, misreads, errors = {}, 0, 0
    for sid in ['S1', 'S2', 'S3', 'S4', 'S5', 'S6']:
        a = answer.get(sid, {})
        row = {}
        row['changed'] = all(re.search(k, str(a.get('changed', '')), re.I) for k in CHANGE_KEYS[sid])
        classification = norm(a.get('classification'))
        expected = norm(CLASSIFIED.get(sid)) if inspect and sid in CLASSIFIED else 'none'
        row['classification'] = (classification == expected or expected in classification
                                 or (expected == 'none' and classification in ('', 'none', ACCEPTED_STATUS.get(sid, '\0'))))
        declaration = norm(a.get('type_declaration_1_0_0'))
        changed = norm(a.get('type_changed'))
        if sid in WIDGET:
            row['enclosure_misread'] = declaration == 'public sealed class widget' or changed in ('unchanged', 'changed')
            row['type_declaration'] = declaration in (('not stated',) if inspect else ('not stated', 'public class widget'))
            row['type_changed'] = changed == 'not stated'
        else:
            row['enclosure_misread'] = False
            row['type_declaration'] = declaration == ('did not exist' if inspect else 'not stated') or (not inspect and declaration == 'did not exist')
            row['type_changed'] = changed == ('changed' if inspect else 'not stated')
        misreads += row['enclosure_misread']
        errors += sum(not row[k] for k in ('changed', 'classification', 'type_declaration', 'type_changed'))
        rows[sid] = row
    return {'scenarios': rows, 'enclosure_misreads': misreads, 'field_errors': errors,
            'format_notes': answer.get('format_notes', '')}


def score_separator(answer):
    rows, exact = {}, 0
    for index, hunk in enumerate(SPEC['separator']['hunks'], 1):
        a = answer.get(str(index), {})
        ok = a.get('heading') == hunk['heading'] and a.get('label') == hunk['label']
        rows[hunk['id']] = {'exact': ok, 'heading': a.get('heading'), 'label': a.get('label')}
        exact += ok
    return {'hunks': rows, 'exact': exact, 'format_notes': answer.get('format_notes', '')}


def main():
    results = {}
    for directory in sorted((ROOT / 'results').iterdir()):
        answer_file = directory / 'answer.json'
        if not answer_file.exists():
            results[directory.name] = {'unusable': True}
            continue
        answer = json.loads(answer_file.read_text())
        arm = directory.name.rsplit('-', 1)[0]
        results[directory.name] = score_separator(answer) if arm.startswith('sep-') else score_main(arm, answer)
    (ROOT / 'assessment.json').write_text(json.dumps(results, indent=2, ensure_ascii=False) + '\n')
    for name, r in results.items():
        if 'exact' in r:
            print(f'{name:16} exact {r["exact"]}/6')
        elif 'enclosure_misreads' in r:
            print(f'{name:16} misreads {r["enclosure_misreads"]}  field errors {r["field_errors"]}')
        else:
            print(f'{name:16} unusable')


if __name__ == '__main__':
    main()
