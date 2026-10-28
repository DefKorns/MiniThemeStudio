using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.App.Tests.TestDoubles;

public sealed class FakeAppearanceService : IAppearanceService
{
    public ApplicationThemePreference? Applied { get; private set; }

    public void Apply(ApplicationThemePreference preference)
    {
        Applied = preference;
    }
}
