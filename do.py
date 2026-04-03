import codecs, re
with codecs.open('src/RouteSuggest.cs', 'rb') as f:
    lines = f.read().decode('utf-8', 'replace').split('\n')
for i in range(len(lines)):
    line = lines[i].replace('\ufffd', '')
    if '"' in line or '?' in line:
        c = line.count('"')
        if c % 2 != 0:
            if 'Name = ' in line:
                line = line.rstrip()
                if line.endswith(','): line = line[:-1].rstrip() + '",'
                else: line = line + '",'
            elif 'zhs' in line or 'en' in line:
                if 'labels: new()' in line or 'descriptions: new()' in line:
                    line = line.rstrip()
                    if line.endswith(','): line = line[:-1].rstrip() + '" } },'
                    else: line += '" } },'
                elif '[' in line and ']' in line:
                    line = line.rstrip()
                    if line.endswith(';'): line = line[:-1].rstrip() + '";'
                    else: line += '";'
                else:
                    line += '"'
            else:
                line = line.rstrip()
                if line.endswith(','): line = line[:-1].rstrip() + '",'
                else: line += '"'
    lines[i] = line
with open('src/RouteSuggest.cs', 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines))
