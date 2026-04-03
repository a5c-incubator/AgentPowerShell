using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentPowerShell.Core;

public sealed class PolicyLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public ExecutionPolicy LoadFromFile(string path) =>
        LoadFromYaml(File.ReadAllText(path));

    public ExecutionPolicy LoadFromYaml(string yaml)
    {
        var policy = _deserializer.Deserialize<ExecutionPolicy>(yaml) ?? new ExecutionPolicy();
        return Normalize(policy);
    }

    private static ExecutionPolicy Normalize(ExecutionPolicy policy) =>
        policy with
        {
            FileRules = policy.FileRules ?? [],
            CommandRules = policy.CommandRules ?? [],
            NetworkRules = policy.NetworkRules ?? [],
            EnvRules = policy.EnvRules ?? []
        };
}
