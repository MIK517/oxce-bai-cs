"""Generates docs/phase-6-refusal-inventory.md: every reason sentence in the strategic code, by class.

A reason sentence is a string literal (plain or interpolated, outside comments) of at least twelve characters that
contains a space and ends with a period, in src/Oxce.Gameplay, src/Oxce.Savegames or src/Oxce.Extensions. Each is put
in one class by the first matching pattern; OVERRIDES holds the hand-reviewed exceptions. Run with --check to verify
that the committed inventory is current.
"""
import io
import os
import re
import sys

ROOTS = ['src/Oxce.Gameplay', 'src/Oxce.Savegames', 'src/Oxce.Extensions']
OUTPUT = 'docs/phase-6-refusal-inventory.md'
LITERAL = re.compile(r'\$?"((?:[^"\\]|\\.){12,}?\.)"')

CLASSES = [
    ('hold', 'Modal holds and the ended campaign (reference behaviour: the windows and the game over stop time and input)',
     r'is open|are open|pending|stop the clock|stops the clock|campaign has ended|until it is|answered'),
    ('invariant', 'Internal consistency checks (a stage, provider, recomputation or registration the code itself guarantees)',
     r'not preflighted|changed after|shadow|more than one|could not compile|disagreed|lost ufos|not reserved|did not stop|'
     r'differs from its recomputation|failed after|lost its preflighted|was not'),
    ('phase7', 'Tactical and battle boundaries (Phase 7)', r'tactical|battle|battlescape'),
    ('bound', 'Port work and range bounds (deterministic limits on forecast work, identities and values the reference leaves unbounded)',
     r'work limit|exceed|supported range|capacity|exhaust|outside the supported|reaches the reference|out of range|outside its range|'
     r'overflow|too many|more soldiers than supported|population limit'),
    ('action', 'Player command refusals the reference also makes (its states disable or refuse the action)',
     r'^required |lift is in use|maximum|insufficient|needs two different|one to four|only to its own base|overlap|conflicts with|'
     r'do not fit|not enough|no free|cannot afford|already|is full|no room|limit reached|in use|place the starting base|is hidden|'
     r'no facility at|does not support autosell|signed an alien pact|requires base abandonment|^no |^cannot |not complete|incomplete|'
     r'was not found|is too low|has expired'),
    ('restriction', 'Named restrictions: strategic transitions and same-tick interactions the port does not run yet (R3-R6 and the other capability boundaries)',
     r'forecast|ambiguous|shares|may reuse|unsupported|not supported|requires? world simulation|simulation is unavailable|latitude domain|'
     r'namespace|alternatives|guaranteed|bounded|compiler-owned|dormant ufo|reference the same|unavailable world transition|repair|require'),
    ('input', 'Invalid content or save input (the reference throws, crashes or misbehaves; ADR 0036 and earlier decision records hold the choices)',
     r'.'),
]

# Hand-reviewed exceptions to the patterns (closure audit, branch 6 S9).
OVERRIDES = {
    'Airborne craft cannot be transferred (canTransferCraftsWhileAirborne is not supported).': 'restriction',
    "Alien base upgrades can collide with another world target's marker identity.": 'restriction',
    'Crashed UFO cannot expire while a mission site references it.': 'restriction',
    'A new campaign requires at least one geographic country.': 'input',
    'Unsupported stock kind.': 'invariant',
    # Restore-only checks of a loaded battle save, wrapped as "Battle save is corrupted: ..." (Phase 7 branch 2 audit).
    'Battle item ID {highest} leaves no ID for the next item.': 'input',
    'Battle item ID {item.Id} is saved twice.': 'input',
    "Battle item {entry.Id} 'ammoItem' names item {legacy}, which the battle does not have.": 'input',
    'Battle item {item.Id} lies at {position}, outside the map.': 'input',
    # The AI's start node is set only on units the generator gives an AI module.
    'Battle unit {Id} has no AI module.': 'invariant',
}


def literals():
    for root in ROOTS:
        for folder, _, files in os.walk(root):
            if os.sep + 'bin' in folder or os.sep + 'obj' in folder or '/bin' in folder or '/obj' in folder:
                continue
            for name in sorted(files):
                if not name.endswith('.cs'):
                    continue
                path = os.path.join(folder, name).replace('\\', '/')
                for number, line in enumerate(io.open(path, encoding='utf-8'), 1):
                    code = line.strip()
                    if code.startswith('//') or code.startswith('*') or code.startswith('[') and 'Obsolete' in code:
                        continue
                    for match in LITERAL.finditer(line.split('//')[0] if '"' not in line.split('//')[0][-1:] else line):
                        text = match.group(1)
                        if ' ' in text and not text.startswith('STR_'):
                            yield text, f'{path}:{number}'


def classify(text):
    if text in OVERRIDES:
        return OVERRIDES[text]
    lowered = text.lower()
    # Corrupted battle saves (Phase 7 branch 2) are input validation, not a battle boundary.
    if lowered.startswith('battle save '):
        return 'input'
    for key, _, pattern in CLASSES:
        if re.search(pattern, lowered):
            return key
    return 'input'


def render():
    places = {}
    for text, where in literals():
        places.setdefault(text, []).append(where)
    groups = {key: [] for key, _, _ in CLASSES}
    for text in sorted(places):
        groups[classify(text)].append(text)
    lines = ['# Phase 6 refusal inventory', '',
             'Generated by `tools/generate-refusal-inventory.py` for the Phase 6 closure (branch 6 S9,',
             '[plan D5](phase-6-campaign-cycle-plan.md#decisions)): every reason sentence in `src/Oxce.Gameplay`,',
             '`src/Oxce.Savegames` and `src/Oxce.Extensions`, in one class each. The classes and their owners are in',
             '[Phase 6 status](phase-6-status.md#refusal-inventory). Counts: ' +
             ', '.join(f'{key} {len(groups[key])}' for key, _, _ in CLASSES) + f'; {len(places)} in all.', '']
    for key, title, _ in CLASSES:
        lines += [f'## {title}', '']
        for text in groups[key]:
            first = places[text][0].replace('src/', '')
            more = f' (+{len(places[text]) - 1})' if len(places[text]) > 1 else ''
            lines.append(f'- "{text}" — `{first}`{more}')
        lines.append('')
    return '\n'.join(lines)


if __name__ == '__main__':
    text = render()
    if '--check' in sys.argv:
        current = io.open(OUTPUT, encoding='utf-8').read() if os.path.exists(OUTPUT) else ''
        if current != text:
            print(f'{OUTPUT} is out of date; run python tools/generate-refusal-inventory.py.')
            sys.exit(1)
        print(f'{OUTPUT} is current.')
    else:
        io.open(OUTPUT, 'w', encoding='utf-8', newline='\n').write(text)
        print('wrote ' + OUTPUT)
