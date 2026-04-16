using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// === Database Configuration (Works on Render + Local) ===
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"];

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Database connection string is missing. Set DefaultConnection or DATABASE_URL.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        // Fixed for .NET 10 / newer Npgsql
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5);
    });
});

// === HttpClients for external APIs ===
builder.Services.AddHttpClient("ExternalApis", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Strong CORS (required for grading script)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

// === Auto Migration (Very Important for Render) ===
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    Console.WriteLine("✅ Database migrated successfully.");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ Migration failed: {ex.Message}");
}

// Helper to get age group
static string GetAgeGroup(int age) => age switch
{
    < 13 => "child",
    < 20 => "teenager",
    < 60 => "adult",
    _ => "senior"
};

// ====================== POST /api/profiles ======================
app.MapPost("/api/profiles", async (CreateProfileRequest req, AppDbContext db, IHttpClientFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { status = "error", message = "Name parameter is required and cannot be empty" });

    var name = req.Name.Trim().ToLowerInvariant();

    // Idempotency check
    var existing = await db.Profiles.FirstOrDefaultAsync(p => p.Name.ToLower() == name);
    if (existing != null)
    {
        return Results.Ok(new
        {
            status = "success",
            message = "Profile already exists",
            data = new ProfileResponse
            {
                Id = existing.Id,
                Name = existing.Name,
                Gender = existing.Gender,
                GenderProbability = existing.GenderProbability,
                SampleSize = existing.SampleSize,
                Age = existing.Age,
                AgeGroup = existing.AgeGroup,
                CountryId = existing.CountryId,
                CountryProbability = existing.CountryProbability,
                CreatedAt = existing.CreatedAt
            }
        });
    }

    var client = factory.CreateClient("ExternalApis");

    try
    {
        var genderTask = client.GetFromJsonAsync<GenderizeResponse>($"https://api.genderize.io?name={Uri.EscapeDataString(name)}");
        var ageTask    = client.GetFromJsonAsync<AgifyResponse>($"https://api.agify.io?name={Uri.EscapeDataString(name)}");
        var nationTask = client.GetFromJsonAsync<NationalizeResponse>($"https://api.nationalize.io?name={Uri.EscapeDataString(name)}");

        await Task.WhenAll(genderTask, ageTask, nationTask);

        var genderData = await genderTask;
        var ageData    = await ageTask;
        var nationData = await nationTask;

        // Edge cases → Return 502 with body
        if (genderData == null || string.IsNullOrEmpty(genderData.Gender) || genderData.Count == 0)
            return Results.Json(new { status = "error", message = "Genderize returned an invalid response" }, statusCode: 502);

        if (ageData == null || ageData.Age == null)
            return Results.Json(new { status = "error", message = "Agify returned an invalid response" }, statusCode: 502);

        if (nationData == null || nationData.Country.Count == 0)
            return Results.Json(new { status = "error", message = "Nationalize returned an invalid response" }, statusCode: 502);

        var bestCountry = nationData.Country.OrderByDescending(c => c.Probability).First();

        var profile = new Profile
        {
            Name = name,
            Gender = genderData.Gender!,
            GenderProbability = genderData.Probability,
            SampleSize = genderData.Count,
            Age = ageData.Age.Value,
            AgeGroup = GetAgeGroup(ageData.Age.Value),
            CountryId = bestCountry.CountryId.ToUpper(),
            CountryProbability = bestCountry.Probability,
            CreatedAt = DateTime.UtcNow
        };

        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var responseData = new ProfileResponse
        {
            Id = profile.Id,
            Name = profile.Name,
            Gender = profile.Gender,
            GenderProbability = profile.GenderProbability,
            SampleSize = profile.SampleSize,
            Age = profile.Age,
            AgeGroup = profile.AgeGroup,
            CountryId = profile.CountryId,
            CountryProbability = profile.CountryProbability,
            CreatedAt = profile.CreatedAt
        };

        return Results.Created($"/api/profiles/{profile.Id}", new { status = "success", data = responseData });
    }
    catch (HttpRequestException)
    {
        return Results.Json(new { status = "error", message = "Upstream service unavailable" }, statusCode: 502);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// ====================== GET /api/profiles/{id} ======================
app.MapGet("/api/profiles/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var profile = await db.Profiles.FindAsync(id);
    if (profile == null)
        return Results.NotFound(new { status = "error", message = "Profile not found" });

    var response = new ProfileResponse
    {
        Id = profile.Id,
        Name = profile.Name,
        Gender = profile.Gender,
        GenderProbability = profile.GenderProbability,
        SampleSize = profile.SampleSize,
        Age = profile.Age,
        AgeGroup = profile.AgeGroup,
        CountryId = profile.CountryId,
        CountryProbability = profile.CountryProbability,
        CreatedAt = profile.CreatedAt
    };

    return Results.Ok(new { status = "success", data = response });
});

// ====================== GET /api/profiles (with filters) ======================
app.MapGet("/api/profiles", async (AppDbContext db, string? gender, string? country_id, string? age_group) =>
{
    var query = db.Profiles.AsQueryable();

    if (!string.IsNullOrEmpty(gender))
        query = query.Where(p => p.Gender.ToLower() == gender.ToLower());

    if (!string.IsNullOrEmpty(country_id))
        query = query.Where(p => p.CountryId.ToLower() == country_id.ToLower());

    if (!string.IsNullOrEmpty(age_group))
        query = query.Where(p => p.AgeGroup.ToLower() == age_group.ToLower());

    var profiles = await query
        .Select(p => new ProfileListItem
        {
            Id = p.Id,
            Name = p.Name,
            Gender = p.Gender,
            Age = p.Age,
            AgeGroup = p.AgeGroup,
            CountryId = p.CountryId
        })
        .ToListAsync();

    return Results.Ok(new
    {
        status = "success",
        count = profiles.Count,
        data = profiles
    });
});

// ====================== DELETE /api/profiles/{id} ======================
app.MapDelete("/api/profiles/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var profile = await db.Profiles.FindAsync(id);
    if (profile == null)
        return Results.NotFound(new { status = "error", message = "Profile not found" });

    db.Profiles.Remove(profile);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();