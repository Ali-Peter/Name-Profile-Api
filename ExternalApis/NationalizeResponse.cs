using System.Text.Json.Serialization;
public class NationalizeResponse
{
    public string? Name { get; set; }
    public List<Country> Country { get; set; } = new();
}

public class Country
{
    [JsonPropertyName("country_id")]
    public string CountryId { get; set; } = string.Empty;

    [JsonPropertyName("probability")]
    public double Probability { get; set; }
}