using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Update03BusinessFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "HoSoNhaTuyenDung",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FeaturedListings",
                columns: table => new
                {
                    FeaturedListingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerAccountId = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    FeatureType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeaturedListings", x => x.FeaturedListingId);
                    table.ForeignKey(
                        name: "FK_FeaturedListings_TaiKhoan_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostAnalyticsDailies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    ListingImpressionCount = table.Column<int>(type: "integer", nullable: false),
                    MapImpressionCount = table.Column<int>(type: "integer", nullable: false),
                    ChatStartedCount = table.Column<int>(type: "integer", nullable: false),
                    FavoriteCount = table.Column<int>(type: "integer", nullable: false),
                    AppointmentCount = table.Column<int>(type: "integer", nullable: false),
                    ApplicationCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostAnalyticsDailies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostAnalyticsEvents",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    OwnerAccountId = table.Column<int>(type: "integer", nullable: false),
                    ActorAccountId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourcePage = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostAnalyticsEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_PostAnalyticsEvents_TaiKhoan_ActorAccountId",
                        column: x => x.ActorAccountId,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PostAnalyticsEvents_TaiKhoan_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoleScope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PriceVnd = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BillingCycle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.PlanId);
                });

            migrationBuilder.CreateTable(
                name: "AccountSubscriptions",
                columns: table => new
                {
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSubscriptions", x => x.SubscriptionId);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptions_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountSubscriptions_TaiKhoan_AccountId",
                        column: x => x.AccountId,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_AccountId",
                table: "AccountSubscriptions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSubscriptions_PlanId",
                table: "AccountSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedListings_Owner_Type_Status",
                table: "FeaturedListings",
                columns: new[] { "OwnerAccountId", "FeatureType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedListings_Status_EndsAt",
                table: "FeaturedListings",
                columns: new[] { "Status", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedListings_Target_Type_Status",
                table: "FeaturedListings",
                columns: new[] { "TargetType", "TargetId", "FeatureType", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_PostAnalyticsDaily_Target_Date",
                table: "PostAnalyticsDailies",
                columns: new[] { "TargetType", "TargetId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostAnalyticsEvents_ActorAccountId",
                table: "PostAnalyticsEvents",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PostAnalyticsEvents_Owner_OccurredAt",
                table: "PostAnalyticsEvents",
                columns: new[] { "OwnerAccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostAnalyticsEvents_Target_OccurredAt",
                table: "PostAnalyticsEvents",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostAnalyticsEvents_Type_OccurredAt",
                table: "PostAnalyticsEvents",
                columns: new[] { "EventType", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSubscriptions");

            migrationBuilder.DropTable(
                name: "FeaturedListings");

            migrationBuilder.DropTable(
                name: "PostAnalyticsDailies");

            migrationBuilder.DropTable(
                name: "PostAnalyticsEvents");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "HoSoNhaTuyenDung");
        }
    }
}
