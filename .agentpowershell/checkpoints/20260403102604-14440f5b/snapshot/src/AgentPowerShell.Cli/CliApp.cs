using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using AgentPowerShell.Core;
using AgentPowerShell.Daemon;

namespace AgentPowerShell.Cli;

public static class CliApp
{
    public static int Run(string[] args) => BuildRootCommand().Invoke(args);

    public static RootCommand BuildRootCommand()
    {
        var outputOption = new Option<string>("--output", () => "text");
        var eventsOption = new Option<string>("--events", () => "summary");

        var root = new RootCommand("AgentPowerShell CLI");
        root.AddGlobalOption(outputOption);
        root.AddGlobalOption(eventsOption);
        root.AddCommand(BuildVersionCommand());
        root.AddCommand(BuildExecCommand(outputOption));
        root.AddCommand(BuildStartCommand(outputOption));
        root.AddCommand(BuildStopCommand(outputOption));
        root.AddCommand(BuildSessionCommand(outputOption));
        root.AddCommand(BuildPolicyCommand(outputOption));
        root.AddCommand(BuildReportCommand(outputOption, eventsOption));
        root.AddCommand(BuildStatusCommand(outputOption));
        root.AddCommand(BuildCheckpointCommand(outputOption));
        root.AddCommand(BuildConfigCommand(outputOption));
        return root;
    }

    public static Parser CreateParser() => new(BuildRootCommand());

    private static Command BuildVersionCommand()
    {
        var version = new Command("version", "Print the version");
        version.SetHandler(() => Console.WriteLine("agentpowershell 0.1.0-dev"));
        return version;
    }

    private static Command BuildExecCommand(Option<string> outputOption)
    {
        var command = new Command("exec", "Execute a command in a session");
        var sessionIdArgument = new Argument<string>("session-id");
        var commandArgument = new Argument<string[]>("command") { Arity = ArgumentArity.OneOrMore };
        command.AddArgument(sessionIdArgument);
        command.AddArgument(commandArgument);
        command.SetHandler((string sessionId, string[] commandLine, string output) =>
        {
            Emit(output, new
            {
                command = "exec",
                sessionId,
                commandLine = string.Join(' ', commandLine)
            });
        }, sessionIdArgument, commandArgument, outputOption);
        return command;
    }

    private static Command BuildStartCommand(Option<string> outputOption)
    {
        var command = new Command("start", "Start daemon");
        command.SetHandler((string output) => Emit(output, new { command = "start", daemon = "starting" }), outputOption);
        return command;
    }

    private static Command BuildStopCommand(Option<string> outputOption)
    {
        var command = new Command("stop", "Stop daemon");
        command.SetHandler((string output) => Emit(output, new { command = "stop", daemon = "stopping" }), outputOption);
        return command;
    }

    private static Command BuildSessionCommand(Option<string> outputOption)
    {
        var command = new Command("session", "Session management");
        var create = new Command("create", "Create session");
        create.SetHandler((string output) => Emit(output, new { command = "session create", sessionId = Guid.NewGuid().ToString("N") }), outputOption);

        var list = new Command("list", "List sessions");
        list.SetHandler(async (string output) =>
        {
            var store = new SessionStore(Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var sessions = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            Emit(output, new { command = "session list", sessions });
        }, outputOption);

        var destroy = new Command("destroy", "Destroy session");
        var sessionId = new Argument<string>("session-id");
        destroy.AddArgument(sessionId);
        destroy.SetHandler(async (string id, string output) =>
        {
            var store = new SessionStore(Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            await store.RemoveAsync(id, CancellationToken.None).ConfigureAwait(false);
            Emit(output, new { command = "session destroy", sessionId = id, removed = true });
        }, sessionId, outputOption);

        command.AddCommand(create);
        command.AddCommand(list);
        command.AddCommand(destroy);
        return command;
    }

    private static Command BuildPolicyCommand(Option<string> outputOption)
    {
        var command = new Command("policy", "Policy management");
        var validate = new Command("validate", "Validate policy");
        var pathArgument = new Argument<string>("path", () => "default-policy.yml");
        validate.AddArgument(pathArgument);
        validate.SetHandler((string path, string output) =>
        {
            var policy = new PolicyLoader().LoadFromFile(path);
            Emit(output, new { command = "policy validate", valid = true, policy = policy.Name });
        }, pathArgument, outputOption);

        var show = new Command("show", "Show policy");
        show.AddArgument(pathArgument);
        show.SetHandler((string path, string output) =>
        {
            var policy = new PolicyLoader().LoadFromFile(path);
            Emit(output, new { command = "policy show", policy });
        }, pathArgument, outputOption);

        command.AddCommand(validate);
        command.AddCommand(show);
        return command;
    }

    private static Command BuildReportCommand(Option<string> outputOption, Option<string> eventsOption)
    {
        var command = new Command("report", "Generate session report");
        var sessionId = new Option<string?>("--session-id");
        command.AddOption(sessionId);
        command.SetHandler((string? id, string output, string eventsMode) =>
        {
            Emit(output, new { command = "report", sessionId = id, events = eventsMode, status = "generated" });
        }, sessionId, outputOption, eventsOption);
        return command;
    }

    private static Command BuildStatusCommand(Option<string> outputOption)
    {
        var command = new Command("status", "Show daemon and session status");
        command.SetHandler(async (string output) =>
        {
            var store = new SessionStore(Path.Combine(Environment.CurrentDirectory, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            var sessions = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            Emit(output, new { command = "status", daemon = "configured", sessionCount = sessions.Count });
        }, outputOption);
        return command;
    }

    private static Command BuildCheckpointCommand(Option<string> outputOption)
    {
        var command = new Command("checkpoint", "Checkpoint commands");
        var managerFactory = () => new WorkspaceCheckpointManager(Environment.CurrentDirectory);

        var create = new Command("create", "Create a workspace checkpoint");
        var nameOption = new Option<string?>("--name");
        create.AddOption(nameOption);
        create.SetHandler(async (string? name, string output) =>
        {
            var checkpoint = await managerFactory().CreateAsync(name, CancellationToken.None).ConfigureAwait(false);
            Emit(output, new
            {
                command = "checkpoint create",
                checkpoint = new
                {
                    id = checkpoint.CheckpointId,
                    checkpoint.Name,
                    checkpoint.CreatedAt,
                    fileCount = checkpoint.Files.Count
                }
            });
        }, nameOption, outputOption);

        var restore = new Command("restore", "Restore a workspace checkpoint");
        var checkpointIdArgument = new Argument<string>("checkpoint-id", () => "latest");
        var dryRunOption = new Option<bool>("--dry-run");
        restore.AddArgument(checkpointIdArgument);
        restore.AddOption(dryRunOption);
        restore.SetHandler(async (string checkpointId, bool dryRun, string output) =>
        {
            var manager = managerFactory();
            var preview = dryRun
                ? await manager.PreviewRestoreAsync(checkpointId, CancellationToken.None).ConfigureAwait(false)
                : await manager.RestoreAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
            Emit(output, new
            {
                command = "checkpoint restore",
                checkpointId,
                dryRun,
                changes = new
                {
                    add = preview.FilesToAdd,
                    update = preview.FilesToUpdate,
                    delete = preview.FilesToDelete,
                    total = preview.TotalChanges
                }
            });
        }, checkpointIdArgument, dryRunOption, outputOption);

        var list = new Command("list", "List workspace checkpoints");
        list.SetHandler(async (string output) =>
        {
            var checkpoints = await managerFactory().ListAsync(CancellationToken.None).ConfigureAwait(false);
            Emit(output, new
            {
                command = "checkpoint list",
                checkpoints = checkpoints.Select(checkpoint => new
                {
                    id = checkpoint.CheckpointId,
                    checkpoint.Name,
                    checkpoint.CreatedAt,
                    fileCount = checkpoint.Files.Count
                }).ToArray()
            });
        }, outputOption);

        command.AddCommand(create);
        command.AddCommand(restore);
        command.AddCommand(list);

        return command;
    }

    private static Command BuildConfigCommand(Option<string> outputOption)
    {
        var command = new Command("config", "Config commands");
        var show = new Command("show", "Show config");
        show.SetHandler((string output) =>
        {
            var config = File.Exists("config.yml") ? new ConfigLoader().LoadFromFile("config.yml") : new AgentPowerShellConfig();
            Emit(output, new { command = "config show", config });
        }, outputOption);

        var set = new Command("set", "Set config value");
        var key = new Argument<string>("key");
        var value = new Argument<string>("value");
        set.AddArgument(key);
        set.AddArgument(value);
        set.SetHandler((string keyValue, string configValue, string output) =>
        {
            Emit(output, new { command = "config set", key = keyValue, value = configValue, updated = false });
        }, key, value, outputOption);

        command.AddCommand(show);
        command.AddCommand(set);
        return command;
    }

    private static void Emit(string outputMode, object payload)
    {
        if (string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        Console.WriteLine(string.Join(Environment.NewLine, payload.GetType().GetProperties().Select(property => $"{property.Name}: {property.GetValue(payload)}")));
    }
}
