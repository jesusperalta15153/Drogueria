using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Drogueria.Data;

/// <summary>
/// Permite que "dotnet ef migrations add" y "dotnet ef database update" creen el DbContext en design-time.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContex>
{
    public AppDbContex CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true);

        var config = builder.Build();
        var connectionString = config.GetConnectionString("DefaultConnection") 
            ?? "Server=localhost;Port=3306;Database=Drogueria;User=root;Password=;";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContex>();

        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 30)));

        return new AppDbContex(optionsBuilder.Options);
    }
}
