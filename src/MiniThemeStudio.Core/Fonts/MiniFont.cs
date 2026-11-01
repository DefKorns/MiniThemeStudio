namespace MiniThemeStudio.Core.Fonts;

public sealed class MiniFont
{
    private readonly Dictionary<uint, FontGlyph> _glyphMap;

    public MiniFont(
        byte formatType,
        FontInfo info,
        FontCommon common,
        IReadOnlyList<string> pageNames,
        IReadOnlyList<FontGlyph> glyphs,
        IReadOnlyList<FontKerningPair> kerningPairs,
        byte[] texturePngBytes)
    {
        FormatType = formatType;
        Info = info;
        Common = common;
        PageNames = pageNames;
        Glyphs = glyphs;
        KerningPairs = kerningPairs;
        TexturePngBytes = texturePngBytes;

        _glyphMap = new Dictionary<uint, FontGlyph>(glyphs.Count);
        foreach (FontGlyph glyph in glyphs)
        {
            _glyphMap[glyph.Id] = glyph;
        }
    }

    public byte FormatType { get; set; }
    public FontInfo Info { get; set; }
    public FontCommon Common { get; set; }
    public IReadOnlyList<string> PageNames { get; set; }
    public IReadOnlyList<FontGlyph> Glyphs { get; set; }
    public IReadOnlyList<FontKerningPair> KerningPairs { get; set; }
    public byte[] TexturePngBytes { get; set; }

    public FontGlyph? GetGlyph(char character) => GetGlyph((uint)character);

    public FontGlyph? GetGlyph(uint characterId) =>
        _glyphMap.TryGetValue(characterId, out FontGlyph? glyph) ? glyph : null;

    public bool HasGlyph(char character) => HasGlyph((uint)character);

    public bool HasGlyph(uint characterId) => _glyphMap.ContainsKey(characterId);
}
