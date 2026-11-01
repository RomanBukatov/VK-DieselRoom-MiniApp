using System.Text.Json.Serialization;

namespace DieselRoom.Api.DTOs;

public class VkCallbackRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("group_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? GroupId { get; set; }

    private string? _secret;

    [JsonPropertyName("secret")]
    public string? Secret
    {
        get => _secret;
        set => _secret = value;
    }

    [JsonPropertyName("secret_key")]
    public string? SecretKey
    {
        get => _secret;
        set => _secret = value ?? _secret;
    }

    [JsonPropertyName("object")]
    public object? Object { get; set; }

    // Lowercase property aliases for compatibility
    [JsonIgnore]
    public string? type { get => Type; set => Type = value; }

    [JsonIgnore]
    public string? secret { get => Secret; set => Secret = value; }

    [JsonIgnore]
    public long? group_id { get => GroupId; set => GroupId = value; }

    [JsonIgnore]
    public object? @object { get => Object; set => Object = value; }

    public VkCallbackRequest() { }

    public VkCallbackRequest(string? type, object? @object = null, string? secret = null, long? groupId = null)
    {
        Type = type;
        Object = @object;
        Secret = secret;
        GroupId = groupId;
    }
}
