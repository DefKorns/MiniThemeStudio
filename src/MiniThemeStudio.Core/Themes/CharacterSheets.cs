namespace MiniThemeStudio.Core.Themes;

// One picture of a character sheet: Row counts the rows of the sheet (on the NES, 0 is Mario, 1 Luigi and 2 the
// "?" block) and Index the pictures of the row, from the left.
public sealed record CharacterSheetFrame(int Row, int Index, int X, int Y, int Width, int Height);

// The sheet the NES and Famicom UIs draw their characters from (resources/sprites/characters_sbm.png). A script
// cuts the pictures at fixed places, so a new picture keeps the place and the size of the one it replaces.
public sealed record CharacterSheet(string ImagePath, string RelativePath, int Width, int Height, bool IsFromBaseUi, IReadOnlyList<CharacterSheetFrame> Frames);

public interface ICharacterSheetEditor
{
    // null when the UI draws its characters from scene animations instead (the SNES ones)
    CharacterSheet? GetSheet(string themePath, string? baseUiPath);

    // draws the picture over the frame, in the theme's copy of the sheet; the picture has the frame's size
    void ReplaceFrame(string themeDraftPath, string? baseUiPath, CharacterSheetFrame frame, string pictureImagePath);

    // the whole sheet, edited elsewhere; it has the size of the one it replaces
    void ReplaceSheet(string themeDraftPath, string? baseUiPath, string sheetImagePath);
}
