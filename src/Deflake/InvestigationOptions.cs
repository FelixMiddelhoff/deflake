using System.Collections.Generic;

namespace Deflake;

internal sealed class InvestigationOptions
{
    public List<string> Tests { get; set; } = new();
    public string? FromTrx { get; set; }
    public int Runs { get; set; } = 20;
    public int MaxRuns { get; set; } = 20;
    public int TimeoutMinutes { get; set; } = 60;
    public string? Framework { get; set; }
    public string Configuration { get; set; } = "Debug";
    public bool NoBuild { get; set; }
    public string? ReportDir { get; set; }
    public string Format { get; set; } = "text";
    public bool RecordReplay { get; set; }
}
