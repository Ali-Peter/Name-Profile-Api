using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new Exception("DATABASE_URL is missing");
        }

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

        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}