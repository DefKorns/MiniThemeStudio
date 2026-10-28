using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ThemeSessionTests
{
    [Fact]
    public void AChangeMarksTheThemeUnsavedAndNotAsADraft()
    {
        var session = new ThemeSession(new ThemeResourceIndexer()) { IsDraftSaved = true };

        session.MarkChanged();

        Assert.True(session.HasUnsavedChanges);
        Assert.False(session.IsDraftSaved);
    }

    [Fact]
    public void ContentChangesAreAnnounced()
    {
        var session = new ThemeSession(new ThemeResourceIndexer());
        int announced = 0;
        session.ContentChanged += (_, _) => announced++;

        session.NotifyContentChanged();

        Assert.Equal(1, announced);
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public void ReloadWithoutAThemeEmptiesTheReportAndTellsTheTabs()
    {
        var session = new ThemeSession(new ThemeResourceIndexer());
        int reloads = 0;
        session.Reloaded += (_, _) => reloads++;

        session.Reload();

        Assert.Equal(1, reloads);
        Assert.Same(ThemeResourceReport.Empty, session.Report);
        Assert.Null(session.DraftPath);
    }
}
