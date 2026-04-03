namespace AgentPowerShell.Platform.MacOS;

public sealed record LaunchdPlist(string Label, string Program, string WorkingDirectory)
{
    public string Render() =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{{Label}}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{{Program}}</string>
          </array>
          <key>WorkingDirectory</key>
          <string>{{WorkingDirectory}}</string>
          <key>RunAtLoad</key>
          <true/>
        </dict>
        </plist>
        """;
}
