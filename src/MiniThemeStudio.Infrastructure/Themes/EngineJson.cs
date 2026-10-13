using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniThemeStudio.Infrastructure.Themes;

public static class EngineJson
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Indented = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentSize = 4,
        NewLine = "\n",
    };

    public static string ToCompactString(JsonNode node) => node.ToJsonString(Compact);

    public static string ToIndentedString(JsonNode node) => node.ToJsonString(Indented);

    public static void WriteAtomically(string path, string content)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, true);
    }
}
