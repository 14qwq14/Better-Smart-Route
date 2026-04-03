import os, subprocess, re

for _ in range(10):
    res = subprocess.run(['dotnet', 'build', 'RouteSuggest.csproj', '-c', 'Release'], capture_output=True, text=True)
    if 'CS1010' not in res.stdout:
        print('Done fixing CS1010')
        break
    
    with open('src/RouteSuggest.cs', 'r', encoding='utf-8') as f:
        lines = f.read().split('\n')
        
    errors = re.findall(r'RouteSuggest\.cs\((\d+),\d+\): error CS1010', res.stdout)
    err_lines = list(set([int(e)-1 for e in errors]))
    
    for l in err_lines:
        lines[l] = lines[l].rstrip() + '\"'
        
    with open('src/RouteSuggest.cs', 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
        
    print(f'fixed {len(err_lines)} lines')

for _ in range(10):
    res = subprocess.run(['dotnet', 'build', 'RouteSuggest.csproj', '-c', 'Release'], capture_output=True, text=True)
    if 'CS1003' not in res.stdout:
        print('Done fixing CS1003')
        break
    
    with open('src/RouteSuggest.cs', 'r', encoding='utf-8') as f:
        lines = f.read().split('\n')
        
    errors = re.findall(r'RouteSuggest\.cs\((\d+),\d+\): error CS1003:.*?,', res.stdout)
    if not errors: break
    err_lines = list(set([int(e)-1 for e in errors]))
    
    for l in err_lines:
        if not lines[l].strip().endswith(','): lines[l] = lines[l].rstrip() + ','
        
    with open('src/RouteSuggest.cs', 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
        
    print(f'fixed {len(err_lines)} lines')

