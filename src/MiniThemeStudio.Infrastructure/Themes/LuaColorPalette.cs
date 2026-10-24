using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

/// <summary>
/// Reads and edits the colours of a firmware colors_*.lua, which is stripped LuaJIT 2.0 bytecode.
/// Every colour is a constant template table {r, g, b, a} that a TDUP copies and a TSETS stores under its name,
/// so editing the template's numbers gives the same bytes LuaJIT 2.0.2 compiles from the edited source.
/// </summary>
public sealed class LuaColorPalette
{
    private const byte FlagBigEndian = 0x01;
    private const byte FlagStrip = 0x02;
    private const byte OpTdup = 0x33;
    private const byte OpTsets = 0x3a;

    private readonly byte[] _header;
    private readonly List<Prototype> _prototypes;
    private readonly List<ColorSlot> _slots;

    private LuaColorPalette(byte[] header, List<Prototype> prototypes)
    {
        _header = header;
        _prototypes = prototypes;
        _slots = prototypes.SelectMany(FindColors).ToList();
    }

    public IReadOnlyList<PaletteColor> Colors => _slots.Select(slot => slot.ToColor()).ToList();

    public static LuaColorPalette Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Read(File.ReadAllBytes(filePath));
    }

    public static LuaColorPalette Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var reader = new LuaByteReader(bytes);
        if (bytes.Length < 5 || bytes[0] != 0x1B || bytes[1] != (byte)'L' || bytes[2] != (byte)'J' || bytes[3] != 1)
        {
            throw new InvalidDataException("Not LuaJIT 2.0 bytecode.");
        }

        reader.Position = 4;
        var flags = reader.ReadUleb();
        if ((flags & FlagStrip) == 0 || (flags & FlagBigEndian) != 0)
        {
            throw new InvalidDataException("Only stripped little-endian LuaJIT bytecode is supported.");
        }

        var header = bytes[..reader.Position];
        var prototypes = new List<Prototype>();
        while (true)
        {
            var length = (int)reader.ReadUleb();
            if (length == 0)
            {
                break;
            }

            var end = reader.Position + length;
            prototypes.Add(Prototype.Read(reader));
            if (reader.Position != end)
            {
                throw new InvalidDataException("Prototype length does not match its contents.");
            }
        }

        if (reader.Position != bytes.Length)
        {
            throw new InvalidDataException("Unexpected data after the last prototype.");
        }

        return new LuaColorPalette(header, prototypes);
    }

    public void SetColor(PaletteColor color)
    {
        var slot = _slots.FirstOrDefault(s => s.Name == color.Name)
            ?? throw new KeyNotFoundException($"The palette has no colour named '{color.Name}'.");
        slot.Set(color);
    }

    public byte[] ToBytes()
    {
        var output = new LuaByteWriter();
        output.WriteBytes(_header);
        foreach (var prototype in _prototypes)
        {
            var body = new LuaByteWriter();
            prototype.Write(body);
            output.WriteUleb((uint)body.Length);
            output.WriteBytes(body.ToArray());
        }

        output.WriteByte(0);
        return output.ToArray();
    }

    public void Write(string filePath) => File.WriteAllBytes(filePath, ToBytes());

    private static IEnumerable<ColorSlot> FindColors(Prototype prototype)
    {
        var duplicated = new Dictionary<int, LuaTemplateTable>();
        var seen = new HashSet<LuaTemplateTable>();
        foreach (var instruction in prototype.Instructions)
        {
            var op = (byte)instruction;
            var a = (int)((instruction >> 8) & 0xff);
            if (op == OpTdup)
            {
                if (prototype.ConstantAt((int)(instruction >> 16)) is LuaTemplateTable table)
                {
                    duplicated[a] = table;
                }

                continue;
            }

            if (op == OpTsets && duplicated.TryGetValue(a, out var template) && template.IsColor &&
                prototype.ConstantAt((int)((instruction >> 16) & 0xff)) is LuaStringConstant key && seen.Add(template))
            {
                yield return new ColorSlot(key.Text, template);
            }
            else if (op != OpTsets)
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

    private sealed class Prototype
    {
        private byte _flags;
        private byte _parameterCount;
        private byte _frameSize;
        private byte _upvalueCount;
        private byte[] _bytecode = [];
        private byte[] _upvalues = [];
        private List<LuaConstant> _constants = [];
        private int _numberCount;
        private byte[] _numbers = [];

        public uint[] Instructions { get; private set; } = [];

        // bytecode addresses GC constants from the end: D = 0 is the last one
        public LuaConstant ConstantAt(int index) => _constants[_constants.Count - 1 - index];

        public static Prototype Read(LuaByteReader reader)
        {
            var prototype = new Prototype
            {
                _flags = reader.ReadByte(),
                _parameterCount = reader.ReadByte(),
                _frameSize = reader.ReadByte(),
                _upvalueCount = reader.ReadByte(),
            };
            var constantCount = (int)reader.ReadUleb();
            prototype._numberCount = (int)reader.ReadUleb();
            var instructionCount = (int)reader.ReadUleb();

            prototype._bytecode = reader.ReadBytes(instructionCount * 4);
            prototype.Instructions = Enumerable.Range(0, instructionCount)
                .Select(i => BitConverter.ToUInt32(prototype._bytecode, i * 4))
                .ToArray();
            prototype._upvalues = reader.ReadBytes(prototype._upvalueCount * 2);

            for (var i = 0; i < constantCount; i++)
            {
                prototype._constants.Add(LuaConstant.Read(reader));
            }

            var numbersStart = reader.Position;
            for (var i = 0; i < prototype._numberCount; i++)
            {
                if (reader.ReadUleb33IsNumber())
                {
                    reader.ReadUleb();
                }
            }

            prototype._numbers = reader.Bytes[numbersStart..reader.Position];
            return prototype;
        }

        public void Write(LuaByteWriter writer)
        {
            writer.WriteByte(_flags);
            writer.WriteByte(_parameterCount);
            writer.WriteByte(_frameSize);
            writer.WriteByte(_upvalueCount);
            writer.WriteUleb((uint)_constants.Count);
            writer.WriteUleb((uint)_numberCount);
            writer.WriteUleb((uint)Instructions.Length);
            writer.WriteBytes(_bytecode);
            writer.WriteBytes(_upvalues);
            foreach (var constant in _constants)
            {
                constant.Write(writer);
            }

            writer.WriteBytes(_numbers);
        }
    }
}

internal abstract class LuaConstant
{
    private const uint KgcTable = 1;
    private const uint KgcString = 5;

    public static LuaConstant Read(LuaByteReader reader)
    {
        var type = reader.ReadUleb();
        if (type >= KgcString)
        {
            return new LuaStringConstant(reader.ReadBytes((int)(type - KgcString)));
        }

        if (type == KgcTable)
        {
            return LuaTemplateTable.ReadBody(reader);
        }

        // child prototypes and 64-bit/complex cdata never appear in a palette
        throw new InvalidDataException($"Unsupported LuaJIT constant type {type}.");
    }

    public abstract void Write(LuaByteWriter writer);
}

internal sealed class LuaStringConstant(byte[] bytes) : LuaConstant
{
    public string Text { get; } = Encoding.UTF8.GetString(bytes);

    public override void Write(LuaByteWriter writer)
    {
        writer.WriteUleb((uint)bytes.Length + 5);
        writer.WriteBytes(bytes);
    }
}

internal sealed class LuaTemplateTable : LuaConstant
{
    public List<LuaTableValue> Array { get; } = [];

    public List<(LuaTableValue Key, LuaTableValue Value)> Hash { get; } = [];

    // {r, g, b, a}: slot 0 is nil in a LuaJIT template's array part
    public bool IsColor =>
        Hash.Count == 0 && Array.Count == 5 && Array[0].Kind == LuaTableValueKind.Nil &&
        Array.Skip(1).All(v => v.Kind is LuaTableValueKind.Integer or LuaTableValueKind.Number);

    public static LuaTemplateTable ReadBody(LuaByteReader reader)
    {
        var table = new LuaTemplateTable();
        var arrayCount = (int)reader.ReadUleb();
        var hashCount = (int)reader.ReadUleb();
        for (var i = 0; i < arrayCount; i++)
        {
            table.Array.Add(LuaTableValue.Read(reader));
        }

        for (var i = 0; i < hashCount; i++)
        {
            table.Hash.Add((LuaTableValue.Read(reader), LuaTableValue.Read(reader)));
        }

        return table;
    }

    public override void Write(LuaByteWriter writer)
    {
        writer.WriteUleb(1);
        writer.WriteUleb((uint)Array.Count);
        writer.WriteUleb((uint)Hash.Count);
        foreach (var value in Array)
        {
            value.Write(writer);
        }

        foreach (var (key, value) in Hash)
        {
            key.Write(writer);
            value.Write(writer);
        }
    }
}

internal enum LuaTableValueKind
{
    Nil,
    False,
    True,
    Integer,
    Number,
    String,
}

internal readonly record struct LuaTableValue(LuaTableValueKind Kind, uint Integer, ulong Bits, byte[]? Text)
{
    public double Number => Kind == LuaTableValueKind.Integer ? (int)Integer : BitConverter.UInt64BitsToDouble(Bits);

    // LuaJIT narrows every number that fits an int32 to an integer constant
    public static LuaTableValue FromNumber(double value)
    {
        var narrowed = (int)value;
        return value == narrowed && value is >= int.MinValue and <= int.MaxValue
            ? new LuaTableValue(LuaTableValueKind.Integer, (uint)narrowed, 0, null)
            : new LuaTableValue(LuaTableValueKind.Number, 0, BitConverter.DoubleToUInt64Bits(value), null);
    }

    public static LuaTableValue Read(LuaByteReader reader)
    {
        var type = reader.ReadUleb();
        switch (type)
        {
            case 0: return new LuaTableValue(LuaTableValueKind.Nil, 0, 0, null);
            case 1: return new LuaTableValue(LuaTableValueKind.False, 0, 0, null);
            case 2: return new LuaTableValue(LuaTableValueKind.True, 0, 0, null);
            case 3: return new LuaTableValue(LuaTableValueKind.Integer, reader.ReadUleb(), 0, null);
            case 4:
                var low = (ulong)reader.ReadUleb();
                var high = (ulong)reader.ReadUleb();
                return new LuaTableValue(LuaTableValueKind.Number, 0, low | (high << 32), null);
            default:
                return new LuaTableValue(LuaTableValueKind.String, 0, 0, reader.ReadBytes((int)(type - 5)));
        }
    }

    public void Write(LuaByteWriter writer)
    {
        switch (Kind)
        {
            case LuaTableValueKind.Nil: writer.WriteUleb(0); break;
            case LuaTableValueKind.False: writer.WriteUleb(1); break;
            case LuaTableValueKind.True: writer.WriteUleb(2); break;
            case LuaTableValueKind.Integer:
                writer.WriteUleb(3);
                writer.WriteUleb(Integer);
                break;
            case LuaTableValueKind.Number:
                writer.WriteUleb(4);
                writer.WriteUleb((uint)Bits);
                writer.WriteUleb((uint)(Bits >> 32));
                break;
            default:
                writer.WriteUleb((uint)Text!.Length + 5);
                writer.WriteBytes(Text);
                break;
        }
    }
}

internal sealed class LuaByteReader(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;

    public int Position { get; set; }

    public byte ReadByte()
    {
        if (Position >= Bytes.Length)
        {
            throw new InvalidDataException("Unexpected end of LuaJIT bytecode.");
        }

        return Bytes[Position++];
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || Position + count > Bytes.Length)
        {
            throw new InvalidDataException("Unexpected end of LuaJIT bytecode.");
        }

        var result = Bytes[Position..(Position + count)];
        Position += count;
        return result;
    }

    public uint ReadUleb()
    {
        uint value = 0;
        var shift = 0;
        byte current;
        do
        {
            current = ReadByte();
            value |= (uint)(current & 0x7f) << shift;
            shift += 7;
        }
        while (current >= 0x80);

        return value;
    }

    // the 33-bit form of number constants: bit 0 of the first byte tells a double from an integer
    public bool ReadUleb33IsNumber()
    {
        var first = ReadByte();
        var current = first;
        while (current >= 0x80)
        {
            current = ReadByte();
        }

        return (first & 1) != 0;
    }
}

internal sealed class LuaByteWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public void WriteByte(byte value) => _bytes.Add(value);

    public void WriteBytes(byte[] values) => _bytes.AddRange(values);

    public void WriteUleb(uint value)
    {
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            _bytes.Add(current);
        }
        while (value != 0);
    }

    public byte[] ToArray() => [.. _bytes];
}
