# Markdown Syntax List

This note lists the Markdown syntax supported in the body. When editing is locked, checkboxes and the body/title/image display sizes can still be adjusted.

## Inline Syntax

| Type | Syntax | Rendered |
| --- | --- | --- |
| Bold | `**Important**` or `__Important__` | **Important** |
| Italic | `*Note*` or `_Note_` | *Note* |
| Strikethrough | `~~Removed~~` | ~~Removed~~ |
| Inline code | `` `Ctrl+C` `` | `Ctrl+C` |
| Markdown link | `[OpenAI](https://openai.com/)` | [OpenAI](https://openai.com/) |
| Auto URL link | `https://www.google.com` | https://www.google.com |
| Auto Windows path link | `C:\Users` | C:\Users |
| Escape | `\*show as text\*` | \*show as text\* |

## Block Syntax

| Type | Syntax | Rendered |
| --- | --- | --- |
| Headings 1 to 6 | `# Heading 1` through `###### Heading 6` | See "Headings" below |
| Bullet list | `- Item`, `* Item`, `+ Item` | See "Bullet list" below |
| Numbered list | `1. Item` | See "Numbered list" below |
| Checklist | `- [ ] Todo`, `- [x] Done` | See "Checklist" below |
| Quote | `> Quote text` | See "Quote" below |
| Horizontal rule | `---`, `***`, `___` | See "Horizontal rule" below |
| Code block | Wrap lines with three backticks | See "Code block" below |
| Table | `\| Col 1 \| Col 2 \|` plus `\| --- \| --- \|` | See "Table" below |
| Table left align | `\| :--- \|` | See "Table" below |
| Table center align | `\| :---: \|` | See "Table" below |
| Table right align | `\| ---: \|` | See "Table" below |

## Block Rendered Examples

### Headings

# Heading 1
## Heading 2
### Heading 3

### Bullet list

- Item A
* Item B
+ Item C

### Numbered list

1. First
2. Second

### Checklist

- [x] Completed item
- [ ] Click to toggle in view mode or while editing is locked

### Quote

> This is a quote example.

### Horizontal rule

---

### Table

| Left | Center | Right |
| :--- | :---: | ---: |
| A | B | C |

### Code block

```csharp
var note = "Markdown ready";
Console.WriteLine(note);
```

## Images

| Type | Syntax |
| --- | --- |
| Image | `![description](assets/image.png)` |
| Width | `![description](assets/image.png){width=320}` |

Image targets can be files in this note's `assets` folder, Windows paths, or URLs. Pasted images are saved into this note's `assets` folder.

## Syntax Created by Operations

| Operation | Result |
| --- | --- |
| Paste an image | `![description](assets/...)` |
| `Ctrl + mouse wheel` over an image | Changes the image `{width=...}` |
| `Ctrl + mouse wheel` over the body | Changes the body font size |
| `Ctrl + mouse wheel` over the title bar | Changes the title font size |
| Paste an Excel range | Markdown table |
| Paste as Markdown link | `[label](URL)` |
