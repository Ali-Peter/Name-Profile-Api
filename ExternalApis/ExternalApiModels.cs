public class GenderizeResponse
{
    public string? Name { get; set; }
    public string? Gender { get; set; }
    public double Probability { get; set; }
    public int Count { get; set; }
}

public class AgifyResponse
{
    public string? Name { get; set; }
    public int? Age { get; set; }
    public int Count { get; set; }
}

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