using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mofang.Application;
using Mofang.Infrastructure.Jobs;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;
using Mofang.Infrastructure.Services;
using Mofang.Infrastructure.Storage;

namespace Mofang.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMofangInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mofang") ?? throw new InvalidOperationException("ConnectionStrings:Mofang is required.");
        services.AddDbContext<MofangDbContext>(options => options.UseNpgsql(connectionString));
        services.AddIdentityApiEndpoints<ApplicationUser>(options =>
            {
                options.User.AllowedUserNameCharacters = string.Empty;
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 3;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddEntityFrameworkStores<MofangDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, MofangClaimsPrincipalFactory>();
        services.AddOptions<MinioOptions>()
            .Bind(configuration.GetSection(MinioOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.Endpoint), "MinIO endpoint is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.AccessKey), "MinIO access key is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.SecretKey), "MinIO secret key is required.")
            .ValidateOnStart();
        services.AddSingleton<IThumbnailQueue, ThumbnailQueue>();
        services.AddScoped<MinioConfigurationService>();
        services.AddScoped<IMinioConfigurationProvider>(provider => provider.GetRequiredService<MinioConfigurationService>());
        services.AddScoped<IMinioAdministrationService>(provider => provider.GetRequiredService<MinioConfigurationService>());
        services.AddScoped<IAssetStorage, MinioAssetStorage>();
        services.AddScoped<IDamService, DamService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IDirectoryAccessService, DirectoryAccessService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddHostedService<ThumbnailWorker>();
        return services;
    }
}
