using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mofang.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StableLibraryIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_identity", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_identity_LibraryId",
                table: "library_identity",
                column: "LibraryId",
                unique: true);

            // Generate once per database, never from the client name, host, or storage address.
            migrationBuilder.Sql("INSERT INTO library_identity (\"Id\", \"LibraryId\") VALUES (1, gen_random_uuid());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_identity");
        }
    }
}
