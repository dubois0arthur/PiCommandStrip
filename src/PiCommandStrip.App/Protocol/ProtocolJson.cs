using System.Text.Json;

namespace PiCommandStrip.App.Protocol;

public static class ProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };
}
