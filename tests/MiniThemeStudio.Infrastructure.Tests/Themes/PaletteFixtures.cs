using System;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

internal static class PaletteFixtures
{
    // LuaJIT 2.0.2 -b of:
    // colors_test = {
    //   bg_base = {0.5, 0.25, 0.75, 1},
    //   font_home_hud = {1, 1, 1, 1},
    //   font_resume_title = {0, 0.2, 0.4, 0.6},
    // }
    public const string Sample =
        "G0xKAQKwAQIAAgAIAAkzAAEAMwEAADoBAgAzAQMAOgEEADMBBQA6AQYANQAHAEcAAQAQY29sb3JzX3Rlc3QWZm9udF9yZXN1bWVfdGl0bGUBBQAAAwAEmrPmzAmZs6b+AwSas+bMCZmz5v4DBLPmzJkDs+aM/wMSZm9udF9ob21lX2h1ZAEFAAADAQMBAwEDAQxiZ19iYXNlAQAAAQUAAAQAgICA/wMEAICAwP4DBACAgKD/AwMBAA==";

    // the same with font_home_hud = {0.745098, 0, 0.101961, 1}
    public const string SampleEdited =
        "G0xKAQLCAQIAAgAIAAkzAAEAMwEAADoBAgAzAQMAOgEEADMBBQA6AQYANQAHAEcAAQAQY29sb3JzX3Rlc3QWZm9udF9yZXN1bWVfdGl0bGUBBQAAAwAEmrPmzAmZs6b+AwSas+bMCZmz5v4DBLPmzJkDs+aM/wMSZm9udF9ob21lX2h1ZAEFAAAEjKmolgzXr5//AwMABLLW3sMLnbTo/QMDAQxiZ19iYXNlAQAAAQUAAAQAgICA/wMEAICAwP4DBACAgKD/AwMBAA==";

    public static byte[] SampleBytes => Convert.FromBase64String(Sample);

    public static byte[] SampleEditedBytes => Convert.FromBase64String(SampleEdited);
}
