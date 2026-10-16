using Microsoft.EntityFrameworkCore;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<PropertyCategory> PropertyCategories { get; set; }
        public DbSet<Property> Properties { get; set; }
        public DbSet<PropertyPhoto> PropertyPhotos { get; set; }
        public DbSet<Amenity> Amenities { get; set; }
        public DbSet<PropertyAmenity> PropertyAmenities { get; set; }
        public DbSet<RentalApplication> RentalApplications { get; set; }
        public DbSet<RentalAgreement> RentalAgreements { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<ArrearsNotice> ArrearsNotices { get; set; }
        public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<AgreementRequest> AgreementRequests { get; set; }
        public DbSet<SupportTicket> SupportTickets { get; set; }
        public DbSet<SupportMessage> SupportMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Composite primary key for the many-to-many join entity.
            // (This is the one relationship data annotations cannot express,
            //  so a small amount of Fluent API is used here as allowed by the brief.)
            modelBuilder.Entity<PropertyAmenity>()
                .HasKey(pa => new { pa.PropertyID, pa.AmenityID });

            // Prevent multiple cascade-delete paths (SQL Server restriction) by
            // switching some FK relationships to Restrict instead of Cascade.
            modelBuilder.Entity<RentalApplication>()
                .HasOne(a => a.Tenant)
                .WithMany(u => u.RentalApplications)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.Tenant)
                .WithMany(u => u.MaintenanceRequests)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MaintenanceRequest>()
                .HasOne(m => m.RepliedByAdmin)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Review>()
                .HasOne(r => r.Reviewer)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Review>()
                .HasOne(r => r.Reviewee)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            // An arrears notice points at both the agreement and the overdue payment that
            // triggered it. Cascading down both paths would give SQL Server two delete paths
            // to the same row, so the payment side is left as a plain (non-cascading) link.
            modelBuilder.Entity<ArrearsNotice>()
                .HasOne(n => n.Payment)
                .WithMany()
                .HasForeignKey(n => n.PaymentID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ArrearsNotice>()
                .HasOne(n => n.Agreement)
                .WithMany(a => a.ArrearsNotices)
                .HasForeignKey(n => n.AgreementID)
                .OnDelete(DeleteBehavior.Cascade);

            // Same multiple-cascade-path issue as above: AgreementRequest points at both the
            // agreement and the users who raised/decided it, so the User side is left non-cascading.
            modelBuilder.Entity<AgreementRequest>()
                .HasOne(r => r.RequestedBy)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AgreementRequest>()
                .HasOne(r => r.DecidedByAdmin)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            // Same again: a support message points at both its ticket and the user who
            // sent it, so the User side is left non-cascading.
            modelBuilder.Entity<SupportTicket>()
                .HasOne(t => t.User)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SupportMessage>()
                .HasOne(m => m.SentBy)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Sample seed data so the system has data to demo with ----
            modelBuilder.Entity<PropertyCategory>().HasData(
                new PropertyCategory { CategoryID = 1, CategoryName = "Whole House" },
                new PropertyCategory { CategoryID = 2, CategoryName = "Single Room" },
                new PropertyCategory { CategoryID = 3, CategoryName = "Apartment/Condo" },
                new PropertyCategory { CategoryID = 4, CategoryName = "Studio" }
            );

            modelBuilder.Entity<Amenity>().HasData(
                new Amenity { AmenityID = 1, AmenityName = "Wi-Fi" },
                new Amenity { AmenityID = 2, AmenityName = "Air Conditioning" },
                new Amenity { AmenityID = 3, AmenityName = "Parking" },
                new Amenity { AmenityID = 4, AmenityName = "Furnished" },
                new Amenity { AmenityID = 5, AmenityName = "Washing Machine" },
                new Amenity { AmenityID = 6, AmenityName = "Near Public Transport" }
            );

            // Seed passwords are pre-hashed with PasswordHasher<User> (login verifies with the same
            // hasher, which throws on a plain-text value). The hash must be a fixed literal here -
            // HashPassword salts randomly, and a call in OnModelCreating would make the EF model
            // non-deterministic between builds (PendingModelChangesWarning on every migration).
            // Admin@123 / Owner@123 / Tenant@123 respectively.
            // EmailVerified = true on all seeded accounts - these are pre-provisioned demo
            // logins, not self-registrations, so there is no signup email to confirm.
            modelBuilder.Entity<User>().HasData(
     new User { UserID = 1, FullName = "System Admin", Email = "admin@rental.com", PasswordHash = "AQAAAAIAAYagAAAAEI2ogL9QVWL/6ucAIHWusAsHKfll67FP7YNrvV2ykk7YhGwK3Q+V7oAeSUI024X42Q==", Phone = "0100000001", DateOfBirth = new DateTime(1991, 1, 1), ProfilePhoto = "", Role = UserRole.Admin, CreatedDate = new DateTime(2026, 1, 1), EmailVerified = true },
     new User { UserID = 2, FullName = "Ahmad Owner", Email = "owner1@rental.com", PasswordHash = "AQAAAAIAAYagAAAAEJIcDIzn3Cb+lfM4/e186DHUMpPtojs/oUIRYefsTdUEzD9DAyQIXy/rhireKIAY/Q==", Phone = "0123456781", DateOfBirth = new DateTime(1986, 1, 1), ProfilePhoto = "", Role = UserRole.Owner, CreatedDate = new DateTime(2026, 1, 1), EmailVerified = true },
     new User { UserID = 3, FullName = "Siti Tenant", Email = "tenant1@rental.com", PasswordHash = "AQAAAAIAAYagAAAAEAEWoGxS8Hjb5RRTqPVc9h+euQabSE9xeNyfgbsqZHESACEhy2WnhGrlsWEXOgVhzw==", Phone = "0123456782", DateOfBirth = new DateTime(2001, 1, 1), ProfilePhoto = "", Role = UserRole.Tenant, CreatedDate = new DateTime(2026, 1, 1), EmailVerified = true }
 );
        }
    }
}
