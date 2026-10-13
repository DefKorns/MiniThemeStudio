using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.Infrastructure.Firmware;

public sealed class FirmwareRegistry : IFirmwareRegistry
{
    private static readonly FirmwareDefinition[] BuiltInDefinitions =
    [
        new(
            "dp-sneseur-v2.0.14",
            "snes",
            "eur",
            "2.0.14",
            "SNES Classic (Europe) v2.0.14",
            "dp-sneseur-release-v2.0.14-*.tar.gz",
            "usr/share/ui/snes-eur",
            "/usr/share/ui/snes-eur",
            "/var/squashfs/usr/share/ui/snes-eur",
            9),
        new(
            "dp-sneseur-v2.0.7",
            "snes",
            "eur",
            "2.0.7",
            "SNES Classic (Europe) v2.0.7",
            "dp-sneseur-release-v2.0.7-*.tar.gz",
            "usr/share/ui/snes-eur",
            "/usr/share/ui/snes-eur",
            "/var/squashfs/usr/share/ui/snes-eur",
            9),
        new(
            "dp-snesusa-v2.0.14",
            "snes",
            "usa",
            "2.0.14",
            "SNES Classic (USA) v2.0.14",
            "dp-snesusa-release-v2.0.14-*.tar.gz",
            "usr/share/ui/snes-usa",
            "/usr/share/ui/snes-usa",
            "/var/squashfs/usr/share/ui/snes-usa",
            9),
        new(
            "dp-snesusa-v2.0.13",
            "snes",
            "usa",
            "2.0.13",
            "SNES Classic (USA) v2.0.13",
            "dp-snesusa-release-v2.0.13-*.tar.gz",
            "usr/share/ui/snes-usa",
            "/usr/share/ui/snes-usa",
            "/var/squashfs/usr/share/ui/snes-usa",
            9),
        new(
            "dp-snesusa-v2.0.7",
            "snes",
            "usa",
            "2.0.7",
            "SNES Classic (USA) v2.0.7",
            "dp-snesusa-release-v2.0.7-*.tar.gz",
            "usr/share/ui/snes-usa",
            "/usr/share/ui/snes-usa",
            "/var/squashfs/usr/share/ui/snes-usa",
            9),
        new(
            "dp-shvc-v2.0.12",
            "snes",
            "jpn",
            "2.0.12",
            "Super Famicom (Japan) v2.0.12",
            "dp-shvc-release-v2.0.12-*.tar.gz",
            "usr/share/ui/shvc",
            "/usr/share/ui/shvc",
            "/var/squashfs/usr/share/ui/shvc",
            9),
        new(
            "dp-nes-v1.0.2",
            "nes",
            "usa",
            "1.0.2",
            "NES Classic v1.0.2",
            "dp-nes-release-v1.0.2-*.tar.gz",
            "usr/share/clover-ui",
            "/usr/share/clover-ui",
            "/var/squashfs/usr/share/clover-ui",
            8),
        new(
            "dp-nes-v1.0.3",
            "nes",
            "usa",
            "1.0.3",
            "NES Classic v1.0.3",
            "dp-nes-release-v1.0.3-*.tar.gz",
            "usr/share/clover-ui",
            "/usr/share/clover-ui",
            "/var/squashfs/usr/share/clover-ui",
            8),
        new(
            "dp-nes-v1.0.7",
            "nes",
            "usa",
            "1.0.7",
            "NES Classic v1.0.7",
            "dp-nes-release-v1.0.7-*.tar*",
            "usr/share/clover-ui",
            "/usr/share/clover-ui",
            "/var/squashfs/usr/share/clover-ui",
            8),
        new(
            "dp-hvc-v1.0.5",
            "famicom",
            "jpn",
            "1.0.5",
            "Famicom Classic (Japan) v1.0.5",
            "dp-hvc-release-v1.0.5-*.tar.gz",
            "usr/share/clover-ui",
            "/usr/share/clover-ui",
            "/var/squashfs/usr/share/clover-ui",
            8),
        new(
            "dp-hvcj-v3.0.1",
            "shonen",
            "jpn",
            "3.0.1",
            "Shonen Jump 50th Edition v3.0.1",
            "dp*hvcj*release*v3.0.1*.tar*",
            "usr/share/ui/hvc",
            "/usr/share/ui/hvc",
            "/var/squashfs/usr/share/ui/hvc",
            10),
    ];

    private readonly List<FirmwareDefinition> _definitions = [.. BuiltInDefinitions];

    public FirmwareRegistry(string? customJsonConfigPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customJsonConfigPath) && File.Exists(customJsonConfigPath))
        {
            LoadCustomDefinitions(customJsonConfigPath);
        }
    }

    public IReadOnlyList<FirmwareDefinition> All => _definitions;

    public FirmwareDefinition? FindById(string id)
    {
        return _definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public FirmwareDefinition? MatchArchive(string fileName)
    {
        var cleanName = Path.GetFileName(fileName).ToLowerInvariant();
        foreach (var def in _definitions)
        {
            var pattern = def.ArchivePattern.ToLowerInvariant()
                .Replace(".", "\\.")
                .Replace("*", ".*")
                .Replace("?", ".");
            if (Regex.IsMatch(cleanName, $"^{pattern}$", RegexOptions.IgnoreCase))
            {
                return def;
            }
        }

        // Fallback heuristic: check keywords in filename
        if (cleanName.Contains("shvc"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "jpn");
        }
        if (cleanName.Contains("sneseur") || (cleanName.Contains("snes") && cleanName.Contains("eur")))
        {
            return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "eur");
        }
        if (cleanName.Contains("snesusa") || (cleanName.Contains("snes") && cleanName.Contains("usa")))
        {
            return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "usa");
        }
        if (cleanName.Contains("hvcj") || cleanName.Contains("shonen"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "shonen");
        }
        if (cleanName.Contains("hvc"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "famicom");
        }
        if (cleanName.Contains("nes"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "nes");
        }

        return null;
    }

    public FirmwareDefinition? MatchExtractedFolder(string directoryPath)
    {
        var initJsonPath = Path.Combine(directoryPath, "init.json");
        if (File.Exists(initJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(initJsonPath));
                var root = doc.RootElement;
                var engineFormat = root.TryGetProperty("engineFormat", out var ef) ? ef.GetInt32() : 0;
                var scripts = root.TryGetProperty("luaScripts", out var s)
                    ? s.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                    : [];

                if (engineFormat >= 10 || scripts.Any(x => x.Contains("shonen", StringComparison.OrdinalIgnoreCase)))
                {
                    return _definitions.FirstOrDefault(d => d.Console == "shonen");
                }
                if (scripts.Any(x => x.EndsWith("env_eur.lua", StringComparison.OrdinalIgnoreCase)))
                {
                    return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "eur");
                }
                if (scripts.Any(x => x.EndsWith("env_usa.lua", StringComparison.OrdinalIgnoreCase)))
                {
                    return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "usa");
                }
                if (scripts.Any(x => x.EndsWith("env_jpn.lua", StringComparison.OrdinalIgnoreCase)))
                {
                    return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "jpn");
                }
                if (scripts.Any(x => x.EndsWith("colors_nes.lua", StringComparison.OrdinalIgnoreCase)))
                {
                    return _definitions.FirstOrDefault(d => d.Console == "nes");
                }
                if (scripts.Any(x => x.EndsWith("colors_hvc.lua", StringComparison.OrdinalIgnoreCase)))
                {
                    return _definitions.FirstOrDefault(d => d.Console == "famicom");
                }
            }
            catch
            {
                // Ignore parse errors and fall through
            }
        }

        // Folder name heuristic fallback
        var dirName = new DirectoryInfo(directoryPath).Name.ToLowerInvariant();
        if (dirName.Contains("shonen"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "shonen");
        }
        if (dirName.Contains("eur"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "eur");
        }
        if (dirName.Contains("usa"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "usa");
        }
        if (dirName.Contains("jpn") || dirName.Contains("shvc"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "snes" && d.Region == "jpn");
        }
        if (dirName.Contains("famicom") || dirName.Contains("hvc"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "famicom");
        }
        if (dirName.Contains("nes"))
        {
            return _definitions.FirstOrDefault(d => d.Console == "nes");
        }

        return null;
    }

    public void RegisterCustomDefinition(FirmwareDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var existingIndex = _definitions.FindIndex(d => string.Equals(d.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _definitions[existingIndex] = definition;
        }
        else
        {
            _definitions.Add(definition);
        }
    }

    public void LoadCustomDefinitions(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(jsonFilePath);
            var loaded = JsonSerializer.Deserialize<List<FirmwareDefinition>>(json);
            if (loaded != null)
            {
                foreach (var def in loaded)
                {
                    RegisterCustomDefinition(def);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Could not load custom firmware definitions from {jsonFilePath}: {ex.Message}", ex);
        }
    }
}
