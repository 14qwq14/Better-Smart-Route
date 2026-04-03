import re, codecs

with codecs.open('build.log', 'r', 'gbk', 'ignore') as f:
    res = f.read()

with codecs.open('src/RouteSuggest.cs', 'r', 'utf-8', 'ignore') as f:
    lines = f.read().split('\n')

errors = re.findall(r'RouteSuggest\.cs\((\d+),\d+\): error CS1010', res)
s = set([int(e)-1 for e in set(errors)])

for i in s:
    lines[i] = lines[i].rstrip() + '\"'
    print(f'fixed line {i+1}')

with codecs.open('src/RouteSuggest.cs', 'w', 'utf-8') as f:
    f.write('\n'.join(lines))
