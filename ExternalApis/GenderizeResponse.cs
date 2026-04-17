using System.Text.Json.Serialization;

public class GenderizeResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("probability")]
    public double Probability { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}