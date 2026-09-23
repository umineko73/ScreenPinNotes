namespace ScreenPinNotes.Services;

public enum StorageLoadFailure { InvalidData, AccessDenied, IoError, Other }

public sealed record StorageLoadIssue(string Path, StorageLoadFailure Kind, Exception Error)
{
    public static StorageLoadIssue From(string path, Exception error) => new(path, error switch
    {
        System.Text.Json.JsonException or ArgumentException => StorageLoadFailure.InvalidData,
        UnauthorizedAccessException => StorageLoadFailure.AccessDenied,
        System.IO.IOException => StorageLoadFailure.IoError,
        _ => StorageLoadFailure.Other,
    }, error);
}

public sealed record StorageLoadResult<T>(T Value, IReadOnlyList<StorageLoadIssue> Issues)
{
    public bool HasErrors => Issues.Count != 0;
}
