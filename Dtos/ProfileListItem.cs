public class ProfileListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string AgeGroup { get; set; } = string.Empty;
    public string CountryId { get; set; } = string.Empty;
}