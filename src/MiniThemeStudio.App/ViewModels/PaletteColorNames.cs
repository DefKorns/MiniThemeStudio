using System;
using System.Collections.Generic;
using System.Globalization;

namespace MiniThemeStudio.App.ViewModels;

// What the colours of the firmware palettes are called to the person editing them
public static class PaletteColorNames
{
    private static readonly Dictionary<string, string> Titles = new()
    {
        ["bg_base"] = "Background",
        ["bg_luigi"] = "Background pattern: Luigi",
        ["bg_mario"] = "Background pattern: Mario",
        ["font_home_gametitle"] = "Home: game title",
        ["font_home_caption"] = "Home: caption",
        ["font_home_hud"] = "Home: info text",
        ["font_home_sort"] = "Home: sort label",
        ["font_option_title"] = "Options: title",
        ["font_option_subtitle"] = "Options: subtitle",
        ["font_option_text"] = "Options: text",
        ["font_option_hud"] = "Options: info text",
        ["font_option_item"] = "Options: item",
        ["font_option_item_disable"] = "Options: item (disabled)",
        ["font_option_button"] = "Options: button",
        ["font_option_button_focus"] = "Options: button (selected)",
        ["font_dialog_text"] = "Dialog: text",
        ["font_dialog_button"] = "Dialog: button",
        ["font_dialog_button_focus"] = "Dialog: button (selected)",
        ["font_resume_title"] = "Suspend points: title",
        ["font_resume_hud"] = "Suspend points: info text",
        ["font_resume_time"] = "Suspend points: time",
        ["font_resume_empty"] = "Suspend points: empty slot",
    };

    private static readonly Dictionary<string, string> Regions = new()
    {
        ["eur"] = "Europe",
        ["usa"] = "USA",
        ["jpn"] = "Japan",
        ["nes"] = "NES",
        ["hvc"] = "Famicom",
    };

    public static string Title(string colorName) =>
        Titles.TryGetValue(colorName, out string? title) ? title : Humanize(colorName);

    public static string Region(string region) =>
        Regions.TryGetValue(region, out string? name) ? name : region;

    // a colour this table doesn't know: "font_foo_bar" -> "Foo bar"
    private static string Humanize(string colorName)
    {
        string words = colorName.Replace('_', ' ').Trim();
        foreach (string prefix in new[] { "font ", "bg " })
        {
            if (words.StartsWith(prefix, StringComparison.Ordinal))
            {
                words = words[prefix.Length..];
                break;
            }
        }

        return words.Length == 0 ? colorName : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }
}
