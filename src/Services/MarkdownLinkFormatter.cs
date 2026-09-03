// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace ScreenPinNotes.Services;

public static class MarkdownLinkFormatter
{
    public static string Build(string label, string target)
        => $"[{EscapeLabel(label)}]({EscapeTarget(target)})";

    private static string EscapeLabel(string value)
        => value.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");

    private static string EscapeTarget(string value)
        => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
