namespace ScreenStickyNotes.Models;

public class StickyNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = "";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 220;
    public string ColorKey { get; set; } = "yellow";
    public string FontFamily { get; set; } = "Yu Gothic UI";
    public double FontSize { get; set; } = 13;
    public bool IsTopmost { get; set; } = false;
    public bool IsFolded { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
