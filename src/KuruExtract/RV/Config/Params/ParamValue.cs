using KuruExtract.RV.IO;

namespace KuruExtract.RV.Config;
internal sealed class ParamValue : ParamEntry
{
    public RawValue Value { get; }

    public ParamValue(RVBinaryReader input)
    {
        var subtype = (ValueType)input.ReadByte();
        Name = input.ReadAsciiz();
        Value = new RawValue(input, subtype);
    }

    public override string ToString(int indentionLevel)
    {
        var comment = Value.WasEscaped ? " // DayZExtract - resolved quote error within value" : string.Empty;
        return $"{Indent(indentionLevel)}{Name} = {Value};{comment}";
    }
}
