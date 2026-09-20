using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mofang.Domain;
using Mofang.Infrastructure.Identity;

namespace Mofang.Infrastructure.Persistence;

public sealed class MofangDbContext(DbContextOptions<MofangDbContext> options) : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<LibraryIdentity> LibraryIdentities => Set<LibraryIdentity>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetVersion> AssetVersions => Set<AssetVersion>();
    public DbSet<StorageObject> StorageObjects => Set<StorageObject>();
    public DbSet<MinioConfiguration> MinioConfigurations => Set<MinioConfiguration>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();
    public DbSet<DirectoryPermission> DirectoryPermissions => Set<DirectoryPermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LibraryIdentity>(entity =>
        {
            entity.ToTable("library_identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => x.LibraryId).IsUnique();
        });

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("accounts");
            entity.Property(x => x.UserName).HasMaxLength(50);
            entity.Property(x => x.NormalizedUserName).HasMaxLength(50);
            entity.Property(x => x.DisplayName).HasMaxLength(50);
        });
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("account_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("account_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("account_tokens");

        modelBuilder.Entity<Folder>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(255);
            entity.Property(x => x.CreatedBy).HasMaxLength(100);
            entity.HasIndex(x => new { x.ParentId, x.Name, x.Status });
            entity.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("assets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(500);
            entity.Property(x => x.OriginalFileName).HasMaxLength(500);
            entity.Property(x => x.Extension).HasMaxLength(32);
            entity.Property(x => x.MimeType).HasMaxLength(255);
            entity.Property(x => x.Bucket).HasMaxLength(128);
            entity.Property(x => x.ObjectKey).HasMaxLength(1024);
            entity.Property(x => x.Hash).HasMaxLength(128);
            entity.Property(x => x.CreatedBy).HasMaxLength(100);
            entity.Property(x => x.ThumbnailBucket).HasMaxLength(128);
            entity.Property(x => x.ThumbnailObjectKey).HasMaxLength(1024);
            entity.HasIndex(x => new { x.FolderId, x.FileName, x.Status });
            entity.HasIndex(x => x.AssetType);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasOne(x => x.Folder).WithMany(x => x.Assets).HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StorageObject>(entity =>
        {
            entity.ToTable("storage_objects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(50);
            entity.Property(x => x.Bucket).HasMaxLength(128);
            entity.Property(x => x.ObjectKey).HasMaxLength(1024);
            entity.Property(x => x.Hash).HasMaxLength(128);
            entity.HasIndex(x => new { x.Bucket, x.ObjectKey }).IsUnique();
        });

        modelBuilder.Entity<MinioConfiguration>(entity =>
        {
            entity.ToTable("minio_configuration");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ServiceUrl).HasMaxLength(2048);
            entity.Property(x => x.PublicUrl).HasMaxLength(2048);
            entity.Property(x => x.AccessKey).HasMaxLength(255);
            entity.Property(x => x.AssetBucket).HasMaxLength(63);
            entity.Property(x => x.ThumbnailBucket).HasMaxLength(63);
            entity.Property(x => x.UpdatedBy).HasMaxLength(100);
        });

        modelBuilder.Entity<AssetVersion>(entity =>
        {
            entity.ToTable("asset_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(500);
            entity.Property(x => x.MimeType).HasMaxLength(255);
            entity.Property(x => x.Hash).HasMaxLength(128);
            entity.Property(x => x.CreatedBy).HasMaxLength(100);
            entity.HasIndex(x => new { x.AssetId, x.VersionNumber }).IsUnique();
            entity.HasOne(x => x.Asset).WithMany(x => x.Versions).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StorageObject).WithMany(x => x.Versions).HasForeignKey(x => x.StorageObjectId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OperationLog>(entity =>
        {
            entity.ToTable("operation_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(100);
            entity.Property(x => x.Action).HasMaxLength(50);
            entity.Property(x => x.TargetType).HasMaxLength(50);
            entity.Property(x => x.TargetName).HasMaxLength(500);
            entity.Property(x => x.FolderPath).HasMaxLength(2048);
            entity.Property(x => x.PreviousName).HasMaxLength(500);
            entity.Property(x => x.NewName).HasMaxLength(500);
            entity.Property(x => x.Detail).HasColumnType("jsonb");
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => x.FolderId);
            entity.HasIndex(x => x.TargetName);
            entity.HasIndex(x => new { x.TargetType, x.TargetId });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DirectoryPermission>(entity =>
        {
            entity.ToTable("directory_permissions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.AccountId, x.FolderId }).IsUnique();
            entity.HasIndex(x => x.AccountId).IsUnique().HasFilter("\"FolderId\" IS NULL");
            entity.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
