namespace MiniThemeStudio.Core.Themes;

public interface IThemeDraftService
{
    ThemeDraft OpenExisting(string themePath);

    ThemeDraft CreateNew(NewThemeRequest request);

    ThemeDraft Save(ThemeDraft draft);

    void Discard(ThemeDraft draft);
}
