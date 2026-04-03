import re

with open('README.md', 'r', encoding='utf-8') as f:
    text = f.read()

print("视觉高亮" in text)
print("三条路线高亮" in text)
print("可以在游戏内" in text)
