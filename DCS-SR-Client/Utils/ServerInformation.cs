using System.Text.Json.Serialization;

namespace Vanguard.VCS.Client.Utils;

public class ServerInformationApiResponse
{
    [JsonPropertyName("data")]
    public ServerInformation Data { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; }
}

public class ServerInformation
{
    [JsonPropertyName("serverAddress")]
    public string Address { get; set; }
    
    [JsonPropertyName("serverControlPort")]
    public int ControlPort { get; set; }
    
    [JsonPropertyName("serverVoicePort")]
    public int VoicePort { get; set; }
    
    [JsonPropertyName("serverRestPort")]
    public int RestPort { get; set; }
}