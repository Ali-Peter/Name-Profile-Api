using System.ComponentModel.DataAnnotations;

public class Profile
{
    public Guid Id { get; set; } = Guid.CreateVersion7(); // UUID v7 as required

    [Required]
    public string Name { get; set; } = string.Empty;

    public string Gender { get; set; } = string.Empty;
    public double GenderProbability { get; set; }
    public int SampleSize { get; set; }

    public int Age { get; set; }
    public string AgeGroup { get; set; } = string.Empty;

    public string CountryId { get; set; } = string.Empty;
    public double CountryProbability { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}