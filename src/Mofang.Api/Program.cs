using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Mofang.Api.Security;
using Mofang.Contracts;
using Mofang.Infrastructure;
using Mofang.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.BearerTokenExpiration = TimeSpan.FromHours(2);
    options.RefreshTokenExpiration = TimeSpan.FromDays(14);
});
builder.Services.AddAuthorization(options =>
{
    var activeAccount = new ActiveAccountRequirement();
    options.DefaultPolicy = new AuthorizationPolicyBuilder(IdentityConstants.BearerScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(activeAccount)
        .Build();
    options.AddPolicy(MofangPolicies.MasterAdmin, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.BearerScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveAccountRequirement())
        .RequireClaim(MofangClaimTypes.MasterAdmin, "true"));
});
builder.Services.AddScoped<IAuthorizationHandler, ActiveAccountHandler>();
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MofangDam", "data-protection-keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("Mofang.Dam");
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.MemoryBufferThreshold = 64 * 1024;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddMofangInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = exception switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
    context.Response.StatusCode = status;
    await Results.Problem(
        statusCode: status,
        title: status == 500 ? "服务器内部错误" : exception?.Message,
        detail: app.Environment.IsDevelopment() && status == 500 ? exception?.Message : null).ExecuteAsync(context);
}));
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapControllers();
app.MapPost("/api/auth/refresh", async (RefreshTokenRequest request, IOptionsMonitor<BearerTokenOptions> bearerOptions, SignInManager<ApplicationUser> signInManager) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken)) return Results.BadRequest();
    var options = bearerOptions.Get(IdentityConstants.BearerScheme);
    var ticket = options.RefreshTokenProtector.Unprotect(request.RefreshToken);
    if (ticket?.Properties.ExpiresUtc is not { } expiresUtc || TimeProvider.System.GetUtcNow() >= expiresUtc || await signInManager.ValidateSecurityStampAsync(ticket.Principal) is not ApplicationUser user || !user.IsEnabled)
        return Results.Unauthorized();
    var principal = await signInManager.CreateUserPrincipalAsync(user);
    return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
}).AllowAnonymous();

await app.Services.InitializeMofangAsync();
app.Run();

public partial class Program;
