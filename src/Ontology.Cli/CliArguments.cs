namespace Ontology.Cli;

internal sealed record CliArguments(string Command, string Input, string? Output)
{
    internal static CliArguments? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("build" or "inspect")) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--input" or "--output") ||
                string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[i + 1])) return null;
        }
        if (!values.TryGetValue("--input", out var input)) return null;
        values.TryGetValue("--output", out var output);
        if ((args[0] == "build" && output is null) || (args[0] == "inspect" && output is not null)) return null;
        return new(args[0], input, output);
    }
}
