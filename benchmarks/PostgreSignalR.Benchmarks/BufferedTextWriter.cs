using System.Text;

namespace PostgreSignalR.Benchmarks;

sealed class BufferedTextWriter(TextWriter inner, StringBuilder buffer) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        inner.Write(value);
        buffer.Append(value);
    }

    public override void Write(string? value)
    {
        inner.Write(value);
        buffer.Append(value);
    }

    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        buffer.Append(value).Append(Environment.NewLine);
    }
}
