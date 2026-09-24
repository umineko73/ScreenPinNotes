// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class PathDisplayTests
{
    [Theory]
    [InlineData("assets/0月写真/image.png", 30, "assets/0月写真/image.png")]
    [InlineData("assets/0月写真/image.png", 16, "…/0月写真/image.png")]
    [InlineData("assets/0月写真/image.png", 11, "…/image.png")]
    [InlineData("assets/0月写真/image.png", 7, "…ge.png")]
    [InlineData("assets/0月写真/image.png", 0, "")]
    public void Fit_PreservesTrailingPath(string path, int width, string expected)
        => Assert.Equal(expected, PathDisplay.Fit(path, width, text => text.Length));

    [Theory]
    [InlineData("![photo](assets/image.png)", "assets/image.png")]
    [InlineData("![](assets/image.png){width=100}", "assets/image.png")]
    [InlineData("text\n![photo](assets/image.png)", null)]
    [InlineData("", null)]
    public void ImageOnly_ExcludesTextNotes(string source, string? target)
        => Assert.Equal(target, MarkdownRenderer.GetImageOnlyTarget(source));
}
