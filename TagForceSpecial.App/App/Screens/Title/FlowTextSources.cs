namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Named original byte sources ("eboot", "title-menu", ...) that measured text runs read from. Strings stay in the private import.</summary>
public sealed class FlowTextSources
{
    private readonly Dictionary<string, byte[]> sources = new(StringComparer.Ordinal);

    public FlowTextSources Add(string name, byte[] bytes) { sources.Add(name, bytes); return this; }

    public string Read(string source, int offset, int start, int chars) => TitleFlowText.Read(
        sources.GetValueOrDefault(source) ?? throw new InvalidDataException("Unknown or missing text source " + source + "."), offset, start, chars);

    public string Read(FlowText run) => Read(run.Source, run.Offset, run.Start, run.Chars);

    public string Read(FlowTextSource reference) => Read(reference.Source, reference.Offset, reference.Start, reference.Chars);
}
