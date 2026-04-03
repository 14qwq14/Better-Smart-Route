import re

with open('src/RouteSuggest.cs', 'rb') as f:
    lines = f.read().decode('utf-8', 'replace').split('\n')

for i in range(len(lines)):
    line = lines[i]
    if 'Name = "' in line and not line.strip().endswith(','):
        lines[i] = re.sub(r'(\ufffd)+', '', line).rstrip() + '",'
    if 'labels: new()' in line and 'zhs' in line and not line.strip().endswith('} },'):
        lines[i] = re.sub(r'(\ufffd)+', '', line).rstrip() + '\" } },'
    if 'descriptions: new()' in line and 'en' in line and not line.strip().endswith('} },'):
        lines[i] = re.sub(r'(\ufffd)+', '', line).rstrip() + '\" } },'
    if '{ \"en\", \"' in line and not line.strip().endswith('} };'):
        if 'roomLabels' in lines[i-1] or 'roomDescriptions' in lines[i-1]:
            lines[i] = re.sub(r'(\ufffd)+', '', line).rstrip() + '\" };'
    if 'roomLabels[\"zhs\"]' in line or 'roomDescriptions[\"zhs\"]' in line:
        if not line.strip().endswith(';'):
            lines[i] = re.sub(r'(\ufffd)+', '', line).rstrip() + '\";'
    
with open('src/RouteSuggest.cs', 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines))
