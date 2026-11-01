using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The integrity panel: validation issues, textures registered without an image, and images no scene draws.
public sealed partial class ValidationViewModel : ViewModelBase
{
    private readonly ThemeSession _session;
    private readonly IThemeValidator _validator;
    private readonly IThemeAssetManager _assetManager;
    private readonly AudioViewModel _audio;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogs;
    private readonly IErrorHandler _errorHandler;

    public ValidationViewModel(
        ThemeSession session,
        IThemeValidator validator,
        IThemeAssetManager assetManager,
        AudioViewModel audio,
        IDialogService dialogService,
        IFileDialogService fileDialogs,
        IErrorHandler errorHandler)
    {
        _session = session;
        _validator = validator;
        _assetManager = assetManager;
        _audio = audio;
        _dialogService = dialogService;
        _fileDialogs = fileDialogs;
        _errorHandler = errorHandler;
        _session.Reloaded += (_, _) => Reload();
        _audio.OptionsChanged += (_, _) => Run();
    }

    [ObservableProperty]
    private IReadOnlyList<ThemeValidationIssue> validationIssues = [];

    [ObservableProperty]
    private string validationSummary = "Theme not validated yet.";

    [ObservableProperty]
    private bool isThemeValid = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingTextures))]
    private IReadOnlyList<MissingTexture> missingTextures = [];

    [ObservableProperty]
    private MissingTexture? selectedMissingTexture;

    // images of this theme that no scene draws
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnusedImages))]
    private IReadOnlyList<ExtraAsset> unusedImages = [];

    [ObservableProperty]
    private ExtraAsset? selectedUnusedImage;

    public bool HasMissingTextures => MissingTextures.Count > 0;

    public bool HasUnusedImages => UnusedImages.Count > 0;

    [RelayCommand]
    public void Run()
    {
        if (_session.Draft is not { } draft)
        {
            return;
        }

        ThemeValidationResult result = Validate(draft);
        ValidationSummary = result.IsValid
            ? (result.HasWarnings ? "Validation passed with notices." : "Theme is fully valid!")
            : "Validation found errors that must be fixed.";
        _session.Status = ValidationSummary;
    }

    // throws when the theme has validation errors, so nothing broken is saved or exported
    public void EnsureValidFor(string action)
    {
        if (_session.Draft is not { } draft || !Validate(draft).HasErrors)
        {
            return;
        }

        var errors = ValidationIssues.Where(issue => issue.IsError).ToList();
        throw new OperationFailedException($"Fix {errors.Count} validation error(s) before {action}: {errors[0].Message}");
    }

    [RelayCommand]
    private async Task DeleteUnusedImageAsync()
    {
        if (_session.Draft is not { } draft || SelectedUnusedImage is not { IsFromBaseUi: false } image)
        {
            return;
        }

        bool confirmed = await _dialogService.ShowConfirmationAsync(
            "Delete unused image?",
            image.OverridesBase
                ? $"No scene draws resources/{image.RelativePath}. Its replacement is removed and the Base image is kept."
                : $"No scene draws resources/{image.RelativePath}. It is removed from the theme.",
            DialogVariant.Warning,
            "Delete",
            "Keep",
            true);
        if (!confirmed)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync($"Cannot delete {image.DisplayName}", () =>
        {
            _assetManager.DeleteThemeImage(draft.DraftPath, _session.BaseUiPath, image.RelativePath);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = image.OverridesBase
                ? $"Removed the replacement of {image.DisplayName}; the Base image is used again."
                : $"Deleted {image.DisplayName}.";
        }
    }

    [RelayCommand]
    private async Task ProvideMissingTextureAsync()
    {
        if (_session.Draft is not { } draft
            || SelectedMissingTexture is not { } texture
            || await _fileDialogs.OpenFileAsync($"Select PNG for {Path.GetFileName(texture.ImageRelativePath)}", new FileType("PNG image", "*.png")) is not { } pngPath)
        {
            return;
        }

        if (await _errorHandler.TryRunAsync("Cannot add texture", () =>
        {
            _assetManager.ProvideMissingTexture(draft.DraftPath, texture, pngPath);
            return Task.CompletedTask;
        }))
        {
            _session.NotifyContentChanged();
            _session.Status = $"Added {Path.GetFileName(texture.ImageRelativePath)} for texture {texture.TextureId}.";
        }
    }

    private ThemeValidationResult Validate(ThemeDraft draft)
    {
        ThemeValidationResult result = _validator.Validate(draft.DraftPath, _session.BaseUiPath, _audio.ValidationOptions);
        ValidationIssues = result.Issues;
        IsThemeValid = result.IsValid;
        return result;
    }

    private void Reload()
    {
        ThemeResourceReport report = _session.Report;
        MissingTextures = report.MissingTextures;
        SelectedMissingTexture = MissingTextures.Count > 0 ? MissingTextures[0] : null;

        // a Base image nobody draws is not part of the theme, so only the theme's own files are reported
        string? selectedPath = SelectedUnusedImage?.RelativePath;
        UnusedImages = _session.Draft is { } draft
            ? _assetManager.GetExtraAssets(draft.DraftPath, _session.BaseUiPath)
                .Where(asset => !asset.IsFromBaseUi)
                .Select(asset => report.TextureIdOf(asset.RelativePath, asset.TextureId) is var textureId
                    ? asset with { TextureId = textureId, UsedBy = report.UsageOf(textureId) }
                    : asset)
                .Where(asset => asset.UsedBy.Count == 0)
                .ToList()
            : [];
        SelectedUnusedImage = UnusedImages.FirstOrDefault(asset => asset.RelativePath == selectedPath)
            ?? (UnusedImages.Count > 0 ? UnusedImages[0] : null);
    }
}
