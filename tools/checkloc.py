# Checks that every Loc.T key used in the code exists in en/es/fr, with the same placeholders, and that none is unused.
# Usage: python tools/checkloc.py
import json, re, glob, os

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # the repository root
files = [f for f in glob.glob(root + r'\**\*.cs', recursive=True) if (os.sep + 'obj' + os.sep) not in f and (os.sep + 'bin' + os.sep) not in f]
used = {}
literals = set()
for f in files:
    text = open(f, encoding='utf-8-sig').read()
    for m in re.finditer(r'Loc\.T\(\s*"([^"]+)"', text):
        used.setdefault(m.group(1), set()).add(os.path.basename(f))
    literals |= set(re.findall(r'"([A-Za-z]+(?:\.[A-Za-z]+)+)"', text))

langs = {code: json.load(open(os.path.join(root, 'Languages', code + '.json'), encoding='utf-8')) for code in ('en', 'es', 'fr')}
ok = True
prefixes = [k for k in used if k.endswith('.')]
used = {k: v for k, v in used.items() if not k.endswith('.')}
for key, where in sorted(used.items()):
    for code, d in langs.items():
        if key not in d:
            print('MISSING in', code, ':', key, sorted(where)); ok = False
for code, d in langs.items():
    for key in d:
        if key not in used and key not in literals and key != 'language.name' and not any(key.startswith(p) for p in prefixes):
            print('UNUSED in', code, ':', key); ok = False
en = langs['en']
for code in ('es', 'fr'):
    for key, text in langs[code].items():
        if key in en:
            a = sorted(set(re.findall(r'\{(\d+)(?::[^}]*)?\}', en[key])))
            b = sorted(set(re.findall(r'\{(\d+)(?::[^}]*)?\}', text)))
            if a != b:
                print('PLACEHOLDERS differ in', code, key, a, b); ok = False
    for key in en:
        if key not in langs[code]:
            print('NOT TRANSLATED in', code, ':', key); ok = False
print('keys used:', len(used), '| en:', len(en), 'es:', len(langs['es']), 'fr:', len(langs['fr']), '| OK' if ok else '| PROBLEMS')
