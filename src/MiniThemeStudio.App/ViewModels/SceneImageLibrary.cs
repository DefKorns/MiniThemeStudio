using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniThemeStudio.App.Controls;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The images scene previews draw (the textures and atlas sprites of the theme over its Base), loaded once per
// reload of the theme and shared by every tab that previews a scene. It reloads before those tabs do: they
// depend on it, so it subscribes to the session first.
public sealed class SceneImageLibrary : IDisposable
{
    private readonly ThemeSession _session;
    private readonly ITextureAtlasReader _atlasReader;

    public SceneImageLibrary(ThemeSession session, ITextureAtlasReader atlasReader)
    {
        _session = session;
        _atlasReader = atlasReader;
        _session.Reloaded += (_, _) => Reload();
    }

    public SceneImageSource Images { get; private set; } = SceneImageSource.Empty;

    // the textures a scene node can draw, by name
    public IReadOnlyList<SceneTextureChoice> TextureChoices { get; private set; } = [];

    // the image file of each texture id
    public IReadOnlyDictionary<string, string> TextureImagePaths { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public void Dispose()
    {
        Release(Images);
        Images = SceneImageSource.Empty;
    }

    private void Reload()
    {
        SceneImageSource previous = Images;
        if (_session.Draft is not { } draft)
        {
            TextureChoices = [];
            TextureImagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            Images = SceneImageSource.Empty;
            Release(previous);
            return;
        }

        var imagePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var choices = new List<SceneTextureChoice>();
        foreach ((string? relativePath, string? textureId) in _session.Report.TextureIdsByImagePath)
        {
            string? imagePath = new[] { draft.DraftPath, _session.BaseUiPath }
                .Where(root => root is not null)
                .Select(root => Path.Combine(root!, "resources", relativePath))
                .FirstOrDefault(File.Exists);
            if (imagePath is null)
            {
                continue;
            }

            imagePaths.TryAdd(textureId, imagePath);
            choices.Add(new SceneTextureChoice(textureId, Path.GetFileNameWithoutExtension(relativePath), relativePath));
        }

        TextureChoices = choices.OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase).ToList();
        TextureImagePaths = imagePaths;
        Images = new SceneImageSource(imagePaths, _atlasReader.ReadAll(draft.DraftPath, _session.BaseUiPath));

        // the previews are drawn again only after every tab has taken the new images
        Release(previous);
    }

    private static void Release(SceneImageSource images)
    {
        if (!ReferenceEquals(images, SceneImageSource.Empty))
        {
            images.Dispose();
        }
    }
}
