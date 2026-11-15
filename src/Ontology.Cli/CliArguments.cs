namespace Ontology.Cli;

internal sealed record CliArguments(string Command, string Input, string? Output, bool OpenBrowser, bool AllowPartial)
{
    internal static CliArguments? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("build" or "inspect")) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var noOpen = false;
        var allowPartial = false;
        for (var i = 1; i < args.Length;)
        {
            if (args[i] == "--allow-partial")
            {
                if (allowPartial) return null;
                allowPartial = true;
                i++;
                continue;
            }
            if (args[i] == "--no-open")
            {
                if (noOpen || args[0] != "build") return null;
                noOpen = true;
                i++;
                continue;
            }
            if (i + 1 >= args.Length || args[i] is not ("--input" or "--output") ||
                string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[i + 1])) return null;
            i += 2;
        }
        if (!values.TryGetValue("--input", out var input)) return null;
        values.TryGetValue("--output", out var output);
        if ((args[0] == "build" && output is null) || (args[0] == "inspect" && output is not null)) return null;
        return new(args[0], input, output, !noOpen, allowPartial);
    }
}
