using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

/// <summary>
/// Reads and edits the colours of a firmware colors_*.lua, which is stripped LuaJIT 2.0 bytecode.
/// Every colour is a constant template table {r, g, b, a} that a TDUP copies and a TSETS stores under its name,
/// so editing the template's numbers gives the same bytes LuaJIT 2.0.2 compiles from the edited source.
/// </summary>
public sealed class LuaColorPalette
{
    private readonly LuaBytecodeFile _file;
    private readonly List<ColorSlot> _slots;

    private LuaColorPalette(LuaBytecodeFile file)
    {
        _file = file;
        _slots = file.Prototypes.SelectMany(FindColors).ToList();
    }

    public IReadOnlyList<PaletteColor> Colors => _slots.Select(slot => slot.ToColor()).ToList();

    public static LuaColorPalette Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Read(File.ReadAllBytes(filePath));
    }

    public static LuaColorPalette Read(byte[] bytes) => new(LuaBytecodeFile.Read(bytes));

    public void SetColor(PaletteColor color)
    {
        var slot = _slots.FirstOrDefault(s => s.Name == color.Name)
            ?? throw new KeyNotFoundException($"The palette has no colour named '{color.Name}'.");
        slot.Set(color);
    }

    public byte[] ToBytes() => _file.ToBytes();

    public void Write(string filePath) => File.WriteAllBytes(filePath, ToBytes());

    private static IEnumerable<ColorSlot> FindColors(LuaPrototype prototype)
    {
        var duplicated = new Dictionary<int, LuaTemplateTable>();
        var seen = new HashSet<LuaTemplateTable>();
        foreach (var instruction in prototype.Instructions)
        {
            var op = (byte)instruction;
            var a = (int)((instruction >> 8) & 0xff);
            if (op == LuaPrototype.OpTdup)
            {
                if (prototype.ConstantAt((int)(instruction >> 16)) is LuaTemplateTable table)
                {
                    duplicated[a] = table;
                }

                continue;
            }

            if (op == LuaPrototype.OpTsets && duplicated.TryGetValue(a, out var template) && template.IsColor &&
                prototype.ConstantAt((int)((instruction >> 16) & 0xff)) is LuaStringConstant key && seen.Add(template))
            {
                yield return new ColorSlot(key.Text, template);
            }
            else if (op != LuaPrototype.OpTsets)
            {
                duplicated.Remove(a);
            }
        }
    }

    private sealed class ColorSlot(string name, LuaTemplateTable table)
    {
        public string Name { get; } = name;

        public PaletteColor ToColor() =>
            new(Name, table.Array[1].Number, table.Array[2].Number, table.Array[3].Number, table.Array[4].Number);

        public void Set(PaletteColor color)
        {
            table.Array[1] = LuaTableValue.FromNumber(color.Red);
            table.Array[2] = LuaTableValue.FromNumber(color.Green);
            table.Array[3] = LuaTableValue.FromNumber(color.Blue);
            table.Array[4] = LuaTableValue.FromNumber(color.Alpha);
        }
    }
}
