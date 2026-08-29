using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mofang.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableMinioStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "minio_configuration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PublicUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ProtectedSecretKey = table.Column<string>(type: "text", nullable: false),
                    AssetBucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    ThumbnailBucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_minio_configuration", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "minio_configuration");
        }
    }
}
