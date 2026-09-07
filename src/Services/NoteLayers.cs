using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

public enum LayerMove { Top, Up, Down, Bottom }

public static class NoteLayers
{
    // Windows keeps always-on-top windows in a separate band.
    public static List<StickyNote> Ordered(IEnumerable<StickyNote> notes) => notes
        .OrderByDescending(n => n.IsTopmost).ThenBy(n => n.LayerOrder)
        .ThenByDescending(n => n.CreatedAt).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();

    public static void Move(IEnumerable<StickyNote> notes, ISet<string> selected, LayerMove move)
    {
        var ordered = Ordered(notes);
        foreach (var band in ordered.GroupBy(n => n.IsTopmost))
        {
            var rows = band.ToList();
            bool Chosen(StickyNote n) => selected.Contains(n.Id);
            if (move == LayerMove.Top) rows = rows.Where(Chosen).Concat(rows.Where(n => !Chosen(n))).ToList();
            else if (move == LayerMove.Bottom) rows = rows.Where(n => !Chosen(n)).Concat(rows.Where(Chosen)).ToList();
            else if (move == LayerMove.Up)
            {
                for (int i = 1; i < rows.Count; i++)
                    if (Chosen(rows[i]) && !Chosen(rows[i - 1])) (rows[i - 1], rows[i]) = (rows[i], rows[i - 1]);
            }
            else
            {
                for (int i = rows.Count - 2; i >= 0; i--)
                    if (Chosen(rows[i]) && !Chosen(rows[i + 1])) (rows[i], rows[i + 1]) = (rows[i + 1], rows[i]);
            }
            for (int i = 0; i < rows.Count; i++) rows[i].LayerOrder = i;
        }
    }
}
