using System.Text.Json.Serialization;

namespace AIIDEWPF.Models;

public class ChatMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";
    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; set; } = new();

    // UI only - ignored during serialization
    [JsonIgnore]
    public string Name { get => Function.Name; set => Function.Name = value; }
    [JsonIgnore]
    public string Arguments { get => Function.Arguments; set => Function.Arguments = value; }
    [JsonIgnore]
    public string Status { get; set; } = "pending";
    [JsonIgnore]
    public string Result { get; set; } = string.Empty;
}

public class ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;
}
