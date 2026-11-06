using System.Text.Json;

namespace IOToolkit.WebBridge.Configuration;

public sealed class BridgeOptions
{
    public string ProjectRoot { get; init; } = Directory.GetCurrentDirectory();
    public string HardwareConfigPath { get; init; } = string.Empty;
    public string BindingsPath { get; init; } = string.Empty;
    public int Port { get; init; } = 19101;
    public bool Mock { get; init; }

    /// <summary>
    /// Extra browser origins allowed to call this bridge, on top of loopback origins.
    /// Needed when the page is served from something other than the exhibit shell.
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = Array.Empty<string>();

    public static BridgeOptions Parse(string[] args, string baseDirectory)
    {
        var projectRoot = Directory.GetCurrentDirectory();
        string? hardware = null;
        string? bindings = null;
        var port = 19101;
        var mock = false;
        var allowedOrigins = new List<string>();

        var configuredOrigins = Environment.GetEnvironmentVariable("IOTOOLKIT_BRIDGE_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(configuredOrigins))
            allowedOrigins.AddRange(SplitOrigins(configuredOrigins));

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" when i + 1 < args.Length:
                    projectRoot = Path.GetFullPath(args[++i]);
                    break;
                case "--config" when i + 1 < args.Length:
                    hardware = args[++i];
                    break;
                case "--bindings" when i + 1 < args.Length:
                    bindings = args[++i];
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedPort):
                    port = parsedPort;
                    break;
                case "--allow-origin" when i + 1 < args.Length:
                    allowedOrigins.AddRange(SplitOrigins(args[++i]));
                    break;
                case "--mock":
                    mock = true;
                    break;
            }
        }

        var hardwarePath = Resolve(projectRoot, hardware ?? "hardware/IODevice.xml", baseDirectory);
        var bindingsPath = Resolve(projectRoot, bindings ?? "hardware/bindings.json", baseDirectory);

        return new BridgeOptions
        {
            ProjectRoot = projectRoot,
            HardwareConfigPath = hardwarePath,
            BindingsPath = bindingsPath,
            Port = port,
            Mock = mock,
            AllowedOrigins = allowedOrigins
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IEnumerable<string> SplitOrigins(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Resolve(string projectRoot, string path, string baseDirectory) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(projectRoot, path));
}

public sealed class BridgeConfig
{
    public string Schema { get; set; } = "iotoolkit-bridge/v1";
    public string Device { get; set; } = string.Empty;
    public int PollIntervalMs { get; set; } = 16;
    public List<BridgeChannel> Channels { get; set; } = new();
    public List<BridgeAction> Actions { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static BridgeConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Bridge bindings file was not found.", path);

        var config = JsonSerializer.Deserialize<BridgeConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Bridge bindings file is invalid.");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        if (Schema != "iotoolkit-bridge/v1")
            throw new InvalidDataException($"Unsupported bridge schema '{Schema}'.");
        if (string.IsNullOrWhiteSpace(Device))
            throw new InvalidDataException("Bridge device is required.");
        if (PollIntervalMs is < 4 or > 1000)
            throw new InvalidDataException("PollIntervalMs must be between 4 and 1000.");

        var duplicate = Channels.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Duplicate channel id '{duplicate.Key}'.");

        foreach (var channel in Channels)
            channel.Validate();
        foreach (var action in Actions)
            action.Validate(Channels);
    }
}

public sealed class BridgeChannel
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Read { get; set; } = true;
    public bool Write { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double SafeValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public List<string> Events { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidDataException("Channel id is required.");
        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidDataException($"Channel '{Id}' key is required.");

        Type = Type.Trim().ToUpperInvariant();
        if (Type is not ("DI" or "AD" or "DO" or "OAXIS" or "OACTION"))
            throw new InvalidDataException($"Channel '{Id}' type '{Type}' is unsupported.");
        if (!Read && !Write)
            throw new InvalidDataException($"Channel '{Id}' is neither readable nor writable.");
        if (Min is not null && Max is not null && Min > Max)
            throw new InvalidDataException($"Channel '{Id}' min is greater than max.");

        if (!Write && Type is "DO" or "OAXIS" or "OACTION")
            throw new InvalidDataException($"Output channel '{Id}' must be writable.");
    }
}

public sealed class BridgeAction
{
    public string Id { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public double Value { get; set; }
    public int PulseMs { get; set; }

    public void Validate(IReadOnlyCollection<BridgeChannel> channels)
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidDataException("Action id is required.");
        if (string.IsNullOrWhiteSpace(Channel))
            throw new InvalidDataException($"Action '{Id}' channel is required.");
        if (PulseMs is < 0 or > 600000)
            throw new InvalidDataException($"Action '{Id}' pulse length is invalid.");
        var channel = channels.FirstOrDefault(x => string.Equals(x.Id, Channel, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
            throw new InvalidDataException($"Action '{Id}' references unknown channel '{Channel}'.");
        if (!channel.Write)
            throw new InvalidDataException($"Action '{Id}' targets a read-only channel.");
    }
}

