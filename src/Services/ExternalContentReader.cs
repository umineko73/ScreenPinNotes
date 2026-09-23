using System.IO;
using System.Text;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

/// <summary>External files are read independently of note storage and WPF presentation.</summary>
public static class ExternalContentReader
{
    public static async Task<ExternalContentReadResult> ReadForDisplayAsync(
        string path, bool tail, int tailLineCount, CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (tail)
            {
                var content = await Task.Run(() => ReadTail(fullPath, Math.Max(1, tailLineCount), cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                return new(true, content);
            }
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return new(true, await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, "");
        }
    }

    public const int DefaultExternalTailLineCount = 200;

    public static string ReadExternalContent(StickyNote note, int tailLineCount = DefaultExternalTailLineCount)
    {
        var path = note.ExternalContentPath;
        if (string.IsNullOrWhiteSpace(path))
            return note.Content;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return $"External file not found:\n{fullPath}";

            return note.ExternalTailMode
                ? ReadTail(fullPath, Math.Max(1, tailLineCount))
                : ReadAllSharedText(fullPath);
        }
        catch (Exception ex)
        {
            return $"External file could not be read:\n{path}\n\n{ex.Message}";
        }
    }

    // 読み込みに失敗しても直前のキャッシュを壊さないための Try 版。
    // 一時的にファイルが読めない場合でも content.md 上のキャッシュを
    // エラー文言で上書きしないよう、呼び出し側は成功時のみ内容を反映する。
    public static bool TryReadExternalContent(StickyNote note, out string content)
    {
        var path = note.ExternalContentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            content = "";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                content = "";
                return false;
            }

            content = ReadAllSharedText(fullPath);
            return true;
        }
        catch
        {
            content = "";
            return false;
        }
    }

    /// <summary>
    /// 外部ファイルの全文を、書き手と共有したまま読む。ログのように別のプロセスが
    /// 開いたまま追記しているファイルは、<see cref="File.ReadAllText(string)"/>
    /// （FileShare.Read で開く）では「別のプロセスが使用中」となり読めない。
    /// </summary>
    private static string ReadAllSharedText(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // tail 表示のときは末尾の行数だけを読む Try 版。ノートの ExternalTailMode に
    // 応じて全文/tail のどちらを読むかを切り替えたい呼び出し側はこちらを使う。
    public static bool TryReadExternalContentForDisplay(StickyNote note, int tailLineCount, out string content)
        => note.ExternalTailMode
            ? TryReadExternalContentTail(note, tailLineCount, out content)
            : TryReadExternalContent(note, out content);

    private static bool TryReadExternalContentTail(StickyNote note, int tailLineCount, out string content)
    {
        var path = note.ExternalContentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            content = "";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                content = "";
                return false;
            }

            content = ReadTail(fullPath, Math.Max(1, tailLineCount));
            return true;
        }
        catch
        {
            content = "";
            return false;
        }
    }

    private const int TailReadChunkBytes = 64 * 1024;

    /// <summary>
    /// ファイル末尾の <paramref name="lineCount"/> 行だけを読む。育ち続けるログは
    /// 数百MBになり得るため、全文を読んでから split するのではなく末尾から
    /// チャンク単位で遡って改行を数え、必要な範囲が分かった時点でそこだけ返す。
    /// 改行 (0x0A) は UTF-8 の継続バイト（0x80-0xBF）にも先頭バイトにも現れないので、
    /// デコード前のバイト列を直接走査してよい。
    /// </summary>
    private static string ReadTail(string path, int lineCount, CancellationToken cancellationToken = default)
    {
        // ログはローテーションで消されることもあるので、削除も妨げないで開く。
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length == 0)
            return "";

        var buffer = new byte[TailReadChunkBytes];
        var newlinesNeeded = lineCount;
        var position = length;
        var foundBoundary = false;

        while (position > 0)
        {
            var chunkSize = (int)Math.Min(TailReadChunkBytes, position);
            position -= chunkSize;
            stream.Seek(position, SeekOrigin.Begin);
            var read = ReadExact(stream, buffer, chunkSize, cancellationToken);
            for (var i = read - 1; i >= 0; i--)
            {
                if (buffer[i] != (byte)'\n')
                    continue;
                // ファイル末尾ちょうどの改行は最終行の終端でしかないので、
                // 区切りとしては数えない（数えると空行が1行増えて見える）。
                if (position + i == length - 1)
                    continue;

                if (--newlinesNeeded <= 0)
                {
                    position += i + 1;
                    foundBoundary = true;
                    break;
                }
            }
            if (foundBoundary)
                break;
        }

        var resultLength = (int)(length - position);
        var result = new byte[resultLength];
        stream.Seek(position, SeekOrigin.Begin);
        ReadExact(stream, result, resultLength, cancellationToken);
        // Strip the UTF-8 preamble only when the selected tail starts at byte 0.
        var offset = position == 0 && result.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? 3 : 0;
        return Encoding.UTF8.GetString(result, offset, result.Length - offset);
    }

    internal static int ReadExact(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException("External log was truncated while reading its tail.");
            totalRead += read;
        }

        return totalRead;
    }

}
