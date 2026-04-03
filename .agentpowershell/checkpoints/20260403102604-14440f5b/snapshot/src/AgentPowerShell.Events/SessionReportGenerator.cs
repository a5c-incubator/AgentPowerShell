using System.Text;
using System.Globalization;

namespace AgentPowerShell.Events;

public sealed class SessionReportGenerator
{
    public SessionReport Generate(string sessionId, IEnumerable<EventRecord> events)
    {
        var timeline = events
            .Where(record => string.Equals(record.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.Timestamp)
            .ToArray();

        var findings = DetectFindings(timeline);
        var counts = timeline
            .GroupBy(record => record.EventType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new SessionReport(sessionId, DateTimeOffset.UtcNow, timeline, findings, counts);
    }

    public string RenderMarkdown(SessionReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"# Session Report: {report.SessionId}"));
        builder.AppendLine();
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Generated: {report.GeneratedAt:O}"));
        builder.AppendLine();
        builder.AppendLine("## Summary");
        foreach (var count in report.EventCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {count.Key}: {count.Value}"));
        }

        builder.AppendLine();
        builder.AppendLine("## Findings");
        if (report.Findings.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (var finding in report.Findings)
            {
                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- [{finding.Severity}] {finding.Message}"));
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Timeline");
        foreach (var record in report.Timeline)
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {record.Timestamp:O} `{record.EventType}`"));
        }

        return builder.ToString();
    }

    private static List<ReportFinding> DetectFindings(IEnumerable<EventRecord> timeline)
    {
        var findings = new List<ReportFinding>();
        foreach (var record in timeline)
        {
            switch (record)
            {
                case FileEvent fileEvent when string.Equals(fileEvent.Decision, "deny", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new ReportFinding("high", $"Denied file operation on {fileEvent.Path}."));
                    break;
                case ApprovalEvent approvalEvent when !approvalEvent.Approved:
                    findings.Add(new ReportFinding("medium", $"Approval denied via {approvalEvent.Mode}: {approvalEvent.Reason}"));
                    break;
                case NetworkEvent networkEvent when networkEvent.Port is 22 or 3389:
                    findings.Add(new ReportFinding("medium", $"Sensitive outbound connection to port {networkEvent.Port}."));
                    break;
            }
        }

        return findings;
    }
}
