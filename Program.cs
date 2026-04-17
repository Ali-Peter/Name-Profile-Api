using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ====================== DATABASE CONFIG ======================
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

if (string.IsNullOrEmpty(connectionString))
{
    connectionString = "Host=localhost;Database=devdb;Username=postgres;Password=postgres";
}

// Convert Render postgres URL → Npgsql format
if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
{
    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':');

    var port = uri.Port <= 0 ? 5432 : uri.Port;

    connectionString =
        $"Host={uri.Host};" +
        $"Port={port};" +
        $"Database={uri.AbsolutePath.TrimStart('/')};" +
        $"Username={userInfo[0]};" +
        $"Password={userInfo[1]};" +
        "SSL Mode=Require;Trust Server Certificate=true";
}

// ====================== SERVICES ======================
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddHttpClient("ExternalApis", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseCors("AllowAll");

// ====================== HELPERS ======================
static string GetAgeGroup(int age) => age switch
{
    < 13 => "child",
    < 20 => "teenager",
    < 60 => "adult",
    _ => "senior"
};

// ====================== CREATE PROFILE ======================
app.MapPost("/api/profiles", async (CreateProfileRequest req, AppDbContext db, IHttpClientFactory factory) =>
{
    if (req == null || string.IsNullOrWhiteSpace(req.Name))
    {
        return Results.BadRequest(new
        {
            status = "error",
            message = "Name is required"
        });
    }

    var name = req.Name.Trim().ToLowerInvariant();

    var existing = await db.Profiles.FirstOrDefaultAsync(p => p.Name.ToLower() == name);

    if (existing != null)
    {
        return Results.Ok(new
        {
            status = "success",
            message = "Profile already exists",
            data = new
            {
                id = existing.Id,
                name = existing.Name,
                gender = existing.Gender,
                gender_probability = existing.GenderProbability,
                sample_size = existing.SampleSize,
                age = existing.Age,
                age_group = existing.AgeGroup,
                country_id = existing.CountryId,
                country_probability = existing.CountryProbability,
                created_at = existing.CreatedAt.ToString("o")
            }
        });
    }

    var client = factory.CreateClient("ExternalApis");

    async Task<T?> SafeGet<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429)
                throw new Exception("External API rate limit exceeded. Try again later.");

            throw new Exception($"External API error: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<T>();
    }

    try
    {
        var genderTask = SafeGet<GenderizeResponse>(client, $"https://api.genderize.io?name={name}");
        var ageTask = SafeGet<AgifyResponse>(client, $"https://api.agify.io?name={name}");
        var nationTask = SafeGet<NationalizeResponse>(client, $"https://api.nationalize.io?name={name}");

        await Task.WhenAll(genderTask!, ageTask!, nationTask!);

        var gender = await genderTask;
        var age = await ageTask;
        var nation = await nationTask;

        if (gender == null || string.IsNullOrEmpty(gender.Gender) || gender.Count == 0)
            return Results.Json(new { status = "error", message = "Genderize returned an invalid response" }, statusCode: 502);

        if (age == null || age.Age == null)
            return Results.Json(new { status = "error", message = "Agify returned an invalid response" }, statusCode: 502);

        if (nation == null || nation.Country == null || nation.Country.Count == 0)
            return Results.Json(new { status = "error", message = "Nationalize returned an invalid response" }, statusCode: 502);

        var bestCountry = nation.Country.OrderByDescending(c => c.Probability).First();

        var profile = new Profile
        {
            Name = name,
            Gender = gender.Gender!,
            GenderProbability = gender.Probability,
            SampleSize = gender.Count,
            Age = age.Age.Value,
            AgeGroup = GetAgeGroup(age.Age.Value),
            CountryId = bestCountry.CountryId.ToUpper(),
            CountryProbability = bestCountry.Probability,
            CreatedAt = DateTime.UtcNow
        };

        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        return Results.Created($"/api/profiles/{profile.Id}", new
        {
            status = "success",
            data = new
            {
                id = profile.Id,
                name = profile.Name,
                gender = profile.Gender,
                gender_probability = profile.GenderProbability,
                sample_size = profile.SampleSize,
                age = profile.Age,
                age_group = profile.AgeGroup,
                country_id = profile.CountryId,
                country_probability = profile.CountryProbability,
                created_at = profile.CreatedAt.ToString("o")
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "error",
            message = ex.Message
        }, statusCode: 500);
    }
});

// ====================== GET BY ID ======================
app.MapGet("/api/profiles/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var profile = await db.Profiles.FindAsync(id);

    if (profile == null)
    {
        return Results.NotFound(new
        {
            status = "error",
            message = "Profile not found"
        });
    }

    return Results.Ok(new
    {
        status = "success",
        data = new
        {
            id = profile.Id,
            name = profile.Name,
            gender = profile.Gender,
            gender_probability = profile.GenderProbability,
            sample_size = profile.SampleSize,
            age = profile.Age,
            age_group = profile.AgeGroup,
            country_id = profile.CountryId,
            country_probability = profile.CountryProbability,
            created_at = profile.CreatedAt.ToString("o")
        }
    });
});

// ====================== GET ALL ======================
app.MapGet("/api/profiles", async (AppDbContext db, string? gender, string? country_id, string? age_group) =>
{
    var query = db.Profiles.AsQueryable();

    if (!string.IsNullOrEmpty(gender))
        query = query.Where(p => p.Gender.ToLower() == gender.ToLower());

    if (!string.IsNullOrEmpty(country_id))
        query = query.Where(p => p.CountryId.ToLower() == country_id.ToLower());

    if (!string.IsNullOrEmpty(age_group))
        query = query.Where(p => p.AgeGroup.ToLower() == age_group.ToLower());

    var data = await query.ToListAsync();

    return Results.Ok(new
    {
        status = "success",
        count = data.Count,
        data = data.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            gender = p.Gender,
            age = p.Age,
            age_group = p.AgeGroup,
            country_id = p.CountryId,
            created_at = p.CreatedAt.ToString("o")
        })
    });
});

// ====================== DELETE ======================
app.MapDelete("/api/profiles/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var profile = await db.Profiles.FindAsync(id);

    if (profile == null)
    {
        return Results.NotFound(new
        {
            status = "error",
            message = "Profile not found"
        });
    }

    db.Profiles.Remove(profile);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();