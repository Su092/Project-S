import re, json

src = open(r'E:\S\Assets\Resources\Data\sdata.json', encoding='utf-8').read()
src = re.sub(r'^\s*//.*$', '', src, flags=re.M)
d = json.loads(src)

print('guns:')
for g in d['guns']:
    print('  ', g['id'], g.get('name'), 'slots', g.get('slotCount'), 'starter', g.get('starterChain'))
print('\nbullets:', len(d['bullets']))
for b in d['bullets']:
    print('  ', b['id'], b.get('name'), b.get('rarity'))
print('\nmodifiers:', len(d['modifiers']))
for m in d['modifiers']:
    print('  ', m['id'], m.get('name'), 'scope', m.get('scope'), m.get('rarity'))
