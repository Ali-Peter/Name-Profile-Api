using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);



// ====================== DATABASE CONFIG (FIXED FOR RENDER) ======================
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Database connection string is missing. Set DefaultConnection or DATABASE_URL.");
}


// 🔥 FIX: Convert Render postgres:// URL to EF Core format
if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
{
    var uri = new Uri(connectionString);

    var userInfo = uri.UserInfo.Split(':');

    connectionString =
        $"Host={uri.Host};" +
        $"Port={uri.Port};" +
        $"Database={uri.AbsolutePath.TrimStart('/')};" +
        $"Username={userInfo[0]};" +
        $"Password={userInfo[1]};" +
        $"SSL Mode=Require;Trust Server Certificate=true";
}



// ====================== DB CONTEXT ======================
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5);
    });
});



// ====================== HTTP CLIENT ======================
builder.Services.AddHttpClient("ExternalApis", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});



// ====================== CORS ======================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader());
});



var app = builder.Build();

app.UseCors("AllowAll");



// ====================== AUTO MIGRATION ======================
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



// ====================== HELPER ======================
static string GetAgeGroup(int age) => age switch
{
    < 13 => "child",
    < 20 => "teenager",
    < 60 => "adult",
    _ => "senior"
};



// ====================== POST ======================
app.MapPost("/api/profiles", async (CreateProfileRequest req, AppDbContext db, IHttpClientFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { status = "error", message = "Name parameter is required and cannot be empty" });

    var name = req.Name.Trim().ToLowerInvariant();

    var existing = await db.Profiles.FirstOrDefaultAsync(p => p.Name.ToLower() == name);
    if (existing != null)
    {
        return Results.Ok(new
        {
            status = "success",
            message = "Profile already exists",
            data = existing
        });
    }

    var client = factory.CreateClient("ExternalApis");

    try
    {
        var genderTask = client.GetFromJsonAsync<GenderizeResponse>($"https://api.genderize.io?name={Uri.EscapeDataString(name)}");
        var ageTask = client.GetFromJsonAsync<AgifyResponse>($"https://api.agify.io?name={Uri.EscapeDataString(name)}");
        var nationTask = client.GetFromJsonAsync<NationalizeResponse>($"https://api.nationalize.io?name={Uri.EscapeDataString(name)}");

        await Task.WhenAll(genderTask!, ageTask!, nationTask!);

        var genderData = await genderTask;
        var ageData = await ageTask;
        var nationData = await nationTask;

        if (genderData == null || ageData == null || nationData == null)
            return Results.Problem("External API error");

        var bestCountry = nationData.Country.OrderByDescending(c => c.Probability).First();

        var profile = new Profile
        {
            Name = name,
            Gender = genderData.Gender!,
            GenderProbability = genderData.Probability,
            SampleSize = genderData.Count,
            Age = ageData.Age!.Value,
            AgeGroup = GetAgeGroup(ageData.Age.Value),
            CountryId = bestCountry.CountryId.ToUpper(),
            CountryProbability = bestCountry.Probability,
            CreatedAt = DateTime.UtcNow
        };

        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        return Results.Created($"/api/profiles/{profile.Id}", profile);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
        return Results.StatusCode(500);
    }
});



// ====================== GET BY ID ======================
app.MapGet("/api/profiles/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var profile = await db.Profiles.FindAsync(id);

    return profile == null
        ? Results.NotFound()
        : Results.Ok(profile);
});



// ====================== GET ALL ======================
app.MapGet("/api/profiles", async (AppDbContext db) =>
{
    var data = await db.Profiles.ToListAsync();

    return Results.Ok(new
    {
        status = "success",
        count = data.Count,
        data
    });
});



// ====================== DELETE ======================
app.MapDelete("/api/profiles/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var profile = await db.Profiles.FindAsync(id);

    if (profile == null)
        return Results.NotFound();

    db.Profiles.Remove(profile);
    await db.SaveChangesAsync();

    return Results.NoContent();
});



app.Run();