using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace UniMap360.Models;

public partial class UniMap360ProContext : DbContext
{
    public UniMap360ProContext()
    {
    }

    public UniMap360ProContext(DbContextOptions<UniMap360ProContext> options)
        : base(options)
    {
    }

    [DbFunction("f_unaccent", IsBuiltIn = false, Schema = "public")]
    public static string Unaccent(string value) => throw new NotSupportedException();

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<AdminAuditLog> AdminAuditLogs { get; set; }

    public virtual DbSet<AppSetting> AppSettings { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Conversation> Conversations { get; set; }

    public virtual DbSet<ConversationParticipant> ConversationParticipants { get; set; }

    public virtual DbSet<EmployerProfile> EmployerProfiles { get; set; }

    public virtual DbSet<Favorite> Favorites { get; set; }

    public virtual DbSet<HostProfile> HostProfiles { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<JobApplication> JobApplications { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<Medium> Media { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<Room> Rooms { get; set; }

    public virtual DbSet<RoomViewingAppointment> RoomViewingAppointments { get; set; }

    public virtual DbSet<RoommatePost> RoommatePosts { get; set; }

    public virtual DbSet<ContentReport> ContentReports { get; set; }

    public virtual DbSet<StudentProfile> StudentProfiles { get; set; }

    public virtual DbSet<SystemLog> SystemLogs { get; set; }

    public virtual DbSet<VGlobalMapFeed> VGlobalMapFeeds { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection", x => x.UseNetTopologySuite());
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.HasPostgresExtension("pg_trgm");

        var isPostgres = Database.IsNpgsql();
        var currentTimestampSql = isPostgres ? "now()" : "getdate()";
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("TaiKhoan");
            entity.HasKey(e => e.AccountId).HasName("PK__Accounts__349DA58670C35F96");

            entity.HasIndex(e => e.Email, "UQ__Accounts__A9D1053467B8D8AF").IsUnique();

            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsLocked).HasDefaultValue(false);
            entity.Property(e => e.LockedReason).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.UserRole).HasMaxLength(20);
            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("PK_AdminAuditLogs");

            entity.ToTable("NhatKyKiemToanQuanTri");

            entity.HasIndex(e => new { e.AdminAccountId, e.CreatedAt }, "IX_AdminAuditLogs_Admin_Time");
            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_AdminAuditLogs_Target");

            entity.Property(e => e.AuditId).HasColumnName("AuditID");
            entity.Property(e => e.AdminAccountId).HasColumnName("AdminAccountID");
            entity.Property(e => e.Action).HasMaxLength(100);
            entity.Property(e => e.TargetType).HasMaxLength(30);
            entity.Property(e => e.TargetId).HasColumnName("TargetID");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("PK_AppSettings");

            entity.ToTable("CaiDatUngDung");

            entity.Property(e => e.Key).HasMaxLength(100);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("DanhMuc");
            entity.HasKey(e => e.CategoryId).HasName("PK__Categori__19093A2BACFFCD62");

            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.CategoryType).HasMaxLength(20);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.ConversationId).HasName("PK_Conversations");

            entity.ToTable("CuocTroChuyen");

            entity.HasIndex(e => e.LastMessageAt, "IX_Conversations_LastMessageAt");

            entity.Property(e => e.ConversationId).HasColumnName("ConversationID");
            entity.Property(e => e.Kind)
                .HasMaxLength(20)
                .HasDefaultValue("direct");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
        });

        modelBuilder.Entity<ConversationParticipant>(entity =>
        {
            entity.HasKey(e => new { e.ConversationId, e.AccountId }).HasName("PK_ConversationParticipants");

            entity.ToTable("ThanhVienCuocTroChuyen");

            entity.HasIndex(e => new { e.AccountId, e.IsArchived, e.ConversationId }, "IX_ConversationParticipants_Account_Archived_Conversation");
            entity.HasIndex(e => e.ConversationId, "IX_ConversationParticipants_Conversation");

            entity.Property(e => e.ConversationId).HasColumnName("ConversationID");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.JoinedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.LastReadMessageId).HasColumnName("LastReadMessageID");
            entity.Property(e => e.IsArchived).HasDefaultValue(false);

            entity.HasOne(d => d.Account).WithMany(p => p.ConversationParticipants)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ConversationParticipants_Accounts");

            entity.HasOne(d => d.Conversation).WithMany(p => p.ConversationParticipants)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ConversationParticipants_Conversations");
        });

        modelBuilder.Entity<EmployerProfile>(entity =>
        {
            entity.ToTable("HoSoNhaTuyenDung");
            entity.HasKey(e => e.EmployerId).HasName("PK__Employer__CA445241FBAC3731");

            entity.HasIndex(e => e.TaxCode, "UQ__Employer__12945A28C67BD57F").IsUnique();

            entity.HasIndex(e => e.AccountId, "UQ__Employer__349DA587D082AB93").IsUnique();

            entity.Property(e => e.EmployerId).HasColumnName("EmployerID");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.CompanyName).HasMaxLength(150);
            entity.Property(e => e.TaxCode)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Website).HasMaxLength(255);

            entity.HasOne(d => d.Account).WithOne(p => p.EmployerProfile)
                .HasForeignKey<EmployerProfile>(d => d.AccountId)
                .HasConstraintName("FK_Emp_Acc");
        });

        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.ToTable("YeuThich");
            entity.HasKey(e => e.FavoriteId).HasName("PK__Favorite__CE74FAF5B7788E00");

            entity.HasIndex(e => new { e.StudentId, e.TargetType, e.TargetId }, "UX_YeuThich_Student_Target").IsUnique();

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_YeuThich_Target");

            entity.Property(e => e.FavoriteId).HasColumnName("FavoriteID");
            entity.Property(e => e.StudentId).HasColumnName("StudentID");
            entity.Property(e => e.TargetId).HasColumnName("TargetID");
            entity.Property(e => e.TargetType).HasMaxLength(10);

            entity.HasOne(d => d.Student).WithMany(p => p.Favorites)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Fav_Student");
        });

        modelBuilder.Entity<HostProfile>(entity =>
        {
            entity.ToTable("HoSoChuTro");
            entity.HasKey(e => e.HostId).HasName("PK__HostProf__08D4870CF2133F30");

            entity.HasIndex(e => e.AccountId, "UQ__HostProf__349DA58749AA4188").IsUnique();

            entity.HasIndex(e => e.Idcard, "UQ__HostProf__43A2A4E30AC952B8").IsUnique();

            entity.Property(e => e.HostId).HasColumnName("HostID");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Idcard)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("IDCard");
            entity.Property(e => e.IsVerified).HasDefaultValue(false);
            entity.Property(e => e.Phone)
                .HasMaxLength(15)
                .IsUnicode(false);

            entity.HasOne(d => d.Account).WithOne(p => p.HostProfile)
                .HasForeignKey<HostProfile>(d => d.AccountId)
                .HasConstraintName("FK_Host_Acc");
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("ViecLam");
            entity.HasKey(e => e.JobId).HasName("PK__Jobs__056690E21424BEAF");

            entity.HasIndex(e => e.SourceUrl, "UX_ViecLam_SourceURL").IsUnique();

            entity.HasIndex(e => e.JobTitle, "IX_ViecLam_JobTitle_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.HasIndex(e => new { e.JobStatus, e.CreatedAt }, "IX_ViecLam_JobStatus_CreatedAt");

            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.EmployerId).HasColumnName("EmployerID");
            entity.Property(e => e.ContactPhone)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.IsExternal).HasDefaultValue(false);
            entity.Property(e => e.JobStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Open");
            entity.Property(e => e.JobTitle).HasMaxLength(255);
            entity.Property(e => e.JobType).HasMaxLength(20);
            entity.Property(e => e.LocationId).HasColumnName("LocationID");
            entity.Property(e => e.SalaryRange).HasMaxLength(100);
            entity.Property(e => e.SourceUrl).HasColumnName("SourceURL");

            entity.HasOne(d => d.Category).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Job_Cat");

            entity.HasOne(d => d.Employer).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.EmployerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Job_Emp");

            entity.HasOne(d => d.Location).WithMany(p => p.Jobs)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Job_Loc");
        });

        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.HasKey(e => e.ApplicationId).HasName("PK__JobAppli__C93A4F791661E97C");
            entity.ToTable("HoSoUngTuyen", tb => tb.HasTrigger("tr_HoSoUngTuyen_LogTrangThai"));

            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.ContactPhone).HasMaxLength(20);
            entity.Property(e => e.CvPublicId)
                .HasMaxLength(300)
                .HasColumnName("CvPublicID");
            entity.Property(e => e.CvUrl)
                .HasMaxLength(1000)
                .HasColumnName("CvURL");
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.StudentId).HasColumnName("StudentID");

            entity.HasOne(d => d.Job).WithMany(p => p.JobApplications)
                .HasForeignKey(d => d.JobId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_App_Job");

            entity.HasOne(d => d.Student).WithMany(p => p.JobApplications)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_App_Student");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("DiaDiem");
            entity.HasKey(e => e.LocationId).HasName("PK__Location__E7FEA477A66ECEEE");

            entity.HasIndex(e => e.Coordinates, "IX_Location_Coordinates").HasMethod("gist");
            entity.HasIndex(e => e.ProvinceName, "IX_Location_ProvinceName");
            entity.HasIndex(e => new { e.ProvinceName, e.DistrictName }, "IX_Location_Province_District");
            entity.HasIndex(e => new { e.ProvinceName, e.DistrictName, e.WardName }, "IX_Location_Province_District_Ward");

            entity.HasIndex(e => e.FullAddressNormalized, "IX_Location_FullAddressNormalized");

            entity.HasIndex(e => e.AddressText, "IX_Location_AddressText_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.Property(e => e.LocationId).HasColumnName("LocationID");
            entity.Property(e => e.District).HasMaxLength(100);
            entity.Property(e => e.ProvinceCode).HasMaxLength(20);
            entity.Property(e => e.ProvinceName).HasMaxLength(150);
            entity.Property(e => e.DistrictCode).HasMaxLength(20);
            entity.Property(e => e.DistrictName).HasMaxLength(150);
            entity.Property(e => e.WardCode).HasMaxLength(20);
            entity.Property(e => e.WardName).HasMaxLength(150);
            entity.Property(e => e.HouseNumber).HasMaxLength(50);
            entity.Property(e => e.Street).HasMaxLength(255);
            entity.Property(e => e.FullAddressNormalized).HasMaxLength(500);
            entity.Property(e => e.GeocodedLatitude);
            entity.Property(e => e.GeocodedLongitude);
            entity.Property(e => e.LocationDistanceMeters);
            entity.Property(e => e.LocationConfidence).HasMaxLength(20);
            entity.Property(e => e.LocationSuspicious).HasDefaultValue(false);
            entity.Property(e => e.GeocodeSource).HasMaxLength(50);
        });

        modelBuilder.Entity<Medium>(entity =>
        {
            entity.ToTable("TepDaPhuongTien");
            entity.HasKey(e => e.MediaId).HasName("PK__Media__B2C2B5AF03484D12");

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_TepDaPhuongTien_Target");

            entity.Property(e => e.MediaId).HasColumnName("MediaID");
            entity.Property(e => e.IsThumbnail).HasDefaultValue(false);
            entity.Property(e => e.MediaUrl).HasColumnName("MediaURL");
            entity.Property(e => e.TargetId).HasColumnName("TargetID");
            entity.Property(e => e.TargetType).HasMaxLength(10);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("PK_Messages");

            entity.ToTable("TinNhan");

            entity.HasIndex(e => new { e.ConversationId, e.MessageId }, "IX_Messages_Conversation_Message");
            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt }, "IX_Messages_Conversation_Created");
            entity.HasIndex(e => new { e.SenderAccountId, e.CreatedAt }, "IX_Messages_Sender_Created");

            entity.Property(e => e.MessageId).HasColumnName("MessageID");
            entity.Property(e => e.ConversationId).HasColumnName("ConversationID");
            entity.Property(e => e.SenderAccountId).HasColumnName("SenderAccountID");
            entity.Property(e => e.Content).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Conversation).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Messages_Conversations");

            entity.HasOne(d => d.SenderAccount).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Messages_Accounts");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK_Notifications");

            entity.ToTable("ThongBao");

            entity.HasIndex(e => new { e.RecipientAccountId, e.IsRead, e.CreatedAt }, "IX_Notifications_Recipient_Read_Time");
            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_Notifications_Target");

            entity.Property(e => e.NotificationId).HasColumnName("NotificationID");
            entity.Property(e => e.RecipientAccountId).HasColumnName("RecipientAccountID");
            entity.Property(e => e.Type).HasMaxLength(40);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Message).HasMaxLength(1000);
            entity.Property(e => e.TargetType).HasMaxLength(20);
            entity.Property(e => e.TargetId).HasColumnName("TargetID");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.MetaJson).HasColumnName("MetaJSON");

            entity.HasOne(d => d.RecipientAccount).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.RecipientAccountId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Notifications_Accounts");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("DanhGia");
            entity.HasKey(e => e.ReviewId).HasName("PK__Reviews__74BC79AEF54E702E");

            entity.HasIndex(e => new { e.StudentId, e.TargetType, e.TargetId }, "UX_Review_Student_Target")
                .IsUnique();

            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_Review_Target");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.ReviewId).HasColumnName("ReviewID");
            entity.Property(e => e.StudentId).HasColumnName("StudentID");
            entity.Property(e => e.TargetId).HasColumnName("TargetID");
            entity.Property(e => e.TargetType).HasMaxLength(10);

            entity.HasOne(d => d.Student).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Review_Student");
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.RoomId).HasName("PK__Rooms__32863919894EE30F");

            entity.HasIndex(e => e.SourceUrl, "UX_PhongTro_SourceURL").IsUnique();

            entity.HasIndex(e => e.Title, "IX_PhongTro_Title_Trgm")
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.HasIndex(e => new { e.RoomStatus, e.CreatedAt }, "IX_PhongTro_RoomStatus_CreatedAt");

            entity.ToTable("PhongTro", tb => tb.HasTrigger("trg_LogPriceChange"));

            entity.Property(e => e.RoomId).HasColumnName("RoomID");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.HostId).HasColumnName("HostID");
            entity.Property(e => e.ContactPhone)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.IsExternal).HasDefaultValue(false);
            entity.Property(e => e.LocationId).HasColumnName("LocationID");
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.RoomStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Available");
            entity.Property(e => e.SourceUrl).HasColumnName("SourceURL");
            entity.Property(e => e.Title).HasMaxLength(255);

            entity.HasOne(d => d.Category).WithMany(p => p.Rooms)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Room_Cat");

            entity.HasOne(d => d.Host).WithMany(p => p.Rooms)
                .HasForeignKey(d => d.HostId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Room_Host");

            entity.HasOne(d => d.Location).WithMany(p => p.Rooms)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Room_Loc");
        });

        modelBuilder.Entity<RoomViewingAppointment>(entity =>
        {
            entity.HasKey(e => e.AppointmentId).HasName("PK_RoomViewingAppointments");

            entity.ToTable("LichXemPhong", tb => tb.HasTrigger("tr_LichXemPhong_LogTrangThai"));

            entity.HasIndex(e => new { e.HostId, e.Status, e.ScheduledAt }, "IX_RoomViewingAppointments_Host_Status_Scheduled");
            entity.HasIndex(e => new { e.StudentId, e.Status, e.CreatedAt }, "IX_RoomViewingAppointments_Student_Status_Created");
            entity.HasIndex(e => new { e.RoomId, e.ScheduledAt }, "IX_RoomViewingAppointments_Room_Scheduled");

            entity.Property(e => e.AppointmentId).HasColumnName("AppointmentID");
            entity.Property(e => e.RoomId).HasColumnName("RoomID");
            entity.Property(e => e.StudentId).HasColumnName("StudentID");
            entity.Property(e => e.HostId).HasColumnName("HostID");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.ContactPhone)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.StudentNote).HasMaxLength(1000);
            entity.Property(e => e.HostResponse).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);

            entity.HasOne(d => d.Room).WithMany()
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_RoomViewingAppointments_Room");

            entity.HasOne(d => d.Student).WithMany()
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_RoomViewingAppointments_Student");

            entity.HasOne(d => d.Host).WithMany()
                .HasForeignKey(d => d.HostId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_RoomViewingAppointments_Host");
        });

        modelBuilder.Entity<StudentProfile>(entity =>
        {
            entity.ToTable("HoSoSinhVien");
            entity.HasKey(e => e.StudentId).HasName("PK__StudentP__32C52A79F5EEC15A");

            entity.HasIndex(e => e.AccountId, "UQ__StudentP__349DA5873B5BC4FE").IsUnique();

            entity.Property(e => e.StudentId).HasColumnName("StudentID");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.Cvlink)
                .HasMaxLength(255)
                .HasColumnName("CVLink");
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Major).HasMaxLength(100);
            entity.Property(e => e.University).HasMaxLength(150);

            entity.HasOne(d => d.Account).WithOne(p => p.StudentProfile)
                .HasForeignKey<StudentProfile>(d => d.AccountId)
                .HasConstraintName("FK_Student_Acc");
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.ToTable("NhatKyHeThong");
            entity.HasKey(e => e.LogId).HasName("PK__SystemLo__5E5499A8D6B10865");

            entity.Property(e => e.LogId).HasColumnName("LogID");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.ActionTime).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.LogAction).HasMaxLength(255);

            entity.HasOne(d => d.Account).WithMany(p => p.SystemLogs)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Log_Acc");
        });

        modelBuilder.Entity<RoommatePost>(entity =>
        {
            entity.ToTable("RoommatePost");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.AreaPreference).HasMaxLength(255);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_RoommatePost_Status_CreatedAt");

            entity.HasOne(d => d.Student).WithMany(p => p.RoommatePosts)
                .HasForeignKey(d => d.StudentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RoommatePosts_StudentProfile");
        });

        modelBuilder.Entity<ContentReport>(entity =>
        {
            entity.ToTable("ContentReports");

            entity.HasKey(e => e.ReportId);

            entity.Property(e => e.ReportId).HasColumnName("ReportID");
            entity.Property(e => e.TargetType).HasMaxLength(20);
            entity.Property(e => e.TargetId).HasColumnName("TargetID");
            entity.Property(e => e.ReporterAccountId).HasColumnName("ReporterAccountID");
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(e => e.ResolutionAction).HasMaxLength(20);
            entity.Property(e => e.ResolutionNote).HasMaxLength(1000);
            entity.Property(e => e.ReviewedByAdminAccountId).HasColumnName("ReviewedByAdminAccountID");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(currentTimestampSql);
            entity.Property(e => e.ReviewedAt);
            entity.Property(e => e.TargetTitleSnapshot).HasMaxLength(255);
            entity.Property(e => e.OwnerAccountIdSnapshot);

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_ContentReports_Status_CreatedAt");
            entity.HasIndex(e => new { e.TargetType, e.TargetId }, "IX_ContentReports_TargetType_TargetId");
            entity.HasIndex(e => new { e.ReporterAccountId, e.CreatedAt }, "IX_ContentReports_ReporterAccountId_CreatedAt");
            entity.HasIndex(e => new { e.TargetType, e.TargetId, e.Status }, "IX_ContentReports_TargetType_TargetId_Status");
            entity.HasIndex(e => new { e.ReporterAccountId, e.TargetType, e.TargetId }, "UQ_ContentReports_Reporter_Target_Active")
                .IsUnique()
                .HasFilter(isPostgres ? "\"Status\" IN ('Pending', 'Reviewing')" : "[Status] IN ('Pending', 'Reviewing')");

            entity.HasOne(d => d.ReporterAccount)
                .WithMany()
                .HasForeignKey(d => d.ReporterAccountId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ContentReports_ReporterAccount");

            entity.HasOne(d => d.ReviewedByAdminAccount)
                .WithMany()
                .HasForeignKey(d => d.ReviewedByAdminAccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ContentReports_ReviewedByAdminAccount");
        });

        modelBuilder.Entity<VGlobalMapFeed>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_GlobalMapFeed");

            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ItemType)
                .HasMaxLength(4)
                .IsUnicode(false);
            entity.Property(e => e.SourceUrl).HasColumnName("SourceUrl");
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.Value).HasColumnType("decimal(18, 2)");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
