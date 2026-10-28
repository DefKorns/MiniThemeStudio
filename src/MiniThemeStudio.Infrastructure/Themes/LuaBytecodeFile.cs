using System.Text;

namespace MiniThemeStudio.Infrastructure.Themes;

// A stripped little-endian LuaJIT 2.0 bytecode file (the format the minis load, version 1): its header and its
// prototypes, written back as LuaJIT lays them out. Editing the numbers of a constant template table gives the
// bytes LuaJIT 2.0 compiles from the source with those numbers.
internal sealed class LuaBytecodeFile
{
    private const byte FlagBigEndian = 0x01;
    private const byte FlagStrip = 0x02;

    private readonly byte[] _header;

    private LuaBytecodeFile(byte[] header, List<LuaPrototype> prototypes)
    {
        _header = header;
        Prototypes = prototypes;
    }

    // in the order of the file: a function's children come before it, the main chunk last
    public IReadOnlyList<LuaPrototype> Prototypes { get; }

    public static LuaBytecodeFile Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var reader = new LuaByteReader(bytes);
        if (bytes.Length < 5 || bytes[0] != 0x1B || bytes[1] != (byte)'L' || bytes[2] != (byte)'J' || bytes[3] != 1)
        {
            throw new InvalidDataException("Not LuaJIT 2.0 bytecode.");
        }

        reader.Position = 4;
        uint flags = reader.ReadUleb();
        if ((flags & FlagStrip) == 0 || (flags & FlagBigEndian) != 0)
        {
            throw new InvalidDataException("Only stripped little-endian LuaJIT bytecode is supported.");
        }

        byte[] header = bytes[..reader.Position];
        var prototypes = new List<LuaPrototype>();
        while (true)
        {
            int length = (int)reader.ReadUleb();
            if (length == 0)
            {
                break;
            }

            int end = reader.Position + length;
            prototypes.Add(LuaPrototype.Read(reader));
            if (reader.Position != end)
            {
                throw new InvalidDataException("Prototype length does not match its contents.");
            }
        }

        if (reader.Position != bytes.Length)
        {
            throw new InvalidDataException("Unexpected data after the last prototype.");
        }

        return new LuaBytecodeFile(header, prototypes);
    }

    public byte[] ToBytes()
    {
        var output = new LuaByteWriter();
        output.WriteBytes(_header);
        foreach (LuaPrototype prototype in Prototypes)
        {
            var body = new LuaByteWriter();
            prototype.Write(body);
            output.WriteUleb((uint)body.Length);
            output.WriteBytes(body.ToArray());
        }

        output.WriteByte(0);
        return output.ToArray();
    }
}

internal sealed class LuaPrototype
{
    public const byte OpTdup = 0x33;
    public const byte OpTsets = 0x3a;
    public const byte OpTsetb = 0x3c;
    public const byte OpTnew = 0x32;

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

    public static LuaPrototype Read(LuaByteReader reader)
    {
        var prototype = new LuaPrototype
        {
            _flags = reader.ReadByte(),
            _parameterCount = reader.ReadByte(),
            _frameSize = reader.ReadByte(),
            _upvalueCount = reader.ReadByte(),
        };
        int constantCount = (int)reader.ReadUleb();
        prototype._numberCount = (int)reader.ReadUleb();
        int instructionCount = (int)reader.ReadUleb();

        prototype._bytecode = reader.ReadBytes(instructionCount * 4);
        prototype.Instructions = Enumerable.Range(0, instructionCount)
            .Select(i => BitConverter.ToUInt32(prototype._bytecode, i * 4))
            .ToArray();
        prototype._upvalues = reader.ReadBytes(prototype._upvalueCount * 2);

        for (int i = 0; i < constantCount; i++)
        {
            prototype._constants.Add(LuaConstant.Read(reader));
        }

        int numbersStart = reader.Position;
        for (int i = 0; i < prototype._numberCount; i++)
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
        foreach (LuaConstant constant in _constants)
        {
            constant.Write(writer);
        }

        writer.WriteBytes(_numbers);
    }
}

internal abstract class LuaConstant
{
    private const uint KgcChild = 0;
    private const uint KgcTable = 1;
    private const uint KgcString = 5;

    public static LuaConstant Read(LuaByteReader reader)
    {
        uint type = reader.ReadUleb();
        if (type >= KgcString)
        {
            return new LuaStringConstant(reader.ReadBytes((int)(type - KgcString)));
        }

        if (type == KgcTable)
        {
            return LuaTemplateTable.ReadBody(reader);
        }

        if (type == KgcChild)
        {
            return new LuaChildConstant();
        }

        // 64-bit and complex cdata numbers never appear in the menu's scripts
        throw new InvalidDataException($"Unsupported LuaJIT constant type {type}.");
    }

    public abstract void Write(LuaByteWriter writer);
}

// a function of the chunk: the prototype read just before it
internal sealed class LuaChildConstant : LuaConstant
{
    public override void Write(LuaByteWriter writer) => writer.WriteUleb(0);
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
        int arrayCount = (int)reader.ReadUleb();
        int hashCount = (int)reader.ReadUleb();
        for (int i = 0; i < arrayCount; i++)
        {
            table.Array.Add(LuaTableValue.Read(reader));
        }

        for (int i = 0; i < hashCount; i++)
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
        foreach (LuaTableValue value in Array)
        {
            value.Write(writer);
        }

        foreach ((LuaTableValue key, LuaTableValue value) in Hash)
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
        int narrowed = (int)value;
        return value == narrowed && value is >= int.MinValue and <= int.MaxValue
            ? new LuaTableValue(LuaTableValueKind.Integer, (uint)narrowed, 0, null)
            : new LuaTableValue(LuaTableValueKind.Number, 0, BitConverter.DoubleToUInt64Bits(value), null);
    }

    public static LuaTableValue Read(LuaByteReader reader)
    {
        uint type = reader.ReadUleb();
        switch (type)
        {
            case 0: return new LuaTableValue(LuaTableValueKind.Nil, 0, 0, null);
            case 1: return new LuaTableValue(LuaTableValueKind.False, 0, 0, null);
            case 2: return new LuaTableValue(LuaTableValueKind.True, 0, 0, null);
            case 3: return new LuaTableValue(LuaTableValueKind.Integer, reader.ReadUleb(), 0, null);
            case 4:
                ulong low = (ulong)reader.ReadUleb();
                ulong high = (ulong)reader.ReadUleb();
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

        byte[] result = Bytes[Position..(Position + count)];
        Position += count;
        return result;
    }

    public uint ReadUleb()
    {
        uint value = 0;
        int shift = 0;
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
        byte first = ReadByte();
        byte current = first;
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
            byte current = (byte)(value & 0x7f);
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
