#!/usr/bin/env python3
"""Check local Markdown destinations and GitHub-style heading anchors without network access."""
import argparse
from collections import Counter
import html
from pathlib import Path
import re
import unicodedata
from urllib.parse import unquote, urlsplit

IGNORED = {'.git', 'bin', 'obj', 'artifacts', '__pycache__', 'node_modules', '.idea', '.venv'}


def prose(text):
    fenced = None
    for number, line in enumerate(text.splitlines(), 1):
        match = re.match(r'^\s{0,3}(`{3,}|~{3,})', line)
        if match:
            marker = match[1][0]
            if fenced is None:
                fenced = marker
            elif fenced == marker:
                fenced = None
            continue
        if fenced is None and not line.startswith(('    ', '\t')):
            yield number, line


def heading_slug(text):
    text = re.sub(r'<[^>]*>', '', html.unescape(text))
    text = re.sub(r'\[([^]]+)\]\([^)]*\)', r'\1', text).replace('`', '')
    return ''.join(c for c in text.lower() if c in '-_' or c.isspace()
                   or unicodedata.category(c)[0] in 'LNM').replace(' ', '-')


def anchors(text):
    result, counts, previous = set(), Counter(), ''
    for _, line in prose(text):
        result.update(re.findall(r'<[^>]+\b(?:id|name)=[\"\']([^\"\']+)[\"\']', line))
        match = re.match(r'^\s{0,3}#{1,6}\s+(.+?)(?:\s+#+\s*)?$', line)
        title = match[1] if match else previous if re.match(r'^\s{0,3}(?:=+|-+)\s*$', line) and previous else None
        if title:
            slug = heading_slug(title)
            duplicate = counts[slug]
            result.add(slug if not duplicate else f'{slug}-{duplicate}')
            counts[slug] += 1
        previous = line.strip()
    return result


def check(root):
    root = Path(root).resolve()
    failures, cache = [], {}
    for document in sorted(root.rglob('*.md')):
        if any(part in IGNORED for part in document.relative_to(root).parts):
            continue
        for number, line in prose(document.read_text(encoding='utf-8')):
            links = re.findall(r'!?\[[^]]*\]\(\s*(<[^>]+>|[^\s)]+)', line)
            definition = re.match(r'^\s{0,3}\[[^]]+\]:\s*(\S+)', line)
            if definition:
                links.append(definition[1])
            for link in links:
                link = html.unescape(link.strip('<>'))
                parsed = urlsplit(link)
                if parsed.scheme or parsed.netloc:
                    continue
                path = unquote(parsed.path)
                target = (document.parent / path).resolve() if path else document
                prefix = f'{document.relative_to(root)}:{number}: {link}'
                if not target.is_relative_to(root):
                    failures.append(prefix + ' leaves the package')
                elif not target.exists():
                    failures.append(prefix + ' missing destination')
                elif parsed.fragment and target.suffix.lower() == '.md':
                    if target not in cache:
                        cache[target] = anchors(target.read_text(encoding='utf-8'))
                    if unquote(parsed.fragment) not in cache[target]:
                        failures.append(prefix + ' missing anchor')
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    failures = check(args.root)
    for failure in failures:
        print(failure)
    print(f'Local link check: {len(failures)} failure(s)')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
