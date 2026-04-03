namespace AgentPowerShell.Platform.Windows;

public sealed record ConPtySessionOptions(short Columns = 120, short Rows = 40, bool EnableVirtualTerminal = true);
