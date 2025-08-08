#!/usr/bin/env python3
"""Scan .resx files for XML correctness and attempt repairs.

If a .resx file contains invalid XML (e.g., unclosed tags or is
truncated) the script will try to automatically close any open tags.
If the file still cannot be parsed as valid XML, it is deleted and a
backup with the suffix `.bak` is left in its place.

Usage: run this script from the root of the project.
"""

import os
import re
import shutil
import xml.etree.ElementTree as ET
from typing import Dict, List

TAG_RE = re.compile(r'<(/?)([a-zA-Z0-9_:\-\.]+)([^/>]*)(/?)>')

def _attempt_fix(path: str) -> bool:
    """Try to auto-close open tags and validate the XML.

    Returns True if the file was fixed, False if it should be removed.
    """
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()

    stack: List[str] = []
    for match in TAG_RE.finditer(content):
        closing, tag, _attrs, selfclosing = match.groups()
        tag = tag.strip()
        # Skip declarations, comments, etc.
        if tag.startswith('?') or tag.startswith('!') or tag.startswith('-'):
            continue
        if closing:
            # Pop until the matching tag is found.
            if tag in stack:
                while stack and stack[-1] != tag:
                    stack.pop()
                if stack:
                    stack.pop()
        elif selfclosing:
            continue
        else:
            stack.append(tag)

    if not stack:
        return False  # nothing to fix

    closing_tags = ''.join(f'</{t}>' for t in reversed(stack))
    content += closing_tags
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)

    try:
        ET.parse(path)
        return True
    except ET.ParseError:
        return False

def process_resx(path: str, report: Dict[str, List[str]]) -> None:
    try:
        ET.parse(path)
        return  # already valid
    except ET.ParseError:
        pass

    backup_path = path + '.bak'
    if not os.path.exists(backup_path):
        shutil.copy2(path, backup_path)

    if _attempt_fix(path):
        report['fixed'].append(path)
        return

    os.remove(path)
    report['deleted'].append(path)

def main() -> None:
    report: Dict[str, List[str]] = {"fixed": [], "deleted": []}
    for root, _dirs, files in os.walk('.'):
        for name in files:
            if name.lower().endswith('.resx'):
                process_resx(os.path.join(root, name), report)

    print('Report:')
    if report['fixed']:
        print('  Fixed files:')
        for f in report['fixed']:
            print('   -', f)
    if report['deleted']:
        print('  Deleted files:')
        for f in report['deleted']:
            print('   -', f)
    if not report['fixed'] and not report['deleted']:
        print('  No issues found.')

if __name__ == '__main__':
    main()
