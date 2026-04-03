using AgentPowerShell.Platform.Linux;
using AgentPowerShell.Platform.MacOS;
using AgentPowerShell.Platform.Windows;
using Xunit;

namespace AgentPowerShell.Platform.Tests;

public sealed class PlatformSmokeTests
{
    [Fact]
    public void Placeholder_Platform_Adapters_Have_Names()
    {
        Assert.Equal("windows", new WindowsPlatformEnforcer().Name);
        Assert.Equal("linux", new LinuxPlatformEnforcer().Name);
        Assert.Equal("macos", new MacOsPlatformEnforcer().Name);
    }

    [Fact]
    public void Platform_Plans_Enable_Expected_Features()
    {
        var policy = new AgentPowerShell.Core.ExecutionPolicy();

        var windows = WindowsEnforcementPlan.FromPolicy(policy);
        var linux = LinuxEnforcementPlan.FromPolicy(policy);
        var mac = MacOsEnforcementPlan.FromPolicy(policy);

        Assert.True(windows.UseJobObjects);
        Assert.True(linux.UseLandlock);
        Assert.True(mac.UseEndpointSecurity);
    }
}
