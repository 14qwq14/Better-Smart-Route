import re

with open('build.log', 'rb') as logf:
    res = logf.read().decode('utf-16', 'ignore')
with open('src/RouteSuggest.cs', 'r', encoding='utf-8') as srcf:
    lines = srcf.read().split('\n')

e1 = re.findall(r'RouteSuggest\.cs\((\d+),\d+\): error CS1010', res)
e2 = re.findall(r'RouteSuggest\.cs\((\d+),\d+\): error CS1003', res)

for i in set(int(e)-1 for e in e1):
    lines[i] = lines[i].rstrip() + '\"'

for i in set(int(e)-1 for e in e2):
    if not lines[i].strip().endswith(','):
        lines[i] += ','

with open('src/RouteSuggest.cs', 'w', encoding='utf-8') as srcf:
    srcf.write('\n'.join(lines))
