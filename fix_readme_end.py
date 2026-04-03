import re

with open('README-EN.md', 'r', encoding='utf-8') as f:
    text = f.read()

# Fix table description
text = re.sub(
    r'\| `scoring_weights`\s*\|\s*Object\s*\|.*?(?=\n)',
    r'| `scoring_weights`  | Object  | Target count range for each node type (uses min and max fields). Deviations face penalties. |',
    text
)

# Fix paragraph description
text = text.replace(
    '- **scoring_weights**: (Optional) Target count for each room type. E.g. `"Elite": 15` means combat as many elites as possible. The algorithm calculates penalty based on the squared deviation from target counts, finding the best route matching your preference.',
    '- **scoring_weights**: (Optional) Target count ranges for each room type. E.g. `"Elite": {"min": 15, "max": 15}` means combat as many elites as possible. The algorithm calculates penalty based on the squared deviation from target bounds, finding the best route matching your preference.'
)

# Fix the last example
text = text.replace(
    '"Elite": 0',
    '"Elite": {"min": 0, "max": 0}'
)

with open('README-EN.md', 'w', encoding='utf-8') as f:
    f.write(text)

print("done")
