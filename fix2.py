import re
with open('src/RouteSuggest.cs', 'rb') as f:
    lines = f.read().decode('utf-8', 'ignore').split('\n')
for i in range(len(lines)):
    line = lines[i]
    if '\"' in line or '?' in line:
        b = line.encode('utf-8')
        b = re.sub(b'[^\x00-\x7F]', b'', b)
        line = b.decode()
        if line.count('\"') % 2 != 0:
            if 'Name = ' in line: line += '\",'
            elif '\"zhs\"' in line and '},' not in line: line += '\" } },'
            elif '\"en\"' in line and '},' not in line: line += '\" } },'
            elif '[' in line and ']' in line and '=' in line: line += '\";'
            else: line += '\"'
        lines[i] = line
open('src/RouteSuggest.cs', 'w', encoding='utf-8').write('\n'.join(lines))
