namespace MiniThemeStudio.Infrastructure.Tests.Themes;

internal static class CharacterPatternFixtures
{
    // LuaJIT 2.0.2 -bs of the shape of sys_wallpaper_fg0.lua:
    // local demo = { block = { { 1 }, { 2, 192 } } }
    // local patterns = {
    //   smb_mario = { sizeh = 16, tex_variable_name = "smb_tex", sizew = 16, pattern = { { 0, 1 }, { 18, 1 } } },
    //   smb_block = { sizeh = 16, tex_variable_name = "smb_tex", sizew = 16, pattern = { { 0, 35 } } },
    // }
    // fg = {}
    // function fg.get(name) return patterns[name], demo end
    public const string Sample =
        "G0xKAQIbAAEDAgAABCsBAAA2AQABKwIBAEYBAwABwADArAIDAAUAEAAdMwACADIBAwAzAgAAOwIBATMCAQA7AgIBOgEDADMBCAAzAgQAMgMDADMEBQA7BAEDMwQGADsEAgM6AwcCOgIJATMCCgAyAwMAMwQLADsEAQM6AwcCOgIMATICAAA1Ag0ANAINADEDDwA6Aw4CMAAAgEcAAQAACGdldAdmZw5zbWJfYmxvY2sBAwAAAwADIwEAAwpzaXplaAMQFnRleF92YXJpYWJsZV9uYW1lDHNtYl90ZXgKc2l6ZXcDEA5zbWJfbWFyaW8BAAAMcGF0dGVybgEDAAADEgMBAQMAAAMAAwEBAAMKc2l6ZWgDEBZ0ZXhfdmFyaWFibGVfbmFtZQxzbWJfdGV4CnNpemV3AxAKYmxvY2sBAAABAwAAAwIDwAEBAgAAAwEA";

    // the same with smb_mario = { sizeh = 36, tex_variable_name = "smb_tex", sizew = 28, pattern = { { 0, 1 }, { 300, 1 } } }
    public const string SampleEdited =
        "G0xKAQIbAAEDAgAABCsBAAA2AQABKwIBAEYBAwABwADArQIDAAUAEAAdMwACADIBAwAzAgAAOwIBATMCAQA7AgIBOgEDADMBCAAzAgQAMgMDADMEBQA7BAEDMwQGADsEAgM6AwcCOgIJATMCCgAyAwMAMwQLADsEAQM6AwcCOgIMATICAAA1Ag0ANAINADEDDwA6Aw4CMAAAgEcAAQAACGdldAdmZw5zbWJfYmxvY2sBAwAAAwADIwEAAwpzaXplaAMQFnRleF92YXJpYWJsZV9uYW1lDHNtYl90ZXgKc2l6ZXcDEA5zbWJfbWFyaW8BAAAMcGF0dGVybgEDAAADrAIDAQEDAAADAAMBAQADCnNpemVoAyQWdGV4X3ZhcmlhYmxlX25hbWUMc21iX3RleApzaXpldwMcCmJsb2NrAQAAAQMAAAMCA8ABAQIAAAMBAA==";

    public static byte[] SampleBytes => Convert.FromBase64String(Sample);

    public static byte[] SampleEditedBytes => Convert.FromBase64String(SampleEdited);
}
