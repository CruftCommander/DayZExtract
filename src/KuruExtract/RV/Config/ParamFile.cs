using KuruExtract.RV.IO;

namespace KuruExtract.RV.Config;
internal sealed class ParamFile
{
    public ParamClass? Root { get; private set; }

    public ParamFile(Stream stream)
    {
        Read(new RVBinaryReader(stream));
    }

    public void Read(RVBinaryReader input)
    {
        ReadOnlySpan<byte> sig = [0x00, 0x72, 0x61, 0x50];
        if (!input.ReadBytes(4).AsSpan().SequenceEqual(sig))
            throw new FormatException();

        _ = input.ReadInt32();
        _ = input.ReadInt32();
        _ = input.ReadInt32();

        Root = ParamClass.ReadRoot(input, "rootClass");
    }

    public override string? ToString()
    {
        return Root?.ToString(0, true);
    }
}
