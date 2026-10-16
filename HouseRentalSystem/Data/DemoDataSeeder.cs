using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Data
{
    // Seeds realistic transactional data (listings, bookings, agreements, payments, a
    // review) on top of the three bare accounts + lookup data that come from
    // ApplicationDbContext's migration-based HasData seed. Deliberately NOT done via
    // HasData: this data has many interdependent foreign keys (an agreement needs an
    // application, which needs a property, etc.) and this project's database already
    // accumulates manually-created test records with their own identity values, so
    // pinning explicit primary keys here would risk colliding with them. Instead this
    // runs once at startup, lets the database assign identities as normal, and is
    // guarded to be a no-op once the demo data already exists.
    public static class DemoDataSeeder
    {
        // Just the literal title text for the first seeded property - no longer used as an
        // idempotency check (see the comment on SeedCoreDemoDataAsync for why).
        private const string MarkerTitle = "Cozy Studio near LRT Kelana Jaya";
        private const string MaintenanceMarkerTitle = "Kitchen tap leaking";

        // DEMO ONLY: the late-rent walkthrough (reminder -> deposit deduction -> compound)
        // emails the tenant on the "troubled" tenancy. The migration seed gives that tenant
        // the dummy address tenant1@rental.com, which no real inbox receives. Point it at a
        // real address here so the emails can actually be opened during a demo. Set this back
        // to "tenant1@rental.com" (or blank) to disable the swap. Login for that tenant then
        // uses this address; the password is unchanged (Tenant@123).
        private const string DemoArrearsTenantEmail = "tenant1@rental.com"; // set to a real inbox to enable the one-time swap
        private const string DemoArrearsTenantSeedEmail = "tenant1@rental.com";

        // Two independently-guarded stages, so adding the maintenance-request stage after
        // the core stage had already run once (e.g. on a developer's existing database)
        // still seeds it on the next startup, without re-inserting everything else.
        public static async Task SeedAsync(ApplicationDbContext context)
        {
            await SeedCoreDemoDataAsync(context);
            await SeedMaintenanceRequestsAsync(context);
            await SeedBrowseListingsAsync(context);
            await BackfillMissingAgreementsAsync(context);
            await PointArrearsDemoTenantAtRealInboxAsync(context);
        }

        // Repairs any owner-visible booking request (Pending / awaiting-admin / Approved) that
        // has no RentalAgreement attached. The deposit-first flow always creates the agreement
        // up front, so this only ever finds rows left behind by an earlier version of the flow
        // or of this seeder - without it, the owner's "View rental agreement" link on such a
        // request 404s. Idempotent: a no-op once every such application has an agreement.
        private static async Task BackfillMissingAgreementsAsync(ApplicationDbContext context)
        {
            var ownerVisible = new[]
            {
                ApplicationStatus.Pending,
                ApplicationStatus.PendingAdminApproval,
                ApplicationStatus.Approved
            };

            var orphans = await context.RentalApplications
                .Include(a => a.Property)
                .Where(a => ownerVisible.Contains(a.Status) && a.Agreement == null)
                .ToListAsync();

            if (orphans.Count == 0)
            {
                return;
            }

            var now = DateTime.Now;

            foreach (var application in orphans)
            {
                var acceptedOn = application.TermsAcceptedDate ?? application.AppliedDate;
                application.TermsAcceptedDate ??= acceptedOn;

                var agreement = new RentalAgreement
                {
                    ApplicationID = application.ApplicationID,
                    StartDate = application.MoveInDate,
                    EndDate = application.MoveInDate.AddMonths(application.RentalDurationMonths),
                    MonthlyRent = application.Property.RentPrice,
                    DepositAmount = application.Property.Deposit,
                    DepositBalance = application.Property.Deposit,
                    Status = AgreementStatus.PendingApproval
                };
                context.RentalAgreements.Add(agreement);
                await context.SaveChangesAsync();

                var deposit = RentSchedule.BuildDeposit(agreement);
                deposit.PaymentStatus = PaymentStatus.Paid;
                deposit.PaymentDate = acceptedOn;
                deposit.ReceiptPath = "Paid via RentEasy (demo data)";
                context.Payments.Add(deposit);
                await context.SaveChangesAsync();
            }
        }

        // One-time: move the troubled tenant onto a real email address and rewind that
        // tenancy's late-rent ladder (clear the notices, drop any compound charge, restore
        // the deposit, set the unpaid months back to Pending). The next time the agreement
        // or rent-collection page is opened, ArrearsService works through the whole ladder
        // again and its reminder / deposit-deduction / compound emails go to the real inbox.
        // Guarded on the seed address, so it runs once and then never touches the data again.
        private static async Task PointArrearsDemoTenantAtRealInboxAsync(ApplicationDbContext context)
        {
            if (string.IsNullOrWhiteSpace(DemoArrearsTenantEmail)
                || DemoArrearsTenantEmail == DemoArrearsTenantSeedEmail)
            {
                return;
            }

            var tenant = await context.Users
                .FirstOrDefaultAsync(u => u.Email == DemoArrearsTenantSeedEmail);
            if (tenant == null)
            {
                return; // already swapped on a previous startup, or seed account removed.
            }

            // Don't collide with a real account that already uses the target address.
            if (await context.Users.AnyAsync(u => u.Email == DemoArrearsTenantEmail))
            {
                return;
            }

            tenant.Email = DemoArrearsTenantEmail;

            var agreements = await context.RentalAgreements
                .Include(a => a.Application)
                .Include(a => a.Payments)
                .Include(a => a.ArrearsNotices)
                .Where(a => a.Application.TenantID == tenant.UserID
                            && a.Status == AgreementStatus.Active)
                .ToListAsync();

            foreach (var agreement in agreements)
            {
                context.ArrearsNotices.RemoveRange(agreement.ArrearsNotices);
                context.Payments.RemoveRange(
                    agreement.Payments.Where(p => p.PaymentType == PaymentType.Compound));
                agreement.DepositBalance = agreement.DepositAmount;

                foreach (var rent in agreement.Payments.Where(p => p.PaymentType == PaymentType.Rent))
                {
                    // Leave months the tenant genuinely paid; rewind everything else.
                    if (rent.PaymentStatus == PaymentStatus.Paid && !rent.SettledFromDeposit)
                    {
                        continue;
                    }

                    rent.PaymentStatus = PaymentStatus.Pending;
                    rent.SettledFromDeposit = false;
                    rent.PaymentDate = null;
                    rent.ReceiptPath = string.Empty;
                }
            }

            await context.SaveChangesAsync();
        }

        // Title of the first listing in the browse-catalogue batch, used as this stage's
        // idempotency marker.
        private const string BrowseMarkerTitle = "KLCC View Serviced Suite";

        private static async Task SeedCoreDemoDataAsync(ApplicationDbContext context)
        {
            // Guard on the owner2 account rather than a property title: titles are editable
            // through the UI (Property/Edit), so using one as the "have I already seeded"
            // check breaks the moment someone renames it, causing this to reseed and
            // duplicate every property/application/agreement/payment on the next startup.
            if (await context.Users.AnyAsync(u => u.Email == "owner2@rental.com"))
            {
                return;
            }

            var owner1 = await context.Users.FirstOrDefaultAsync(u => u.Email == "owner1@rental.com");
            var tenant1 = await context.Users.FirstOrDefaultAsync(u => u.Email == "tenant1@rental.com");
            if (owner1 == null || tenant1 == null)
            {
                // Migration-seeded demo accounts aren't there (e.g. someone removed them) -
                // nothing safe to attach this data to.
                return;
            }

            var hasher = new PasswordHasher<User>();
            var today = DateTime.Today;

            var owner2 = await context.Users.FirstOrDefaultAsync(u => u.Email == "owner2@rental.com");
            if (owner2 == null)
            {
                owner2 = new User
                {
                    FullName = "Mei Ling Owner",
                    Email = "owner2@rental.com",
                    Phone = "0123456783",
                    DateOfBirth = new DateTime(1988, 3, 15),
                    Role = UserRole.Owner,
                    ProfilePhoto = "",
                    IsActive = true,
                    EmailVerified = true,
                    CreatedDate = today.AddMonths(-9)
                };
                owner2.PasswordHash = hasher.HashPassword(owner2, "Owner@123");
                context.Users.Add(owner2);
            }

            var tenant2 = await context.Users.FirstOrDefaultAsync(u => u.Email == "tenant2@rental.com");
            if (tenant2 == null)
            {
                tenant2 = new User
                {
                    FullName = "Ravi Tenant",
                    Email = "tenant2@rental.com",
                    Phone = "0123456784",
                    DateOfBirth = new DateTime(1997, 9, 2),
                    Role = UserRole.Tenant,
                    ProfilePhoto = "",
                    IsActive = true,
                    EmailVerified = true,
                    CreatedDate = today.AddMonths(-9)
                };
                tenant2.PasswordHash = hasher.HashPassword(tenant2, "Tenant@123");
                context.Users.Add(tenant2);
            }

            await context.SaveChangesAsync();

            // ---- Properties -------------------------------------------------------
            var property1 = new Property
            {
                OwnerID = owner1.UserID,
                CategoryID = 4, // Studio
                Title = MarkerTitle,
                Description = "A bright, compact studio a 5-minute walk from Kelana Jaya LRT station. " +
                               "Ideal for a single working professional or student. Recently repainted with new flooring.",
                Address = "12 Jalan SS6/12",
                City = "Petaling Jaya",
                RentPrice = 900,
                Deposit = 1800,
                NumBedrooms = 1,
                NumBathrooms = 1,
                IsAvailable = true,
                CreatedDate = today.AddMonths(-6)
            };

            var property2 = new Property
            {
                OwnerID = owner1.UserID,
                CategoryID = 3, // Apartment/Condo
                Title = "Spacious 3-Room Apartment in Cheras",
                Description = "Family-sized 3-bedroom apartment with a renovated kitchen and covered parking. " +
                               "Close to schools, a wet market and the Cheras-Kajang expressway.",
                Address = "45 Jalan Cheras Baru",
                City = "Kuala Lumpur",
                RentPrice = 1800,
                Deposit = 3600,
                NumBedrooms = 3,
                NumBathrooms = 2,
                IsAvailable = false,
                CreatedDate = today.AddMonths(-7)
            };

            var property3 = new Property
            {
                OwnerID = owner2.UserID,
                CategoryID = 2, // Single Room
                Title = "Fully Furnished Single Room in PJ",
                Description = "Furnished single room in a shared house, utilities included in rent. " +
                               "Shared kitchen and washing machine. Walking distance to eateries.",
                Address = "8 Jalan Utama",
                City = "Petaling Jaya",
                RentPrice = 650,
                Deposit = 1300,
                NumBedrooms = 1,
                NumBathrooms = 1,
                IsAvailable = false,
                CreatedDate = today.AddMonths(-8)
            };

            var property4 = new Property
            {
                OwnerID = owner2.UserID,
                CategoryID = 1, // Whole House
                Title = "Whole House for Rent in Subang Jaya",
                Description = "Double-storey terrace house with 4 bedrooms and a small garden, " +
                               "in a quiet, gated-and-guarded neighbourhood close to public transport.",
                Address = "21 Jalan SS15/4",
                City = "Subang Jaya",
                RentPrice = 2500,
                Deposit = 5000,
                NumBedrooms = 4,
                NumBathrooms = 3,
                IsAvailable = true,
                CreatedDate = today.AddMonths(-5)
            };

            context.Properties.AddRange(property1, property2, property3, property4);
            await context.SaveChangesAsync();

            context.PropertyAmenities.AddRange(
                new PropertyAmenity { PropertyID = property1.PropertyID, AmenityID = 1 }, // Wi-Fi
                new PropertyAmenity { PropertyID = property1.PropertyID, AmenityID = 2 }, // Air Conditioning

                new PropertyAmenity { PropertyID = property2.PropertyID, AmenityID = 1 },
                new PropertyAmenity { PropertyID = property2.PropertyID, AmenityID = 3 }, // Parking
                new PropertyAmenity { PropertyID = property2.PropertyID, AmenityID = 4 }, // Furnished

                new PropertyAmenity { PropertyID = property3.PropertyID, AmenityID = 1 },
                new PropertyAmenity { PropertyID = property3.PropertyID, AmenityID = 4 },
                new PropertyAmenity { PropertyID = property3.PropertyID, AmenityID = 5 }, // Washing Machine

                new PropertyAmenity { PropertyID = property4.PropertyID, AmenityID = 1 },
                new PropertyAmenity { PropertyID = property4.PropertyID, AmenityID = 2 },
                new PropertyAmenity { PropertyID = property4.PropertyID, AmenityID = 3 },
                new PropertyAmenity { PropertyID = property4.PropertyID, AmenityID = 6 }  // Near Public Transport
            );

            // ---- Rental applications ------------------------------------------------
            // #1: approved, well behind on rent (property3) - shows the full arrears ladder.
            var troubledStart = today.AddMonths(-5);
            var troubledApplication = new RentalApplication
            {
                PropertyID = property3.PropertyID,
                TenantID = tenant1.UserID,
                MoveInDate = troubledStart,
                RentalDurationMonths = 12,
                Status = ApplicationStatus.Approved,
                Message = "Looking for a quiet place close to work, happy to sign a full-year lease.",
                AppliedDate = troubledStart.AddDays(-14)
            };

            // #2: approved, up to date on rent (property2) - a healthy tenancy for contrast.
            var healthyStart = today.AddMonths(-2);
            var healthyApplication = new RentalApplication
            {
                PropertyID = property2.PropertyID,
                TenantID = tenant2.UserID,
                MoveInDate = healthyStart,
                RentalDurationMonths = 12,
                Status = ApplicationStatus.Approved,
                Message = "Relocating with my family, need 3 bedrooms and covered parking.",
                AppliedDate = healthyStart.AddDays(-10)
            };

            // #3: still pending, awaiting the owner's decision (property1). In the deposit-first
            // flow the tenant has already accepted the agreement and paid the deposit to reach
            // this point, so it needs a matching PendingApproval agreement (built below) - without
            // one the owner's "View rental agreement" link on this request 404s.
            var pendingStart = today.AddDays(21);
            var pendingApplication = new RentalApplication
            {
                PropertyID = property1.PropertyID,
                TenantID = tenant2.UserID,
                MoveInDate = pendingStart,
                RentalDurationMonths = 9,
                Status = ApplicationStatus.Pending,
                Message = "Hi, I'm relocating for work and would love to move in early next month. " +
                           "I have stable income and can provide references.",
                AppliedDate = today.AddDays(-3),
                TermsAcceptedDate = today.AddDays(-3)
            };

            // #4: rejected, with a reason (property4).
            var rejectedApplication = new RentalApplication
            {
                PropertyID = property4.PropertyID,
                TenantID = tenant1.UserID,
                MoveInDate = today.AddDays(-30),
                RentalDurationMonths = 12,
                Status = ApplicationStatus.Rejected,
                Message = "Looking for a spacious place for my family.",
                RejectionReason = "Property already reserved for another applicant who applied earlier.",
                AppliedDate = today.AddDays(-40)
            };

            context.RentalApplications.AddRange(
                troubledApplication, healthyApplication, pendingApplication, rejectedApplication);
            await context.SaveChangesAsync();

            // ---- Rental agreements + payment schedules ------------------------------
            var troubledAgreement = new RentalAgreement
            {
                ApplicationID = troubledApplication.ApplicationID,
                StartDate = troubledStart,
                EndDate = troubledStart.AddMonths(12),
                MonthlyRent = property3.RentPrice,
                DepositAmount = property3.Deposit,
                DepositBalance = property3.Deposit,
                Status = AgreementStatus.Active
            };

            var healthyAgreement = new RentalAgreement
            {
                ApplicationID = healthyApplication.ApplicationID,
                StartDate = healthyStart,
                EndDate = healthyStart.AddMonths(12),
                MonthlyRent = property2.RentPrice,
                DepositAmount = property2.Deposit,
                DepositBalance = property2.Deposit,
                Status = AgreementStatus.Active
            };

            // Pending booking: agreement drawn up and deposit paid, now waiting on the owner.
            var pendingAgreement = new RentalAgreement
            {
                ApplicationID = pendingApplication.ApplicationID,
                StartDate = pendingStart,
                EndDate = pendingStart.AddMonths(9),
                MonthlyRent = property1.RentPrice,
                DepositAmount = property1.Deposit,
                DepositBalance = property1.Deposit,
                Status = AgreementStatus.PendingApproval
            };

            context.RentalAgreements.AddRange(troubledAgreement, healthyAgreement, pendingAgreement);
            await context.SaveChangesAsync();

            // Troubled tenancy: deposit + first 2 months paid on time, months 3-5 unpaid.
            // ArrearsService picks this up the first time anyone opens the agreement page
            // and works through the reminder -> deposit deduction -> compound ladder live.
            var troubledPayments = RentSchedule.Build(troubledAgreement, 12).ToList();
            MarkPaid(troubledPayments, PaymentType.Deposit, 0, troubledStart);
            MarkPaid(troubledPayments, PaymentType.Rent, 1, troubledStart);
            MarkPaid(troubledPayments, PaymentType.Rent, 2, troubledStart.AddMonths(1));
            context.Payments.AddRange(troubledPayments);

            // Healthy tenancy: deposit + every month due so far paid on time.
            var healthyPayments = RentSchedule.Build(healthyAgreement, 12).ToList();
            MarkPaid(healthyPayments, PaymentType.Deposit, 0, healthyStart);
            MarkPaid(healthyPayments, PaymentType.Rent, 1, healthyStart);
            MarkPaid(healthyPayments, PaymentType.Rent, 2, healthyStart.AddMonths(1));
            context.Payments.AddRange(healthyPayments);

            // Pending booking: only the deposit exists yet (paid); rent instalments are added
            // by AdminController.ApproveBooking once the agreement goes live.
            var pendingDeposit = RentSchedule.BuildDeposit(pendingAgreement);
            pendingDeposit.PaymentStatus = PaymentStatus.Paid;
            pendingDeposit.PaymentDate = today.AddDays(-3);
            pendingDeposit.ReceiptPath = "Paid via RentEasy (demo data)";
            context.Payments.Add(pendingDeposit);

            await context.SaveChangesAsync();

            // ---- A review on the healthy tenancy ------------------------------------
            context.Reviews.Add(new Review
            {
                AgreementID = healthyAgreement.AgreementID,
                ReviewerID = tenant2.UserID,
                RevieweeID = owner1.UserID,
                Rating = 5,
                Comment = "Great place, very responsive owner and everything was exactly as described. Highly recommend!",
                ReviewDate = today.AddDays(-20)
            });

            await context.SaveChangesAsync();
        }

        // Requires the core stage to have already created property2/property3 and
        // tenant1/tenant2 - looked up fresh by their known titles/emails rather than
        // reusing in-memory objects, since this stage can run on a later app start.
        private static async Task SeedMaintenanceRequestsAsync(ApplicationDbContext context)
        {
            if (await context.MaintenanceRequests.AnyAsync(r => r.Title == MaintenanceMarkerTitle))
            {
                return;
            }

            var property2 = await context.Properties.FirstOrDefaultAsync(p => p.Title == "Spacious 3-Room Apartment in Cheras");
            var property3 = await context.Properties.FirstOrDefaultAsync(p => p.Title == "Fully Furnished Single Room in PJ");
            var tenant1 = await context.Users.FirstOrDefaultAsync(u => u.Email == "tenant1@rental.com");
            var tenant2 = await context.Users.FirstOrDefaultAsync(u => u.Email == "tenant2@rental.com");
            var admin = await context.Users.FirstOrDefaultAsync(u => u.Email == "admin@rental.com");

            if (property2 == null || property3 == null || tenant1 == null || tenant2 == null || admin == null)
            {
                // Core demo data isn't there yet - nothing to attach these requests to.
                return;
            }

            var today = DateTime.Today;

            context.MaintenanceRequests.AddRange(
                new MaintenanceRequest
                {
                    PropertyID = property2.PropertyID,
                    TenantID = tenant2.UserID,
                    Title = MaintenanceMarkerTitle,
                    Description = "The kitchen sink tap has been dripping constantly for about a week and is starting to stain the sink.",
                    PhotoPath = string.Empty,
                    Status = MaintenanceStatus.Open,
                    SubmittedDate = today.AddDays(-4)
                },
                new MaintenanceRequest
                {
                    PropertyID = property3.PropertyID,
                    TenantID = tenant1.UserID,
                    Title = "Aircon not cooling",
                    Description = "The air conditioning unit in the room turns on but no longer blows cold air.",
                    PhotoPath = string.Empty,
                    Status = MaintenanceStatus.Resolved,
                    SubmittedDate = today.AddMonths(-1),
                    ResolvedDate = today.AddDays(-25),
                    AdminReply = "Verified with the tenant and sent a technician out - the unit's refrigerant was low. Topped up and confirmed it's blowing cold again.",
                    RepliedDate = today.AddDays(-25),
                    RepliedByAdminID = admin.UserID
                });

            await context.SaveChangesAsync();
        }

        // A catalogue of available listings so the browse/search page has enough rows to
        // filter, sort and page through. Independently guarded like the maintenance stage,
        // so it also seeds on an existing database on the next startup. No bookings or
        // agreements hang off these - they stay permanently "available".
        private static async Task SeedBrowseListingsAsync(ApplicationDbContext context)
        {
            if (await context.Properties.AnyAsync(p => p.Title == BrowseMarkerTitle))
            {
                return;
            }

            var owner1 = await context.Users.FirstOrDefaultAsync(u => u.Email == "owner1@rental.com");
            var owner2 = await context.Users.FirstOrDefaultAsync(u => u.Email == "owner2@rental.com");
            if (owner1 == null || owner2 == null)
            {
                return;
            }

            var today = DateTime.Today;

            // CategoryID: 1 Whole House, 2 Single Room, 3 Apartment/Condo, 4 Studio.
            // AmenityID:  1 Wi-Fi, 2 Air Conditioning, 3 Parking, 4 Furnished,
            //             5 Washing Machine, 6 Near Public Transport.
            var specs = new (string Title, int Owner, int Category, string City, string Address,
                             decimal Rent, int Beds, int Baths, int AgeDays, int[] Amenities)[]
            {
                (BrowseMarkerTitle, 1, 3, "Kuala Lumpur", "8 Jalan Ampang", 2600, 2, 2, 3, new[] { 1, 2, 3, 4 }),
                ("Bangsar South Co-living Room", 2, 2, "Kuala Lumpur", "15 Jalan Kerinchi", 850, 1, 1, 6, new[] { 1, 2, 5, 6 }),
                ("Mont Kiara Family Condo", 1, 3, "Kuala Lumpur", "2 Jalan Kiara 3", 3200, 3, 2, 10, new[] { 1, 2, 3, 4, 5 }),
                ("Setapak Budget Studio", 2, 4, "Kuala Lumpur", "40 Jalan Genting Klang", 700, 1, 1, 20, new[] { 1, 6 }),
                ("SS2 Terrace House", 1, 1, "Petaling Jaya", "33 Jalan SS2/24", 2200, 4, 3, 25, new[] { 2, 3, 5 }),
                ("Damansara Perdana Suite", 2, 3, "Petaling Jaya", "5 Jalan PJU 8", 1600, 2, 2, 32, new[] { 1, 2, 3, 4 }),
                ("Kota Damansara Single Room", 1, 2, "Petaling Jaya", "9 Jalan Teknologi", 600, 1, 1, 40, new[] { 1, 5, 6 }),
                ("USJ Whole House", 2, 1, "Subang Jaya", "12 Jalan USJ 11/2", 2800, 5, 3, 12, new[] { 2, 3, 4, 5 }),
                ("Sunway Studio near University", 1, 4, "Subang Jaya", "7 Jalan Lagoon Selatan", 1100, 1, 1, 5, new[] { 1, 2, 6 }),
                ("Setia Alam Double Storey", 2, 1, "Shah Alam", "88 Jalan Setia Prima", 2400, 4, 3, 45, new[] { 3, 4, 5 }),
                ("Seksyen 7 Student Room", 1, 2, "Shah Alam", "3 Jalan Plumbum", 550, 1, 1, 50, new[] { 1, 6 }),
                ("Cyberjaya Tech Hub Condo", 2, 3, "Cyberjaya", "1 Persiaran APEC", 1500, 2, 2, 8, new[] { 1, 2, 3, 4, 6 }),
                ("Cyberjaya Furnished Studio", 1, 4, "Cyberjaya", "22 Jalan Impact", 950, 1, 1, 15, new[] { 1, 2, 4 }),
                ("Puchong Jaya Apartment", 2, 3, "Puchong", "6 Jalan Kenari", 1300, 3, 2, 60, new[] { 2, 3, 5 }),
            };

            var created = new List<(Property Property, int[] Amenities)>();
            foreach (var s in specs)
            {
                var property = new Property
                {
                    OwnerID = s.Owner == 1 ? owner1.UserID : owner2.UserID,
                    CategoryID = s.Category,
                    Title = s.Title,
                    Description = $"{s.Beds}-bed {s.City} listing, move-in ready. Demo data for the browse catalogue.",
                    Address = s.Address,
                    City = s.City,
                    RentPrice = s.Rent,
                    Deposit = s.Rent * 2,
                    NumBedrooms = s.Beds,
                    NumBathrooms = s.Baths,
                    IsAvailable = true,
                    CreatedDate = today.AddDays(-s.AgeDays)
                };
                created.Add((property, s.Amenities));
                context.Properties.Add(property);
            }

            await context.SaveChangesAsync();

            foreach (var (property, amenities) in created)
            {
                foreach (var amenityId in amenities)
                {
                    context.PropertyAmenities.Add(new PropertyAmenity
                    {
                        PropertyID = property.PropertyID,
                        AmenityID = amenityId
                    });
                }
            }

            await context.SaveChangesAsync();
        }

        private static void MarkPaid(List<Payment> payments, PaymentType type, int rentMonthNumber, DateTime paidOn)
        {
            var payment = payments.First(p => p.PaymentType == type && p.RentMonthNumber == rentMonthNumber);
            payment.PaymentStatus = PaymentStatus.Paid;
            payment.PaymentDate = paidOn;
            payment.ReceiptPath = "Paid via RentEasy (demo data)";
        }
    }
}
