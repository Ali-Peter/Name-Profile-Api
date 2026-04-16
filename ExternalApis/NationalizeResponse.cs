public class NationalizeResponse
{
    public string? Name { get; set; }
    public List<Country> Country { get; set; } = new();
}

public class Country
{
    public string CountryId { get; set; } = string.Empty;
    public double Probability { get; set; }
}