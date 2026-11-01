using System.Text.Json;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.Infrastructure.Firmware;

public sealed class FirmwareRegistry : IFirmwareRegistry
{
    private const string BuiltInResourceName = "MiniThemeStudio.Firmware.firmwares.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly List<FirmwareDefinition> _definitions = [];

    public FirmwareRegistry(IEnumerable<FirmwareDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (FirmwareDefinition definition in definitions)
        {
            Register(definition);
        }
    }

    public IReadOnlyList<FirmwareDefinition> All => _definitions;

    public static FirmwareRegistry CreateDefault(string? userDefinitionsPath = null)
    {
        using Stream stream = typeof(FirmwareRegistry).Assembly.GetManifestResourceStream(BuiltInResourceName)
            ?? throw new InvalidOperationException("The built-in firmware registry is missing.");
        var definitions = Parse(stream, "built-in firmwares.json").ToList();

        if (!string.IsNullOrWhiteSpace(userDefinitionsPath) && File.Exists(userDefinitionsPath))
        {
            using FileStream userStream = File.OpenRead(userDefinitionsPath);
            IReadOnlyList<FirmwareDefinition> userDefinitions = Parse(userStream, userDefinitionsPath);
            definitions.RemoveAll(builtIn => userDefinitions.Any(user =>
                string.Equals(user.Id, builtIn.Id, StringComparison.OrdinalIgnoreCase)));
            definitions.AddRange(userDefinitions);
        }

        return new FirmwareRegistry(definitions);
    }

    public static IReadOnlyList<FirmwareDefinition> Parse(Stream stream, string sourceName)
    {
        try
        {
            RegistryDocument document = JsonSerializer.Deserialize<RegistryDocument>(stream, SerializerOptions)
                ?? throw new InvalidDataException("The document is empty.");
            if (document.SchemaVersion != 1)
            {
                throw new InvalidDataException($"Unsupported schemaVersion {document.SchemaVersion}.");
            }

            List<FirmwareDefinition> definitions = document.Bases ?? [];
            foreach (FirmwareDefinition definition in definitions)
            {
                Validate(definition);
            }

            return definitions;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"Invalid firmware registry ({sourceName}): {exception.Message}", exception);
        }
    }

    public FirmwareDefinition? FindById(string id)
    {
        return _definitions.FirstOrDefault(definition =>
            string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public (FirmwareDefinition Definition, FirmwareSource Source)? FindBySourceSha256(string sha256)
    {
        foreach (FirmwareDefinition definition in _definitions)
        {
            FirmwareSource? source = definition.Sources.FirstOrDefault(candidate =>
                string.Equals(candidate.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
            if (source is not null)
            {
                return (definition, source);
            }
        }

        return null;
    }

    public FirmwareDefinition? FindByUiTreeSha256(string sha256)
    {
        return _definitions.FirstOrDefault(definition =>
            string.Equals(definition.UiTreeSha256, sha256, StringComparison.OrdinalIgnoreCase));
    }

    private void Register(FirmwareDefinition definition)
    {
        Validate(definition);
        if (FindById(definition.Id) is not null)
        {
            throw new InvalidDataException($"Duplicate firmware definition id: {definition.Id}");
        }

        _definitions.Add(definition);
    }

    private static void Validate(FirmwareDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id)
            || string.IsNullOrWhiteSpace(definition.Console)
            || string.IsNullOrWhiteSpace(definition.Region)
            || string.IsNullOrWhiteSpace(definition.Version)
            || string.IsNullOrWhiteSpace(definition.UiPath))
        {
            throw new InvalidDataException("Each base needs id, console, region, version and uiPath.");
        }

        if (!IsSha256(definition.UiTreeSha256)
            || definition.Sources is null
            || definition.Sources.Any(source => !IsSha256(source.Sha256)))
        {
            throw new InvalidDataException($"Base '{definition.Id}' has a missing or malformed SHA-256.");
        }
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    }

    private sealed record RegistryDocument(int SchemaVersion, List<FirmwareDefinition>? Bases);
}
