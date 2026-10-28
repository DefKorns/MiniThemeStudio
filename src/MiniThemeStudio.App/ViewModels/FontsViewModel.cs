using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.App.ViewModels;

// The Fonts tab: the console fonts of the theme, their glyphs, a rendered sample, and tools to replace,
// convert from TTF/OTF or fill the characters a font lacks.
public sealed partial class FontsViewModel : ViewModelBase
{
    private static readonly FileType Png = new("PNG image", "*.png");
    private static readonly FileType ConsoleFont = new("Console font file", "*.fnt", "*.font");

    private readonly ThemeSession _session;
    private readonly IThemeAssetManager _assetManager;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IFileDialogService _fileDialogs;
    private readonly IErrorHandler _errorHandler;

    public FontsViewModel(
        ThemeSession session,
        IThemeAssetManager assetManager,
        IAppSettingsStore settingsStore,
        IFileDialogService fileDialogs,
        IErrorHandler errorHandler)
    {
        _session = session;
        _assetManager = assetManager;
        _settingsStore = settingsStore;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
    }

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

        // the font is generated again with the scripts now ticked
        if (lastConvertedFontPath is not null)
        {
            _ = ImportTrueTypeFontAsync(lastConvertedFontPath);
        }
        else if (CanCompleteSelectedFont)
        {
            _ = CompleteFontAsync();
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
        NotifyFontCommands();
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

    private void Reload()
    {
        if (_session.Draft is null)
        {
            Fonts = [];
            SelectedFont = null;
            return;
        }

        var previous = SelectedFont;
        Fonts = _assetManager.GetFonts(_session.Draft.DraftPath, _session.BaseUiPath);
        SelectedFont = Fonts.FirstOrDefault(f => f.Name.Equals(previous?.Name, StringComparison.OrdinalIgnoreCase))
            ?? (Fonts.Count > 0 ? Fonts[0] : null);

        // an equal record raises no change, but the file under it may have been rewritten
        if (Equals(previous, SelectedFont))
        {
            LoadSelectedFont(SelectedFont);
        }
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
            _session.Status = $"Cannot load font {font.DisplayName}: {ex.Message}";
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
        var hasGreek = false;

        foreach (var g in font.Glyphs)
        {
            var c = (char)g.Id;
            if (!hasJapanese && IsJapaneseChar(c))
            {
                hasJapanese = true;
            }

            if (!hasCyrillic && IsCyrillicChar(c))
            {
                hasCyrillic = true;
            }

            if (!hasGreek && IsGreekChar(c))
            {
                hasGreek = true;
            }

            if (hasJapanese && hasCyrillic && hasGreek)
            {
                break;
            }
        }

        _isUpdatingScriptCheckboxes = true;
        try
        {
            ConvertJapanese = hasJapanese;
            ConvertCyrillic = hasCyrillic;
            ConvertGreek = hasGreek;
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

    // the user's own font for the characters a font lacks (Settings); empty uses the system's or the built-in one
    private string? FillFontPath => _settingsStore.Current.FillFontPath;

    public bool CanCompleteSelectedFont =>
        SelectedFont is not null;

    private ExtraScripts SelectedScripts =>
        (ConvertJapanese ? ExtraScripts.Japanese : ExtraScripts.None)
        | (ConvertCyrillic ? ExtraScripts.Cyrillic : ExtraScripts.None)
        | (ConvertGreek ? ExtraScripts.Greek : ExtraScripts.None);

    [RelayCommand(CanExecute = nameof(CanReplaceFont))]
    private async Task ConvertTrueTypeFontAsync()
    {
        if (SelectedFont is { } font
            && await _fileDialogs.OpenFileAsync(
                $"Select TTF, OTF or ZIP font file to convert for {font.DisplayName}",
                new FileType("Font or Zip package", "*.ttf", "*.otf", "*.zip"),
                new FileType("TrueType / OpenType font", "*.ttf", "*.otf"),
                new FileType("Zip archive", "*.zip")) is { } fontPath)
        {
            await ImportTrueTypeFontAsync(fontPath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplaceFont))]
    private async Task ReplaceTextureAsync()
    {
        if (_session.Draft is not { } draft
            || SelectedFont is not { } font
            || await _fileDialogs.OpenFileAsync($"Select replacement PNG texture for {font.DisplayName}", Png) is not { } pngPath)
        {
            return;
        }

        if (await RunAsync("Cannot replace font texture", () => _assetManager.ReplaceFontTexture(draft.DraftPath, font, pngPath)))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Replaced texture for {font.DisplayName}.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplaceFont))]
    private async Task ReplaceFontFileAsync()
    {
        if (_session.Draft is not { } draft
            || SelectedFont is not { } font
            || await _fileDialogs.OpenFileAsync($"Select replacement font file for {font.DisplayName}", ConsoleFont) is not { } fontPath)
        {
            return;
        }

        if (await RunAsync("Cannot replace font file", () => _assetManager.ReplaceFont(draft.DraftPath, font, fontPath)))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Replaced font file for {font.DisplayName}.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplaceFont))]
    private async Task ExportTextureAsync()
    {
        if (SelectedFont is { } font
            && await _fileDialogs.SaveFileAsync($"Export font texture {font.DisplayName}", $"{font.Name}_texture.png", "png", Png) is { } savePath
            && await RunAsync("Cannot export font texture", () => _assetManager.ExportFontTexture(font, savePath)))
        {
            _session.Status = $"Exported font texture for {font.DisplayName}.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplaceFont))]
    private async Task ExportFontFileAsync()
    {
        if (SelectedFont is not { } font)
        {
            return;
        }

        var extension = Path.GetExtension(font.FilePath);
        if (await _fileDialogs.SaveFileAsync($"Export font file {font.DisplayName}", $"{font.Name}{extension}", extension.TrimStart('.'), ConsoleFont) is { } savePath
            && await RunAsync("Cannot export font", () => _assetManager.ExportFont(font, savePath)))
        {
            _session.Status = $"Exported font {font.DisplayName}.";
        }
    }

    // the missing characters, and the scripts ticked, from the complete font of the same role
    [RelayCommand(CanExecute = nameof(CanReplaceFont))]
    private async Task CompleteFontAsync()
    {
        if (_session.Draft is not { } draft || SelectedFont is not { } font)
        {
            return;
        }

        var scripts = SelectedScripts;
        var fillFontPath = FillFontPath;
        if (await RunAsync("Cannot fill the font", () => _assetManager.CompleteFont(draft.DraftPath, font, scripts, fillFontPath)))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Filled {font.DisplayName} from the base font.";
        }
    }

    private async Task ImportTrueTypeFontAsync(string fontOrZipPath)
    {
        if (_session.Draft is not { } draft || SelectedFont is not { } font)
        {
            return;
        }

        var scripts = SelectedScripts;
        var fillFontPath = FillFontPath;
        if (await RunAsync("Cannot convert font", () => _assetManager.ImportTrueTypeFont(draft.DraftPath, font, fontOrZipPath, null, false, scripts, fillFontPath)))
        {
            _session.NotifyContentChanged();
            lastConvertedFontPath = fontOrZipPath;
            _session.Status = $"Generated console font from {Path.GetFileName(fontOrZipPath)} for {font.DisplayName}.";
        }
    }

    private void NotifyFontCommands()
    {
        ConvertTrueTypeFontCommand.NotifyCanExecuteChanged();
        ReplaceTextureCommand.NotifyCanExecuteChanged();
        ReplaceFontFileCommand.NotifyCanExecuteChanged();
        ExportTextureCommand.NotifyCanExecuteChanged();
        ExportFontFileCommand.NotifyCanExecuteChanged();
        CompleteFontCommand.NotifyCanExecuteChanged();
    }

    private Task<bool> RunAsync(string failure, Action action)
    {
        return _errorHandler.TryRunAsync(failure, () =>
        {
            action();
            return Task.CompletedTask;
        });
    }
}
