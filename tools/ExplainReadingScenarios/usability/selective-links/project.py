"""Trial-only link selection over pinned HAL readings; never edits domain state."""
import json


def resources(value, inherited_complete=False):
    """Only resource nodes, including selected embedded resources."""
    if not isinstance(value, dict) or '_links' not in value:
        return
    complete = value.get('data_scope', {}).get('completeness')
    complete = inherited_complete if complete is None else complete == 'Complete'
    yield value, complete
    for population in value.get('_embedded', {}).values():
        for child in population if isinstance(population, list) else [population]:
            yield from resources(child, complete)


def fully_present(resource, complete):
    if not complete:
        return False
    # Selected bindings expose only selected facets, even in a complete dataset.
    # Their total inventory count is retained; preserve access to omitted facets.
    if resource.get('kind') == 'consumer-binding':
        return str(len(resource.get('exposed_facets', []))) == resource.get('members')
    return resource.get('kind') in ('value-vocabulary', 'vocabulary-value',
                                   'query-space', 'query-facet')


def project(data):
    try:
        root = json.loads(data)
    except (ValueError, UnicodeDecodeError):
        return data, []
    if not isinstance(root, dict) or 'kind' not in root or '_links' not in root:
        return data, []
    nodes = list(resources(root))
    complete_hrefs = {node['_links'].get('self', {}).get('href')
                      for node, complete in nodes if fully_present(node, complete)} - {None}
    replacements = {}
    removed = []
    for node, _ in nodes:
        original = node['_links']
        selected = {}
        for relation, links in original.items():
            if relation == 'self':
                selected[relation] = links
                continue
            population = links if isinstance(links, list) else [links]
            kept = []
            for link in population:
                reason = ('contract' if relation == 'describedby' else
                          'complete-local-target' if link.get('href') in complete_hrefs else None)
                if reason:
                    removed.append({'source': original.get('self', {}).get('href'),
                                    'relation': relation, 'href': link.get('href'), 'reason': reason})
                else:
                    kept.append(link)
            if kept:
                selected[relation] = kept if isinstance(links, list) else kept[0]
        if selected != original:
            replacements[json.dumps(original, sort_keys=True)] = selected
    if not replacements:
        return data, removed
    # Locate JSON member values using the standard decoder. Replace only _links
    # spans; all bytes outside those spans, including domain state, stay exact.
    text = data.decode('utf-8')
    decoder = json.JSONDecoder()
    spans = []

    def skip(index):
        while index < len(text) and text[index].isspace():
            index += 1
        return index

    def scan(index):
        index = skip(index)
        if text[index] == '{':
            index = skip(index + 1)
            while text[index] != '}':
                key, end = decoder.raw_decode(text, index)
                start = skip(skip(end) + 1)  # colon
                end = scan(start)
                if key == '_links':
                    links = json.loads(text[start:end])
                    replacement = replacements.get(json.dumps(links, sort_keys=True))
                    if replacement is not None:
                        encoded = json.dumps(replacement, ensure_ascii=True, separators=(',', ':'))
                        spans.append((start, end, encoded))
                index = skip(end)
                if text[index] == ',':
                    index = skip(index + 1)
            return index + 1
        if text[index] == '[':
            index = skip(index + 1)
            while text[index] != ']':
                index = skip(scan(index))
                if text[index] == ',':
                    index = skip(index + 1)
            return index + 1
        return decoder.raw_decode(text, index)[1]

    scan(0)
    for start, end, replacement in reversed(spans):
        text = text[:start] + replacement + text[end:]
    return text.encode('utf-8'), removed
