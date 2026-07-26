using AtariHacker.Analysis;
using AtariHacker.Helpers;
using AtariHacker.State;

namespace AtariHacker.Tools;

/// <summary>
/// CLI entry point for the detect-patterns command.
/// Scans analyzed code for known control flow patterns.
/// </summary>
public static class PatternDetectionTool
{
    /// <summary>
    /// Detect control flow patterns in the loaded ROM.
    /// </summary>
    public static string DetectPatterns(
        RomSession session,
        string? typeFilter = null,
        string format = "text")
    {
        try
        {
            if (!session.IsLoaded || session.Data is null)
            {
                return "ERROR: No ROM is currently loaded. Use LoadRom first.";
            }

            var result = PatternDetector.DetectAllPatterns(session, typeFilter);

            if (format.ToLowerInvariant() == "csv")
            {
                return FormatPatternsCsv(session);
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Format pattern detection results as CSV.
    /// </summary>
    private static string FormatPatternsCsv(RomSession session)
    {
        if (session.Data is null)
            return string.Empty;

        var graph = DisassemblyAnalyzer.Analyze(session.Data, session.Segments, session.BaseAddress);

        var headers = new[] { "type", "address", "confidence", "detail" };
        var rows = new List<string[]>();

        // State machines
        foreach (var sm in PatternDetector.DetectStateMachines(session.Data, graph))
        {
            rows.Add(new[]
            {
                "state-machine",
                Formatting.HexWord(sm.Address),
                sm.Confidence.ToString("P0"),
                $"state_var={Formatting.HexWord(sm.StateVariable)} table={Formatting.HexWord(sm.JumpTable)}"
            });
        }

        // Jump tables
        foreach (var jt in PatternDetector.DetectJumpTables(session.Data, graph))
        {
            rows.Add(new[]
            {
                "jump-table",
                Formatting.HexWord(jt.Address),
                jt.Confidence.ToString("P0"),
                $"table={Formatting.HexWord(jt.TableAddress)} entries={jt.EntryCount}"
            });
        }

        // Coroutines
        foreach (var co in PatternDetector.DetectCoroutines(graph))
        {
            rows.Add(new[]
            {
                "coroutine",
                Formatting.HexWord(co.Address),
                co.Confidence.ToString("P0"),
                $"chain_length={co.Chain.Count} circular={co.IsCircular}"
            });
        }

        // Interrupt handlers
        foreach (var ih in PatternDetector.DetectInterruptHandlers(session))
        {
            rows.Add(new[]
            {
                "interrupt-handler",
                Formatting.HexWord(ih.Address),
                ih.Confidence.ToString("P0"),
                $"vector={ih.VectorName} ({Formatting.HexWord(ih.VectorAddress)})"
            });
        }

        return OutputFormatter.FormatCsv(headers, rows.ToArray());
    }
}