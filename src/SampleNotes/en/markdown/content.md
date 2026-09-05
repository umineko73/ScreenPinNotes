# Markdown Syntax List

This note lists the Markdown syntax supported in the body. When editing is locked, checkboxes and the body/title/image display sizes can still be adjusted.

## Markdown input assistance

While editing, select text and right-click **Markdown formatting** for bold, strikethrough, inline code, headings 1–3, bullets, checklists, and links. Line formatting applies to whole affected lines. With no selection, inline formatting places the caret between markers. Reapplying the matching supported format removes it. Use `Ctrl+Z` to undo.

Place the caret inside an existing link and choose **Edit link...** to edit its display text and URL separately. The URL wraps and the dialog can be resized. **Markdown formatting > Insert / edit link...** also creates new links.

The helper does not validate or repair every possible Markdown combination. Rendering failures fall back to source text. Deep nesting (16 levels), long inline text (over 8,192 characters), and large documents (over 131,072 characters or 2,000 newline characters) limit parsing and display source text without deleting the content.

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

Only local images render inline: files in this note's `assets` folder or Windows paths. Remote image URLs do not display an image preview. Pasted images are saved into this note's `assets` folder.

## Syntax Created by Operations

| Operation | Result |
| --- | --- |
| Paste an image | `![description](assets/...)` |
| `Ctrl + mouse wheel` over an image | Changes the image `{width=...}` |
| `Ctrl + mouse wheel` over the body | Changes the body font size |
| `Ctrl + mouse wheel` over the title bar | Changes the title font size |
| Paste an Excel range | Markdown table |
| Paste as Markdown link | `[label](URL)` |
