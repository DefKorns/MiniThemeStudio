using System.Collections.Generic;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Models;

// frames cut from a sprite sheet, saved as PNG files, all of FrameSize
public sealed record SpriteSheetResult(IReadOnlyList<string> FramePaths, AtlasSize FrameSize);
