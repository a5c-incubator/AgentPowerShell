using System.Text.RegularExpressions;

namespace AgentPowerShell.LlmProxy;

public sealed class DlpRedactor
{
    private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ApiKeyRegex = new(@"(?i)(sk|api|key|token)[-_]?[a-z0-9]{12,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CreditCardRegex = new(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Redact(string input)
    {
        var redacted = EmailRegex.Replace(input, "[REDACTED:email]");
        redacted = ApiKeyRegex.Replace(redacted, "[REDACTED:api_key]");
        redacted = CreditCardRegex.Replace(redacted, "[REDACTED:credit_card]");
        return redacted;
    }
}
