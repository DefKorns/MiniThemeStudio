using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Fonts;
using SkiaSharp;

namespace MiniThemeStudio.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private IReadOnlyList<FontAsset> fonts = [];

    [ObservableProperty]
    private bool convertJapanese;

    [ObservableProperty]
    private bool convertCyrillic;

    [ObservableProperty]
    private bool convertGreek;

    // the TTF last converted into the selected font, re-run when the script options change
    private string? lastConvertedFontPath;

    private bool _isUpdatingScriptCheckboxes;

    partial void OnConvertJapaneseChanged(bool value) => OnConvertOptionsChanged();
    partial void OnConvertCyrillicChanged(bool value) => OnConvertOptionsChanged();
    partial void OnConvertGreekChanged(bool value) => OnConvertOptionsChanged();

    private void OnConvertOptionsChanged()
    {
        if (_isUpdatingScriptCheckboxes)
        {
            return;
        }

        if (lastConvertedFontPath is not null)
        {
            ImportSelectedTrueTypeFont(lastConvertedFontPath);
        }
        else if (CanCompleteSelectedFont)
        {
            CompleteSelectedFont();
        }
        else
        {
            RenderFontSamplePreview();
        }
    }

    private string EffectiveSampleText =>
        (FontSampleText ?? string.Empty)
        + (ConvertJapanese ? "\n日本語 テーマ ダウンロード" : string.Empty)
        + (ConvertCyrillic ? "\nРусский Язык" : string.Empty)
        + (ConvertGreek ? "\nΕλληνικά Γλώσσα" : string.Empty);

    private static bool IsJapaneseChar(char c) =>
        (c is >= '\u3000' and <= '\u30FF') ||
        (c is >= '\u31F0' and <= '\u31FF') ||
        (c is >= '\u4E00' and <= '\u9FFF') ||
        (c is >= '\u2E80' and <= '\u2FD5') ||
        (c is >= '\uFF00' and <= '\uFFEF');

    private static bool IsCyrillicChar(char c) =>
        (c is >= '\u0400' and <= '\u04FF');

    private static bool IsGreekChar(char c) =>
        (c is >= '\u0370' and <= '\u03FF');

    [ObservableProperty]
    private FontAsset? selectedFont;

    [ObservableProperty]
    private MiniFont? currentLoadedFont;

    [ObservableProperty]
    private Bitmap? fontTexturePreview;

    [ObservableProperty]
    private Bitmap? fontSamplePreview;

    [ObservableProperty]
    private IReadOnlyList<FontGlyph> fontGlyphs = [];

    [ObservableProperty]
    private IReadOnlyList<FontGlyph> filteredFontGlyphs = [];

    [ObservableProperty]
    private string glyphSearchText = "";

    [ObservableProperty]
    private string fontSampleText = "SUPER MARIO WORLD\n0123456789 - MENU\nSTART / SELECT / OPTIONS";

    [ObservableProperty]
    private FontValidationResult? fontValidation;

    public bool CanReplaceFont => SelectedFont != null;

    partial void OnSelectedFontChanged(FontAsset? value)
    {
        OnPropertyChanged(nameof(CanReplaceFont));
        OnPropertyChanged(nameof(CanCompleteSelectedFont));
        lastConvertedFontPath = null;
        LoadSelectedFont(value);
    }

    partial void OnGlyphSearchTextChanged(string value)
    {
        ApplyGlyphFilter();
    }

    partial void OnFontSampleTextChanged(string value)
    {
        RenderFontSamplePreview();
    }

    private void ReloadFonts()
    {
        if (_draft is null)
        {
            Fonts = [];
            SelectedFont = null;
            return;
        }

        var prevSelectedName = SelectedFont?.Name;
        Fonts = _assetManager.GetFonts(_draft.DraftPath, _currentBaseUiPath);
        SelectedFont = Fonts.FirstOrDefault(f => f.Name.Equals(prevSelectedName, StringComparison.OrdinalIgnoreCase))
            ?? (Fonts.Count > 0 ? Fonts[0] : null);
    }

    private void LoadSelectedFont(FontAsset? font)
    {
        if (font is null)
        {
            CurrentLoadedFont = null;
            FontTexturePreview = null;
            FontSamplePreview = null;
            FontGlyphs = [];
            FilteredFontGlyphs = [];
            FontValidation = null;
            return;
        }

        try
        {
            CurrentLoadedFont = _assetManager.LoadFont(font.FilePath);
            FontGlyphs = CurrentLoadedFont.Glyphs;
            ApplyGlyphFilter();
            FontValidation = FontValidator.Validate(CurrentLoadedFont);

            if (CurrentLoadedFont.TexturePngBytes is { Length: > 0 })
            {
                using var ms = new MemoryStream(CurrentLoadedFont.TexturePngBytes);
                FontTexturePreview = new Bitmap(ms);
            }
            else
            {
                FontTexturePreview = null;
            }

            SyncScriptCheckboxesToFont(CurrentLoadedFont);
            RenderFontSamplePreview();
        }
        catch (Exception ex)
        {
            Status = $"Cannot load font {font.DisplayName}: {ex.Message}";
            CurrentLoadedFont = null;
            FontTexturePreview = null;
            FontSamplePreview = null;
            FontGlyphs = [];
            FilteredFontGlyphs = [];
            FontValidation = null;
        }
    }

    /// <summary>
    /// Silently updates the script checkboxes to match the characters
    /// actually present in the loaded font.
    /// </summary>
    private void SyncScriptCheckboxesToFont(MiniFont font)
    {
        var hasJapanese = false;
        var hasCyrillic = false;
        var hasGreek    = false;

        foreach (var g in font.Glyphs)
        {
            var c = (char)g.Id;
            if (!hasJapanese && IsJapaneseChar(c)) hasJapanese = true;
            if (!hasCyrillic && IsCyrillicChar(c)) hasCyrillic = true;
            if (!hasGreek    && IsGreekChar(c))    hasGreek    = true;
            if (hasJapanese && hasCyrillic && hasGreek) break;
        }

        _isUpdatingScriptCheckboxes = true;
        try
        {
            ConvertJapanese = hasJapanese;
            ConvertCyrillic = hasCyrillic;
            ConvertGreek    = hasGreek;
        }
        finally
        {
            _isUpdatingScriptCheckboxes = false;
        }
    }

    private void ApplyGlyphFilter()
    {
        if (string.IsNullOrWhiteSpace(GlyphSearchText))
        {
            FilteredFontGlyphs = FontGlyphs;
            return;
        }

        var query = GlyphSearchText.Trim();
        FilteredFontGlyphs = FontGlyphs
            .Where(g => g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        g.Character.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        $"U+{g.Id:X4}".Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RenderFontSamplePreview()
    {
        if (CurrentLoadedFont is null || CurrentLoadedFont.TexturePngBytes is not { Length: > 0 })
        {
            FontSamplePreview = null;
            return;
        }

        try
        {
            using var textureBmp = SKBitmap.Decode(CurrentLoadedFont.TexturePngBytes);
            if (textureBmp is null)
            {
                FontSamplePreview = null;
                return;
            }

            const int pad = 14;
            var lineH = Math.Max(12, CurrentLoadedFont.Common.LineHeight);
            var lines = EffectiveSampleText.Split('\n');
            var totalH = Math.Max(64, lines.Length * lineH + pad * 2);
            const int totalW = 620;

            using var surface = SKSurface.Create(new SKImageInfo(totalW, totalH, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x18, 0x18, 0x1B));

            var y = pad;
            foreach (var line in lines)
            {
                var x = pad;
                foreach (var ch in line)
                {
                    if (!ConvertJapanese && IsJapaneseChar(ch))
                    {
                        continue;
                    }

                    if (!ConvertCyrillic && IsCyrillicChar(ch))
                    {
                        continue;
                    }

                    if (!ConvertGreek && IsGreekChar(ch))
                    {
                        continue;
                    }

                    var glyph = CurrentLoadedFont.GetGlyph(ch);
                    if (glyph is { Width: > 0, Height: > 0 })
                    {
                        var srcRect = new SKRect(glyph.X, glyph.Y, glyph.X + glyph.Width, glyph.Y + glyph.Height);
                        var dstRect = new SKRect(
                            x + glyph.XOffset,
                            y + glyph.YOffset,
                            x + glyph.XOffset + glyph.Width,
                            y + glyph.YOffset + glyph.Height);

                        canvas.DrawBitmap(textureBmp, srcRect, dstRect);
                        x += glyph.XAdvance;
                    }
                    else if (ch == ' ')
                    {
                        x += Math.Max(8, lineH / 2);
                    }
                }
                y += lineH;
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            FontSamplePreview = new Bitmap(ms);
        }
        catch
        {
            FontSamplePreview = null;
        }
    }

    public bool ReplaceSelectedFontTexture(string sourcePngPath)
    {
        if (SelectedFont is null || _draft is null)
        {
            Status = "Select a font first.";
            return false;
        }

        try
        {
            _assetManager.ReplaceFontTexture(_draft.DraftPath, SelectedFont, sourcePngPath);
            ReloadAssets();
            RunValidation();
            MarkChanged();
            Status = $"Replaced texture for {SelectedFont.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    public bool ReplaceSelectedFontFile(string sourceFontPath)
    {
        if (SelectedFont is null || _draft is null)
        {
            Status = "Select a font first.";
            return false;
        }

        try
        {
            _assetManager.ReplaceFont(_draft.DraftPath, SelectedFont, sourceFontPath);
            ReloadAssets();
            RunValidation();
            MarkChanged();
            Status = $"Replaced font file for {SelectedFont.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    public bool ExportSelectedFontTexture(string destinationPath)
    {
        if (SelectedFont is null)
        {
            return false;
        }

        try
        {
            _assetManager.ExportFontTexture(SelectedFont, destinationPath);
            Status = $"Exported font texture for {SelectedFont.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    public bool ExportSelectedFontFile(string destinationPath)
    {
        if (SelectedFont is null)
        {
            return false;
        }

        try
        {
            _assetManager.ExportFont(SelectedFont, destinationPath);
            Status = $"Exported font {SelectedFont.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    public bool ImportSelectedTrueTypeFont(string fontOrZipPath, int? fontSize = null, bool antiAlias = false)
    {
        if (SelectedFont is null || _draft is null)
        {
            Status = "Select a font first.";
            return false;
        }

        try
        {
            _assetManager.ImportTrueTypeFont(_draft.DraftPath, SelectedFont, fontOrZipPath, fontSize, antiAlias, SelectedScripts, FillFontPath);
            ReloadFonts();
            RunValidation();
            MarkChanged();
            lastConvertedFontPath = fontOrZipPath;
            Status = $"Generated console font from {Path.GetFileName(fontOrZipPath)} for {SelectedFont.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    // the user's own font for the characters a font lacks (Settings); empty uses the system's or the built-in one
    public string? FillFontPath { get; set; }

    public bool CanCompleteSelectedFont =>
        SelectedFont is not null;

    private ExtraScripts SelectedScripts =>
        (ConvertJapanese ? ExtraScripts.Japanese : ExtraScripts.None)
        | (ConvertCyrillic ? ExtraScripts.Cyrillic : ExtraScripts.None)
        | (ConvertGreek ? ExtraScripts.Greek : ExtraScripts.None);

    // the missing characters, and the scripts ticked, from the complete font of the same role
    public bool CompleteSelectedFont()
    {
        if (SelectedFont is null || _draft is null)
        {
            Status = "Select a font first.";
            return false;
        }

        try
        {
            _assetManager.CompleteFont(_draft.DraftPath, SelectedFont, SelectedScripts, FillFontPath);
            var name = SelectedFont.DisplayName;
            ReloadFonts();
            RunValidation();
            MarkChanged();
            Status = $"Filled {name} from the base font.";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }
}
