using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessAtlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProvenances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Uri = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    License = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    VersionOrCommit = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Checksum = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RetrievedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RedistributionAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProvenances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Openings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CanonicalName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TerminalName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    FamilyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    EcoCodeValue = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentOpeningId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Openings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Openings_Openings_ParentOpeningId",
                        column: x => x.ParentOpeningId,
                        principalTable: "Openings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpeningId = table.Column<int>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningAliases_Openings_OpeningId",
                        column: x => x.OpeningId,
                        principalTable: "Openings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpeningLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpeningId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentLineId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EcoCodeValue = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    StartingFen = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SanText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    UciText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    PlyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TerminalPositionKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    KindOrigin = table.Column<int>(type: "INTEGER", nullable: false),
                    VerificationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    Victim = table.Column<int>(type: "INTEGER", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ProvenanceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningLines_DataProvenances_ProvenanceId",
                        column: x => x.ProvenanceId,
                        principalTable: "DataProvenances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OpeningLines_OpeningLines_ParentLineId",
                        column: x => x.ParentLineId,
                        principalTable: "OpeningLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningLines_Openings_OpeningId",
                        column: x => x.OpeningId,
                        principalTable: "Openings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataProvenances_Provider_VersionOrCommit",
                table: "DataProvenances",
                columns: new[] { "Provider", "VersionOrCommit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAliases_Alias",
                table: "OpeningAliases",
                column: "Alias");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningAliases_OpeningId_Alias_Language",
                table: "OpeningAliases",
                columns: new[] { "OpeningId", "Alias", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_EcoCodeValue",
                table: "OpeningLines",
                column: "EcoCodeValue");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_Kind",
                table: "OpeningLines",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_OpeningId_StartingFen_UciText",
                table: "OpeningLines",
                columns: new[] { "OpeningId", "StartingFen", "UciText" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_ParentLineId",
                table: "OpeningLines",
                column: "ParentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_ProvenanceId",
                table: "OpeningLines",
                column: "ProvenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_TerminalPositionKey",
                table: "OpeningLines",
                column: "TerminalPositionKey");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningLines_VerificationStatus",
                table: "OpeningLines",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Openings_CanonicalName",
                table: "Openings",
                column: "CanonicalName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Openings_Depth",
                table: "Openings",
                column: "Depth");

            migrationBuilder.CreateIndex(
                name: "IX_Openings_EcoCodeValue",
                table: "Openings",
                column: "EcoCodeValue");

            migrationBuilder.CreateIndex(
                name: "IX_Openings_FamilyKey",
                table: "Openings",
                column: "FamilyKey");

            migrationBuilder.CreateIndex(
                name: "IX_Openings_ParentOpeningId",
                table: "Openings",
                column: "ParentOpeningId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpeningAliases");

            migrationBuilder.DropTable(
                name: "OpeningLines");

            migrationBuilder.DropTable(
                name: "DataProvenances");

            migrationBuilder.DropTable(
                name: "Openings");
        }
    }
}
