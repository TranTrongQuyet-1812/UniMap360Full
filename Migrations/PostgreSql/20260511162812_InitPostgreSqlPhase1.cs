using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class InitPostgreSqlPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            // --- MANUAL SQL: CREATE FUNCTIONS FOR TRIGGERS ---
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_LogStatusChange() RETURNS TRIGGER AS $$
                BEGIN
                    IF (TG_OP = 'UPDATE' AND OLD.""Status"" IS DISTINCT FROM NEW.""Status"") THEN
                        INSERT INTO ""NhatKyHeThong"" (""AccountID"", ""LogAction"", ""ActionTime"")
                        VALUES (
                            CASE 
                                WHEN TG_TABLE_NAME = 'HoSoUngTuyen' THEN NEW.""StudentID""
                                WHEN TG_TABLE_NAME = 'LichXemPhong' THEN NEW.""StudentID""
                                ELSE NULL
                            END,
                            'Thay đổi trạng thái ' || TG_TABLE_NAME || ' ID ' || 
                            CASE 
                                WHEN TG_TABLE_NAME = 'HoSoUngTuyen' THEN NEW.""ApplicationID""::text
                                WHEN TG_TABLE_NAME = 'LichXemPhong' THEN NEW.""AppointmentID""::text
                                ELSE '?'
                            END || ' sang ' || NEW.""Status"",
                            NOW()
                        );
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");


            migrationBuilder.CreateTable(
                name: "CaiDatUngDung",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CuocTroChuyen",
                columns: table => new
                {
                    ConversationID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "direct"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.ConversationID);
                });

            migrationBuilder.CreateTable(
                name: "DanhMuc",
                columns: table => new
                {
                    CategoryID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Categori__19093A2BACFFCD62", x => x.CategoryID);
                });

            migrationBuilder.CreateTable(
                name: "DiaDiem",
                columns: table => new
                {
                    LocationID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AddressText = table.Column<string>(type: "text", nullable: false),
                    ProvinceCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProvinceName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DistrictCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DistrictName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    WardCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    WardName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    HouseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Street = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FullAddressNormalized = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Coordinates = table.Column<Geometry>(type: "geometry", nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Location__E7FEA477A66ECEEE", x => x.LocationID);
                });

            migrationBuilder.CreateTable(
                name: "NhatKyKiemToanQuanTri",
                columns: table => new
                {
                    AuditID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdminAccountID = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetID = table.Column<int>(type: "integer", nullable: true),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.AuditID);
                });

            migrationBuilder.CreateTable(
                name: "TaiKhoan",
                columns: table => new
                {
                    AccountID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "now()"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LockedReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Accounts__349DA58670C35F96", x => x.AccountID);
                });

            migrationBuilder.CreateTable(
                name: "TepDaPhuongTien",
                columns: table => new
                {
                    MediaID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetID = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    MediaURL = table.Column<string>(type: "text", nullable: false),
                    IsThumbnail = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Media__B2C2B5AF03484D12", x => x.MediaID);
                });

            migrationBuilder.CreateTable(
                name: "HoSoChuTro",
                columns: table => new
                {
                    HostID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountID = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(15)", unicode: false, maxLength: 15, nullable: true),
                    IDCard = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__HostProf__08D4870CF2133F30", x => x.HostID);
                    table.ForeignKey(
                        name: "FK_Host_Acc",
                        column: x => x.AccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HoSoNhaTuyenDung",
                columns: table => new
                {
                    EmployerID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountID = table.Column<int>(type: "integer", nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    Website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Employer__CA445241FBAC3731", x => x.EmployerID);
                    table.ForeignKey(
                        name: "FK_Emp_Acc",
                        column: x => x.AccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HoSoSinhVien",
                columns: table => new
                {
                    StudentID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountID = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    University = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Major = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CVLink = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__StudentP__32C52A79F5EEC15A", x => x.StudentID);
                    table.ForeignKey(
                        name: "FK_Student_Acc",
                        column: x => x.AccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NhatKyHeThong",
                columns: table => new
                {
                    LogID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountID = table.Column<int>(type: "integer", nullable: true),
                    LogAction = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ActionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SystemLo__5E5499A8D6B10865", x => x.LogID);
                    table.ForeignKey(
                        name: "FK_Log_Acc",
                        column: x => x.AccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ThanhVienCuocTroChuyen",
                columns: table => new
                {
                    ConversationID = table.Column<int>(type: "integer", nullable: false),
                    AccountID = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastReadMessageID = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationParticipants", x => new { x.ConversationID, x.AccountID });
                    table.ForeignKey(
                        name: "FK_ConversationParticipants_Accounts",
                        column: x => x.AccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationParticipants_Conversations",
                        column: x => x.ConversationID,
                        principalTable: "CuocTroChuyen",
                        principalColumn: "ConversationID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThongBao",
                columns: table => new
                {
                    NotificationID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientAccountID = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TargetID = table.Column<int>(type: "integer", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetaJSON = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationID);
                    table.ForeignKey(
                        name: "FK_Notifications_Accounts",
                        column: x => x.RecipientAccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TinNhan",
                columns: table => new
                {
                    MessageID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationID = table.Column<int>(type: "integer", nullable: false),
                    SenderAccountID = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.MessageID);
                    table.ForeignKey(
                        name: "FK_Messages_Accounts",
                        column: x => x.SenderAccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations",
                        column: x => x.ConversationID,
                        principalTable: "CuocTroChuyen",
                        principalColumn: "ConversationID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhongTro",
                columns: table => new
                {
                    RoomID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostID = table.Column<int>(type: "integer", nullable: false),
                    LocationID = table.Column<int>(type: "integer", nullable: false),
                    CategoryID = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Area = table.Column<double>(type: "double precision", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    RoomStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: "Available"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "now()"),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    SourceURL = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Rooms__32863919894EE30F", x => x.RoomID);
                    table.ForeignKey(
                        name: "FK_Room_Cat",
                        column: x => x.CategoryID,
                        principalTable: "DanhMuc",
                        principalColumn: "CategoryID");
                    table.ForeignKey(
                        name: "FK_Room_Host",
                        column: x => x.HostID,
                        principalTable: "HoSoChuTro",
                        principalColumn: "HostID");
                    table.ForeignKey(
                        name: "FK_Room_Loc",
                        column: x => x.LocationID,
                        principalTable: "DiaDiem",
                        principalColumn: "LocationID");
                });

            migrationBuilder.CreateTable(
                name: "ViecLam",
                columns: table => new
                {
                    JobID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployerID = table.Column<int>(type: "integer", nullable: false),
                    LocationID = table.Column<int>(type: "integer", nullable: false),
                    CategoryID = table.Column<int>(type: "integer", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SalaryRange = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    JobType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    JobStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: "Open"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "now()"),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    SourceURL = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Jobs__056690E21424BEAF", x => x.JobID);
                    table.ForeignKey(
                        name: "FK_Job_Cat",
                        column: x => x.CategoryID,
                        principalTable: "DanhMuc",
                        principalColumn: "CategoryID");
                    table.ForeignKey(
                        name: "FK_Job_Emp",
                        column: x => x.EmployerID,
                        principalTable: "HoSoNhaTuyenDung",
                        principalColumn: "EmployerID");
                    table.ForeignKey(
                        name: "FK_Job_Loc",
                        column: x => x.LocationID,
                        principalTable: "DiaDiem",
                        principalColumn: "LocationID");
                });

            migrationBuilder.CreateTable(
                name: "DanhGia",
                columns: table => new
                {
                    ReviewID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentID = table.Column<int>(type: "integer", nullable: false),
                    TargetID = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Reviews__74BC79AEF54E702E", x => x.ReviewID);
                    table.ForeignKey(
                        name: "FK_Review_Student",
                        column: x => x.StudentID,
                        principalTable: "HoSoSinhVien",
                        principalColumn: "StudentID");
                });

            migrationBuilder.CreateTable(
                name: "RoommatePost",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TargetGender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BudgetPerMonth = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Habits = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AreaPreference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoommatePost", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoommatePosts_StudentProfile",
                        column: x => x.StudentId,
                        principalTable: "HoSoSinhVien",
                        principalColumn: "StudentID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YeuThich",
                columns: table => new
                {
                    FavoriteID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentID = table.Column<int>(type: "integer", nullable: false),
                    TargetID = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Favorite__CE74FAF5B7788E00", x => x.FavoriteID);
                    table.ForeignKey(
                        name: "FK_Fav_Student",
                        column: x => x.StudentID,
                        principalTable: "HoSoSinhVien",
                        principalColumn: "StudentID");
                });

            migrationBuilder.CreateTable(
                name: "LichXemPhong",
                columns: table => new
                {
                    AppointmentID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomID = table.Column<int>(type: "integer", nullable: false),
                    StudentID = table.Column<int>(type: "integer", nullable: false),
                    HostID = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ContactPhone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    StudentNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HostResponse = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SuggestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomViewingAppointments", x => x.AppointmentID);
                    table.ForeignKey(
                        name: "FK_RoomViewingAppointments_Host",
                        column: x => x.HostID,
                        principalTable: "HoSoChuTro",
                        principalColumn: "HostID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomViewingAppointments_Room",
                        column: x => x.RoomID,
                        principalTable: "PhongTro",
                        principalColumn: "RoomID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomViewingAppointments_Student",
                        column: x => x.StudentID,
                        principalTable: "HoSoSinhVien",
                        principalColumn: "StudentID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HoSoUngTuyen",
                columns: table => new
                {
                    ApplicationID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentID = table.Column<int>(type: "integer", nullable: false),
                    JobID = table.Column<int>(type: "integer", nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CvURL = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CvPublicID = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: "Pending")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__JobAppli__C93A4F791661E97C", x => x.ApplicationID);
                    table.ForeignKey(
                        name: "FK_App_Job",
                        column: x => x.JobID,
                        principalTable: "ViecLam",
                        principalColumn: "JobID");
                    table.ForeignKey(
                        name: "FK_App_Student",
                        column: x => x.StudentID,
                        principalTable: "HoSoSinhVien",
                        principalColumn: "StudentID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_LastMessageAt",
                table: "CuocTroChuyen",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "UX_Review_Student_Target",
                table: "DanhGia",
                columns: new[] { "StudentID", "TargetType", "TargetID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Location_Coordinates",
                table: "DiaDiem",
                column: "Coordinates");

            migrationBuilder.CreateIndex(
                name: "IX_Location_Province_District",
                table: "DiaDiem",
                columns: new[] { "ProvinceName", "DistrictName" });

            migrationBuilder.CreateIndex(
                name: "IX_Location_Province_District_Ward",
                table: "DiaDiem",
                columns: new[] { "ProvinceName", "DistrictName", "WardName" });

            migrationBuilder.CreateIndex(
                name: "IX_Location_ProvinceName",
                table: "DiaDiem",
                column: "ProvinceName");

            migrationBuilder.CreateIndex(
                name: "UQ__HostProf__349DA58749AA4188",
                table: "HoSoChuTro",
                column: "AccountID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__HostProf__43A2A4E30AC952B8",
                table: "HoSoChuTro",
                column: "IDCard",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__Employer__12945A28C67BD57F",
                table: "HoSoNhaTuyenDung",
                column: "TaxCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__Employer__349DA587D082AB93",
                table: "HoSoNhaTuyenDung",
                column: "AccountID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__StudentP__349DA5873B5BC4FE",
                table: "HoSoSinhVien",
                column: "AccountID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HoSoUngTuyen_JobID",
                table: "HoSoUngTuyen",
                column: "JobID");

            migrationBuilder.CreateIndex(
                name: "IX_HoSoUngTuyen_StudentID",
                table: "HoSoUngTuyen",
                column: "StudentID");

            migrationBuilder.CreateIndex(
                name: "IX_RoomViewingAppointments_Host_Status_Scheduled",
                table: "LichXemPhong",
                columns: new[] { "HostID", "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomViewingAppointments_Room_Scheduled",
                table: "LichXemPhong",
                columns: new[] { "RoomID", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomViewingAppointments_Student_Status_Created",
                table: "LichXemPhong",
                columns: new[] { "StudentID", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NhatKyHeThong_AccountID",
                table: "NhatKyHeThong",
                column: "AccountID");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Admin_Time",
                table: "NhatKyKiemToanQuanTri",
                columns: new[] { "AdminAccountID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Target",
                table: "NhatKyKiemToanQuanTri",
                columns: new[] { "TargetType", "TargetID" });

            migrationBuilder.CreateIndex(
                name: "IX_PhongTro_CategoryID",
                table: "PhongTro",
                column: "CategoryID");

            migrationBuilder.CreateIndex(
                name: "IX_PhongTro_HostID",
                table: "PhongTro",
                column: "HostID");

            migrationBuilder.CreateIndex(
                name: "IX_PhongTro_LocationID",
                table: "PhongTro",
                column: "LocationID");

            migrationBuilder.CreateIndex(
                name: "IX_RoommatePost_StudentId",
                table: "RoommatePost",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "UQ__Accounts__A9D1053467B8D8AF",
                table: "TaiKhoan",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationParticipants_Account_Archived_Conversation",
                table: "ThanhVienCuocTroChuyen",
                columns: new[] { "AccountID", "IsArchived", "ConversationID" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationParticipants_Conversation",
                table: "ThanhVienCuocTroChuyen",
                column: "ConversationID");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Recipient_Read_Time",
                table: "ThongBao",
                columns: new[] { "RecipientAccountID", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Target",
                table: "ThongBao",
                columns: new[] { "TargetType", "TargetID" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Conversation_Created",
                table: "TinNhan",
                columns: new[] { "ConversationID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Conversation_Message",
                table: "TinNhan",
                columns: new[] { "ConversationID", "MessageID" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Sender_Created",
                table: "TinNhan",
                columns: new[] { "SenderAccountID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ViecLam_CategoryID",
                table: "ViecLam",
                column: "CategoryID");

            migrationBuilder.CreateIndex(
                name: "IX_ViecLam_EmployerID",
                table: "ViecLam",
                column: "EmployerID");

            migrationBuilder.CreateIndex(
                name: "IX_ViecLam_LocationID",
                table: "ViecLam",
                column: "LocationID");

            migrationBuilder.CreateIndex(
                name: "IX_YeuThich_StudentID",
                table: "YeuThich",
                column: "StudentID");

            // --- MANUAL SQL: CREATE VIEW ---
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW ""v_GlobalMapFeed"" AS
                SELECT 
                    'Room' AS ""ItemType"",
                    r.""RoomID"" AS ""Id"",
                    r.""Title"",
                    r.""Price"" AS ""Value"",
                    l.""AddressText"",
                    ST_Y(l.""Coordinates"") AS ""Latitude"",
                    ST_X(l.""Coordinates"") AS ""Longitude"",
                    c.""CategoryName"",
                    r.""IsExternal"",
                    r.""SourceURL"" AS ""SourceUrl""
                FROM ""PhongTro"" r
                JOIN ""DiaDiem"" l ON r.""LocationID"" = l.""LocationID""
                JOIN ""DanhMuc"" c ON r.""CategoryID"" = c.""CategoryID""
                WHERE r.""RoomStatus"" = 'Available'
                UNION ALL
                SELECT 
                    'Job' AS ""ItemType"",
                    j.""JobID"" AS ""Id"",
                    j.""JobTitle"" AS ""Title"",
                    NULL::numeric AS ""Value"",
                    l.""AddressText"",
                    ST_Y(l.""Coordinates"") AS ""Latitude"",
                    ST_X(l.""Coordinates"") AS ""Longitude"",
                    c.""CategoryName"",
                    j.""IsExternal"",
                    j.""SourceURL"" AS ""SourceUrl""
                FROM ""ViecLam"" j
                JOIN ""DiaDiem"" l ON j.""LocationID"" = l.""LocationID""
                JOIN ""DanhMuc"" c ON j.""CategoryID"" = c.""CategoryID""
                WHERE j.""JobStatus"" = 'Open';
            ");

            // --- MANUAL SQL: ATTACH TRIGGERS ---
            migrationBuilder.Sql(@"
                CREATE TRIGGER tr_HoSoUngTuyen_LogTrangThai
                AFTER UPDATE ON ""HoSoUngTuyen""
                FOR EACH ROW EXECUTE FUNCTION fn_LogStatusChange();

                CREATE TRIGGER tr_LichXemPhong_LogTrangThai
                AFTER UPDATE ON ""LichXemPhong""
                FOR EACH ROW EXECUTE FUNCTION fn_LogStatusChange();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaiDatUngDung");

            migrationBuilder.DropTable(
                name: "DanhGia");

            migrationBuilder.DropTable(
                name: "HoSoUngTuyen");

            migrationBuilder.DropTable(
                name: "LichXemPhong");

            migrationBuilder.DropTable(
                name: "NhatKyHeThong");

            migrationBuilder.DropTable(
                name: "NhatKyKiemToanQuanTri");

            migrationBuilder.DropTable(
                name: "RoommatePost");

            migrationBuilder.DropTable(
                name: "TepDaPhuongTien");

            migrationBuilder.DropTable(
                name: "ThanhVienCuocTroChuyen");

            migrationBuilder.DropTable(
                name: "ThongBao");

            migrationBuilder.DropTable(
                name: "TinNhan");

            migrationBuilder.DropTable(
                name: "YeuThich");

            migrationBuilder.DropTable(
                name: "ViecLam");

            migrationBuilder.DropTable(
                name: "PhongTro");

            migrationBuilder.DropTable(
                name: "CuocTroChuyen");

            migrationBuilder.DropTable(
                name: "HoSoSinhVien");

            migrationBuilder.DropTable(
                name: "HoSoNhaTuyenDung");

            migrationBuilder.DropTable(
                name: "DanhMuc");

            migrationBuilder.DropTable(
                name: "HoSoChuTro");

            migrationBuilder.DropTable(
                name: "DiaDiem");

            migrationBuilder.DropTable(
                name: "TaiKhoan");

            // --- MANUAL SQL: DROP VIEW AND TRIGGERS ---
            migrationBuilder.Sql(@"
                DROP VIEW IF EXISTS ""v_GlobalMapFeed"";
                DROP TRIGGER IF EXISTS tr_HoSoUngTuyen_LogTrangThai ON ""HoSoUngTuyen"";
                DROP TRIGGER IF EXISTS tr_LichXemPhong_LogTrangThai ON ""LichXemPhong"";
                DROP FUNCTION IF EXISTS fn_LogStatusChange();
            ");
        }
    }
}
