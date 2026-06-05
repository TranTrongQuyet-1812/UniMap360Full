using Microsoft.EntityFrameworkCore;

namespace UniMap360.Models;

public partial class UniMap360ProContext
{
    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; } = null!;
    public virtual DbSet<AccountSubscription> AccountSubscriptions { get; set; } = null!;
    public virtual DbSet<FeaturedListing> FeaturedListings { get; set; } = null!;
    public virtual DbSet<PostAnalyticsEvent> PostAnalyticsEvents { get; set; } = null!;
    public virtual DbSet<PostAnalyticsDaily> PostAnalyticsDailies { get; set; } = null!;
    public virtual DbSet<PaymentOrder> PaymentOrders { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // 1. SubscriptionPlan Configuration
        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId);
            entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.RoleScope).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PriceVnd).HasPrecision(18, 2);
            entity.Property(e => e.BillingCycle).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.Code)
                .IsUnique()
                .HasDatabaseName("UQ_SubscriptionPlans_Code");
        });

        // 2. AccountSubscription Configuration
        modelBuilder.Entity<AccountSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId);
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(d => d.Account)
                .WithMany()
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Plan)
                .WithMany(p => p.AccountSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. FeaturedListing Configuration
        modelBuilder.Entity<FeaturedListing>(entity =>
        {
            entity.HasKey(e => e.FeaturedListingId);
            entity.Property(e => e.TargetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.FeatureType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(d => d.OwnerAccount)
                .WithMany()
                .HasForeignKey(d => d.OwnerAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for pin limit, lookup featured, and expired pins cleanup
            entity.HasIndex(e => new { e.OwnerAccountId, e.FeatureType, e.Status })
                .HasDatabaseName("IX_FeaturedListings_Owner_Type_Status");

            entity.HasIndex(e => new { e.TargetType, e.TargetId, e.FeatureType, e.Status })
                .HasDatabaseName("IX_FeaturedListings_Target_Type_Status");

            entity.HasIndex(e => new { e.Status, e.EndsAt })
                .HasDatabaseName("IX_FeaturedListings_Status_EndsAt");
        });

        // 4. PostAnalyticsEvent Configuration
        modelBuilder.Entity<PostAnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.TargetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SourcePage).HasMaxLength(255);

            entity.HasOne(d => d.OwnerAccount)
                .WithMany()
                .HasForeignKey(d => d.OwnerAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.ActorAccount)
                .WithMany()
                .HasForeignKey(d => d.ActorAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes for details view / reports
            entity.HasIndex(e => new { e.TargetType, e.TargetId, e.OccurredAt })
                .HasDatabaseName("IX_PostAnalyticsEvents_Target_OccurredAt");

            entity.HasIndex(e => new { e.OwnerAccountId, e.OccurredAt })
                .HasDatabaseName("IX_PostAnalyticsEvents_Owner_OccurredAt");

            entity.HasIndex(e => new { e.EventType, e.OccurredAt })
                .HasDatabaseName("IX_PostAnalyticsEvents_Type_OccurredAt");
        });

        // 5. PostAnalyticsDaily Configuration
        modelBuilder.Entity<PostAnalyticsDaily>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TargetType).HasMaxLength(50).IsRequired();

            // Unique Index for Upsert operations
            entity.HasIndex(e => new { e.TargetType, e.TargetId, e.Date })
                .IsUnique()
                .HasDatabaseName("UQ_PostAnalyticsDaily_Target_Date");
        });

        // 6. PaymentOrder Configuration
        modelBuilder.Entity<PaymentOrder>(entity =>
        {
            entity.ToTable("PaymentOrders");
            entity.HasKey(e => e.PaymentOrderId);
            entity.Property(e => e.PlanCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("VND").IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Pending").IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(50).HasDefaultValue("PayOS").IsRequired();
            entity.Property(e => e.ProviderPaymentLinkId).HasMaxLength(255);
            entity.Property(e => e.CheckoutUrl).HasMaxLength(1000);
            entity.Property(e => e.QrCode).HasMaxLength(2000);
            entity.Property(e => e.FailureReason).HasMaxLength(1000);

            entity.HasIndex(e => e.OrderCode)
                .IsUnique()
                .HasDatabaseName("UQ_PaymentOrders_OrderCode");

            entity.HasOne(d => d.Account)
                .WithMany()
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
