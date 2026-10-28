using System.Text;

namespace MiniThemeStudio.Infrastructure.Themes;

// The characters of sys_wallpaper_fg0.lua, the script that moves Mario, Luigi and the ? block over the NES and
// Famicom menus. Each one is a constant table, smb_mario = { sizew = 16, sizeh = 16, tex_variable_name = "smb_tex",
// pattern = { {0, 1}, {18, 1}, ... } }: the size its pictures are cut and drawn at, and where each picture sits on
// the texture, in pixels. LuaJIT 2.0 keeps those numbers in template tables, so they are rewritten in place.
internal sealed class LuaCharacterPatterns
{
    private readonly LuaBytecodeFile _file;
    private readonly List<Slot> _slots;

    private LuaCharacterPatterns(LuaBytecodeFile file)
    {
        _file = file;
        _slots = file.Prototypes.SelectMany(FindCharacters).ToList();
    }

    public IReadOnlyList<LuaCharacter> Characters => _slots.Select(slot => slot.ToCharacter()).ToList();

    public static LuaCharacterPatterns Read(byte[] bytes) => new(LuaBytecodeFile.Read(bytes));

    public void Set(string name, int width, int height, IReadOnlyList<(int X, int Y)> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var slot = _slots.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new KeyNotFoundException($"The script has no character named '{name}'.");
        if (frames.Count != slot.Frames.Count)
        {
            throw new ArgumentException($"{name} has {slot.Frames.Count} pictures, not {frames.Count}.", nameof(frames));
        }

        slot.Set(width, height, frames);
    }

    public byte[] ToBytes() => _file.ToBytes();

    // TDUP of the character's template, then a TDUP of each {x, y} of its pattern, then the TSETS that names it
    private static IEnumerable<Slot> FindCharacters(LuaPrototype prototype)
    {
        Slot? current = null;
        foreach (var instruction in prototype.Instructions)
        {
            var op = (byte)instruction;
            var a = (int)((instruction >> 8) & 0xff);
            if (op == LuaPrototype.OpTdup && prototype.ConstantAt((int)(instruction >> 16)) is LuaTemplateTable table)
            {
                if (Slot.IsCharacter(table))
                {
                    current = new Slot(table, a);
                }
                else if (current is not null && Slot.IsPosition(table))
                {
                    current.Frames.Add(table);
                }
            }
            else if (op == LuaPrototype.OpTsets && current is not null && a == current.Register
                && prototype.ConstantAt((int)((instruction >> 16) & 0xff)) is LuaStringConstant key)
            {
                current.Name = key.Text;
                yield return current;
                current = null;
            }
        }
    }

    private sealed class Slot(LuaTemplateTable table, int register)
    {
        public int Register { get; } = register;

        public string Name { get; set; } = "";

        public List<LuaTemplateTable> Frames { get; } = [];

        public static bool IsCharacter(LuaTemplateTable table) =>
            Find(table, "sizew") is not null && Find(table, "sizeh") is not null && Find(table, "tex_variable_name") is not null;

        // {x, y}: slot 0 of a template's array part is nil
        public static bool IsPosition(LuaTemplateTable table) =>
            table.Hash.Count == 0 && table.Array.Count == 3 && table.Array[0].Kind == LuaTableValueKind.Nil
            && table.Array[1].Kind == LuaTableValueKind.Integer && table.Array[2].Kind == LuaTableValueKind.Integer;

        public LuaCharacter ToCharacter() => new(
            Name,
            Encoding.UTF8.GetString(Find(table, "tex_variable_name")!.Value.Text ?? []),
            (int)Find(table, "sizew")!.Value.Number,
            (int)Find(table, "sizeh")!.Value.Number,
            Frames.Select(frame => ((int)frame.Array[1].Number, (int)frame.Array[2].Number)).ToList());

        public void Set(int width, int height, IReadOnlyList<(int X, int Y)> frames)
        {
            Replace("sizew", width);
            Replace("sizeh", height);
            for (var index = 0; index < frames.Count; index++)
            {
                Frames[index].Array[1] = LuaTableValue.FromNumber(frames[index].X);
                Frames[index].Array[2] = LuaTableValue.FromNumber(frames[index].Y);
            }
        }

        private void Replace(string key, int value)
        {
            var index = table.Hash.FindIndex(entry => IsKey(entry.Key, key));
            table.Hash[index] = (table.Hash[index].Key, LuaTableValue.FromNumber(value));
        }

        private static LuaTableValue? Find(LuaTemplateTable table, string key) =>
            table.Hash.Where(entry => IsKey(entry.Key, key)).Select(entry => (LuaTableValue?)entry.Value).FirstOrDefault();

        private static bool IsKey(LuaTableValue value, string key) =>
            value.Kind == LuaTableValueKind.String && Encoding.UTF8.GetString(value.Text ?? []) == key;
    }
}

// one character of the script: Texture names the texture property of the node it is drawn from (smb_tex)
internal sealed record LuaCharacter(string Name, string Texture, int Width, int Height, IReadOnlyList<(int X, int Y)> Frames);
