namespace MiniThemeStudio.Core.Themes;

// One picture of a character sheet: Row counts the rows of the sheet (on the NES, 0 is Mario, 1 Luigi and 2 the
// "?" block) and Index the pictures of the row, from the left. X, Y, Width and Height frame the picture of the
// Base; the slot around it is the room a new picture has, the 16 x 16 of a NES sprite where the neighbours leave it.
public sealed record CharacterSheetFrame(int Row, int Index, int X, int Y, int Width, int Height, int SlotX, int SlotY, int SlotWidth, int SlotHeight);

// A sheet the NES and Famicom UIs draw characters from: characters_sbm (Mario, Luigi and the ? block of the
// menu) and wallpaper-hvc (the small ones walking over the background). A script cuts the pictures at fixed
// places, so a new picture keeps the place and the size of the one it replaces.
// RowNames names the rows the script draws (Mario, Luigi, Block); CanResize tells that script sets their size, so
// a row can be given larger frames.
public sealed record CharacterSheet(
    string Name,
    string ImagePath,
    string RelativePath,
    int Width,
    int Height,
    bool IsFromBaseUi,
    IReadOnlyList<CharacterSheetFrame> Frames,
    IReadOnlyList<string> RowNames,
    bool CanResize);

public interface ICharacterSheetEditor
{
    // none when the UI draws its characters from scene animations instead (the SNES ones)
    IReadOnlyList<CharacterSheet> GetSheets(string themePath, string? baseUiPath);

    // draws the picture in the frame's slot of the theme's copy of the sheet, centred where the Base picture was and
    // standing on its bottom line. Its transparent edges are dropped, and a picture larger than the slot is made
    // smaller: by its own pixel size when it is enlarged pixel art, then to fit. Returns the scale it was drawn at.
    double ReplaceFrame(string themeDraftPath, string? baseUiPath, CharacterSheet sheet, CharacterSheetFrame frame, string pictureImagePath);

    // the whole sheet, edited elsewhere; it has the size of the one it replaces
    void ReplaceSheet(string themeDraftPath, string? baseUiPath, CharacterSheet sheet, string sheetImagePath);

    // the size of the picture without its transparent edges
    (int Width, int Height) MeasurePicture(string pictureImagePath);

    // gives every frame of the row the new size (never smaller than now): the sheet is laid out again with the
    // pictures it has, and the script that cuts them is rewritten to match
    void ResizeRow(string themeDraftPath, string? baseUiPath, CharacterSheet sheet, int row, int width, int height);
}
