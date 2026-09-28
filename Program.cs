using Microsoft.EntityFrameworkCore;
using RoshedTehran.BackgroundServices;
using RoshedTehran.Data;
using RoshedTehran.Services;

namespace RoshedTehran;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "RoshedTehran API",
                Version = "v1",
                Description = "Online/offline business-contact search, backed by Postgres + Qdrant."
            });
        });

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<QdrantService>();
        builder.Services.AddScoped<EmbederService>();
        builder.Services.AddScoped<SemanticSearchClient>();
        builder.Services.AddScoped<ScraperService>();
        builder.Services.AddScoped<CommonCrawlService>();

        builder.Services.AddHostedService<CommonCrawlBackgroundService>();
        builder.Services.AddHostedService<DataRefreshBackgroundService>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "RoshedTehran API v1"));
        }
        else
        {
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthorization();
        app.MapControllers();

        app.MapGet("/error", () => Results.Problem("An unexpected error occurred."));

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.Migrate();

            var qdrant = scope.ServiceProvider.GetRequiredService<QdrantService>();
            qdrant.InitializeAsync().GetAwaiter().GetResult();
        }

        app.Run();
    }
}
