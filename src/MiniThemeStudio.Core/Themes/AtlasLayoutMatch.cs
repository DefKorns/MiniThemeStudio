using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.Core.Themes;

public sealed record AtlasLayoutMatch(
    InstalledBaseInfo Base,
    string AtlasName,
    double CoveragePercent,
    int FramesWithContent,
    int FrameCount);

public interface IAtlasLayoutMatcher
{
    IReadOnlyList<AtlasLayoutMatch> Rank(string imagePath, IEnumerable<InstalledBaseInfo> bases);
}
