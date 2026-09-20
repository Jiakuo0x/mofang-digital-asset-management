using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Domain;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;
using Mofang.Infrastructure.Services;

namespace Mofang.UnitTests;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task RenameChangesLoginLookupAndCurrentClaimsButKeepsPasswordAndPermissions()
    {
        await using var test = await Fixture.CreateAsync();
        var result = await test.Service.UpdateAccountAsync(test.Reader.Id, new("新名称", true, " renamed "), test.Admin, default);
        Assert.Equal("renamed", result.UserName);
        Assert.Null(await test.Users.FindByNameAsync("reader"));
        var renamed = await test.Users.FindByNameAsync("RENAMED");
        Assert.NotNull(renamed);
        Assert.True(await test.Users.CheckPasswordAsync(renamed, "test-password"));
        Assert.Single(await test.Db.DirectoryPermissions.Where(x => x.AccountId == test.Reader.Id).ToListAsync());

        var authorization = await test.AuthorizeAsync(test.Reader);
        Assert.True(authorization.HasSucceeded);
        Assert.Equal("renamed", authorization.User.ToAccountContext().UserName);
        Assert.Equal("新名称", authorization.User.ToAccountContext().DisplayName);
        var log = await test.Db.OperationLogs.SingleAsync(x => x.Action == "UpdateAccount");
        Assert.Contains("PreviousUserName", log.Detail);
        Assert.Contains("reader", log.Detail);
    }

    [Fact]
    public async Task DuplicateNameIsRejectedWithoutChangingStoredAccount()
    {
        await using var test = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<ArgumentException>(() => test.Service.UpdateAccountAsync(test.Reader.Id, new("不应保存", false, "ADMIN"), test.Admin, default));
        Assert.Contains("账号名称已存在", error.Message);
        test.Db.ChangeTracker.Clear();
        var stored = await test.Db.Users.SingleAsync(x => x.Id == test.Reader.Id);
        Assert.Equal("reader", stored.UserName);
        Assert.Equal("Reader", stored.DisplayName);
        Assert.True(stored.IsEnabled);
        Assert.Empty(await test.Db.OperationLogs.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("with space")]
    [InlineData("line\nbreak")]
    public async Task InvalidNamesAreRejected(string name)
    {
        await using var test = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => test.Service.UpdateAccountAsync(test.Reader.Id, new("Reader", true, name), test.Admin, default));
        Assert.Equal("reader", (await test.Db.Users.AsNoTracking().SingleAsync(x => x.Id == test.Reader.Id)).UserName);
    }

    [Fact]
    public async Task OlderClientsCanStillUpdateDisplayNameAndDisableLogin()
    {
        await using var test = await Fixture.CreateAsync();
        var result = await test.Service.UpdateAccountAsync(test.Reader.Id, new("改名", false), test.Admin, default);
        Assert.Equal("reader", result.UserName);
        Assert.False(result.IsEnabled);
        Assert.False((await test.AuthorizeAsync(test.Reader)).HasSucceeded);
    }

    [Fact]
    public async Task DeleteRemovesIdentityAndGrantsButRetainsAssetsAndHistoricalLogs()
    {
        await using var test = await Fixture.CreateAsync();
        await test.Service.RecordLoginAsync(test.Reader, default);
        var user = await test.Users.FindByIdAsync(test.Reader.Id.ToString());
        Assert.True((await test.Users.AddClaimAsync(user!, new Claim("test", "value"))).Succeeded);
        Assert.True((await test.Users.SetAuthenticationTokenAsync(user!, "test", "token", "value")).Succeeded);
        await test.Service.DeleteAccountAsync(test.Reader.Id, test.Admin, default);
        test.Db.ChangeTracker.Clear();

        Assert.Null(await test.Users.FindByNameAsync("reader"));
        Assert.Empty(await test.Db.DirectoryPermissions.ToListAsync());
        Assert.Empty(await test.Db.Set<IdentityUserClaim<Guid>>().ToListAsync());
        Assert.Empty(await test.Db.Set<IdentityUserToken<Guid>>().ToListAsync());
        Assert.Single(await test.Db.Folders.ToListAsync());
        Assert.Equal("reader", (await test.Db.Assets.SingleAsync()).CreatedBy);
        var historical = await test.Db.OperationLogs.SingleAsync(x => x.Action == "Login");
        Assert.Null(historical.AccountId);
        Assert.Equal("reader", historical.UserId);
        var deletion = await test.Db.OperationLogs.SingleAsync(x => x.Action == "DeleteAccount");
        Assert.Equal(test.Admin.Id, deletion.AccountId);
        Assert.Equal(test.Reader.Id, deletion.TargetId);
        Assert.False((await test.AuthorizeAsync(test.Reader)).HasSucceeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => test.Service.GetSessionAsync(test.Reader, default));
    }

    [Fact]
    public async Task MasterAndCurrentAccountCannotBeDeletedOrDisabled()
    {
        await using var test = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Service.DeleteAccountAsync(test.Admin.Id, test.Admin, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Service.DeleteAccountAsync(test.Reader.Id, test.Reader, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Service.UpdateAccountAsync(test.Admin.Id, new("Admin", false), test.Admin, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Service.UpdateAccountAsync(test.Reader.Id, new("Reader", false), test.Reader, default));
        Assert.Equal(2, await test.Db.Users.CountAsync());
    }

    [Fact]
    public async Task MissingAccountReturnsNotFound()
    {
        await using var test = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service.DeleteAccountAsync(Guid.NewGuid(), test.Admin, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service.UpdateAccountAsync(Guid.NewGuid(), new("Missing", true), test.Admin, default));
    }

    private sealed class Fixture(SqliteConnection connection, ServiceProvider provider) : IAsyncDisposable
    {
        public MofangDbContext Db { get; } = provider.GetRequiredService<MofangDbContext>();
        public UserManager<ApplicationUser> Users { get; } = provider.GetRequiredService<UserManager<ApplicationUser>>();
        public AccountContext Admin { get; } = new(Guid.NewGuid(), "admin", "Admin", true);
        public AccountContext Reader { get; } = new(Guid.NewGuid(), "reader", "Reader", false);
        public AccountService Service => new(Db, Users, new DirectoryAccessService(Db));

        public async Task<AuthorizationHandlerContext> AuthorizeAsync(AccountContext account)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.UserName),
                new Claim(MofangClaimTypes.DisplayName, account.DisplayName)
            ], "test"));
            var context = new AuthorizationHandlerContext([new ActiveAccountRequirement()], principal, null);
            await new ActiveAccountHandler(Db).HandleAsync(context);
            return context;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<MofangDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.AllowedUserNameCharacters = string.Empty;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            }).AddEntityFrameworkStores<MofangDbContext>();
            var test = new Fixture(connection, services.BuildServiceProvider());
            await test.Db.Database.EnsureCreatedAsync();
            foreach (var account in new[] { test.Admin, test.Reader })
                Assert.True((await test.Users.CreateAsync(new ApplicationUser
                {
                    Id = account.Id, UserName = account.UserName, DisplayName = account.DisplayName,
                    IsMasterAdmin = account.IsMasterAdmin, IsEnabled = true,
                }, "test-password")).Succeeded);
            var folder = new Folder { Id = Guid.NewGuid(), Name = "资料", CreatedBy = "reader" };
            test.Db.Folders.Add(folder);
            test.Db.Assets.Add(new Asset { Id = Guid.NewGuid(), FolderId = folder.Id, FileName = "文档.txt", OriginalFileName = "文档.txt", Extension = ".txt", MimeType = "text/plain", Bucket = "test", ObjectKey = "test", Hash = "test", CreatedBy = "reader" });
            test.Db.DirectoryPermissions.Add(new DirectoryPermission { Id = Guid.NewGuid(), AccountId = test.Reader.Id, FolderId = folder.Id, CanView = true });
            await test.Db.SaveChangesAsync();
            return test;
        }

        public async ValueTask DisposeAsync() { await provider.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
