using Microsoft.EntityFrameworkCore;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Services
{
    // What an agreement's rent collection looks like right now, for the owner's dashboard
    // and the banner on the agreement page.
    public class ArrearsSummary
    {
        public RentalAgreement Agreement { get; set; }
        public int MonthsMissed { get; set; }
        public decimal OutstandingRent { get; set; }
        public decimal OutstandingCompounds { get; set; }
        public decimal DepositDeducted { get; set; }
        public DateTime? OldestOverdueDate { get; set; }
        public ArrearsNotice LatestNotice { get; set; }

        public bool IsInArrears => OutstandingRent > 0;

        // Named after what was actually done, not the rung of the ladder reached - a second
        // missed month cannot be "deposit deducted" if there was no deposit to draw on.
        public string Stage => MonthsMissed == 0
            ? "Up to date"
            : LatestNotice?.NoticeType switch
            {
                ArrearsNoticeType.DepositDeduction => "Deposit deducted",
                ArrearsNoticeType.Compound => "Compound issued",
                _ => "Reminder issued"
            };
    }

    // Applies the late-rent ladder in ArrearsPolicy to an agreement.
    //
    // Evaluation runs from two places: on demand whenever someone with an interest in the
    // tenancy loads a page that shows it (the tenant's agreement, the owner's rent collection
    // list), and once a day for every active tenancy from ArrearsReminderHostedService - the
    // safety net for when neither party opens a page. Every action is written to the database
    // and guarded against duplicates, so running it repeatedly changes nothing.
    //
    // Each notice the ladder raises is recorded on the agreement (shown in-app) straight away,
    // then emailed to the tenant as a separate step. EmailedDate on the notice tracks whether
    // that send has succeeded; a notice still unsent is retried on the next evaluation, so a
    // temporary mail failure delays the email rather than losing it.
    public class ArrearsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ArrearsService> _logger;

        public ArrearsService(ApplicationDbContext context, IEmailSender emailSender, ILogger<ArrearsService> logger)
        {
            _context = context;
            _emailSender = emailSender;
            _logger = logger;
        }

        // Evaluates one agreement. The agreement must have Payments and ArrearsNotices loaded;
        // any notices raised are saved and show up on those navigation collections.
        public async Task<ArrearsSummary> EvaluateAsync(RentalAgreement agreement, DateTime? asOf = null)
        {
            var today = (asOf ?? DateTime.Today).Date;
            var notices = agreement.ArrearsNotices ?? new List<ArrearsNotice>();
            var payments = agreement.Payments ?? new List<Payment>();

            if (agreement.Status != AgreementStatus.Active)
            {
                // No new ladder steps once the tenancy is over, but a notice raised while
                // it was active may still have an unsent email - give it another try.
                await RetryUnsentNoticeEmailsAsync(agreement);
                return Summarise(agreement, payments, notices, today);
            }

            // Rent that has gone past its due date plus the grace period without being settled.
            var overdue = payments
                .Where(p => p.PaymentType == PaymentType.Rent
                            && p.PaymentStatus != PaymentStatus.Paid
                            && p.DueDate.AddDays(ArrearsPolicy.GraceDays) < today)
                .OrderBy(p => p.DueDate)
                .ToList();

            foreach (var payment in overdue)
            {
                payment.PaymentStatus = PaymentStatus.Overdue;
            }

            // Months already cleared out of the deposit still count as months the tenant
            // failed to pay - otherwise raiding the deposit would reset the ladder and the
            // third missed month would never reach the compound stage.
            var settledFromDeposit = payments.Count(p => p.PaymentType == PaymentType.Rent && p.SettledFromDeposit);
            var monthsMissed = overdue.Count + settledFromDeposit;

            // Each stage below calls AddNotice, which records the notice on the agreement.
            // The actual emailing happens once, after SaveChangesAsync, in
            // RetryUnsentNoticeEmailsAsync - which also picks up any earlier failed send.

            // --- Stage 1: remind the tenant about each newly missed month -----------------
            foreach (var payment in overdue)
            {
                if (notices.Any(n => n.NoticeType == ArrearsNoticeType.Reminder && n.PaymentID == payment.PaymentID))
                {
                    continue;
                }

                AddNotice(agreement, payment, ArrearsNoticeType.Reminder, today,
                    ArrearsPolicy.WholeMonthsBetween(payment.DueDate, today), 0,
                    $"Reminder: rent of RM {payment.Amount:N0} for month {payment.RentMonthNumber} "
                    + $"was due on {payment.DueDate:d MMM yyyy} and has not been received. "
                    + $"Please settle it to avoid a deduction from your security deposit.");
            }

            // --- Stage 2: take the arrears out of the security deposit --------------------
            // Only money the owner actually holds can be applied to the arrears, so a deposit
            // the tenant never paid is not available to raid.
            var depositHeld = payments.Any(p => p.PaymentType == PaymentType.Deposit
                                                && p.PaymentStatus == PaymentStatus.Paid);

            if (monthsMissed >= ArrearsPolicy.DepositDeductionStage && depositHeld && agreement.DepositBalance > 0)
            {
                foreach (var payment in overdue.ToList())
                {
                    // Only whole instalments are settled - a half-covered month would leave
                    // the owner chasing a balance the payment record cannot express.
                    if (agreement.DepositBalance < payment.Amount)
                    {
                        break;
                    }

                    agreement.DepositBalance -= payment.Amount;
                    payment.PaymentStatus = PaymentStatus.Paid;
                    payment.SettledFromDeposit = true;
                    payment.PaymentDate = today;
                    payment.ReceiptPath = "Settled from security deposit";

                    AddNotice(agreement, payment, ArrearsNoticeType.DepositDeduction, today,
                        ArrearsPolicy.WholeMonthsBetween(payment.DueDate, today), payment.Amount,
                        $"RM {payment.Amount:N0} was deducted from the security deposit to cover unpaid rent "
                        + $"for month {payment.RentMonthNumber} (due {payment.DueDate:d MMM yyyy}). "
                        + $"Remaining deposit: RM {agreement.DepositBalance:N0}. "
                        + "The deposit must be topped up before the end of the tenancy.");

                    overdue.Remove(payment);
                }
            }

            // --- Stage 3: compound the tenant for rent still unpaid in the third month ----
            if (monthsMissed >= ArrearsPolicy.CompoundStage && overdue.Count > 0)
            {
                var alreadyCompounded = notices
                    .Where(n => n.NoticeType == ArrearsNoticeType.Compound && n.PaymentID.HasValue)
                    .Select(n => n.PaymentID.Value)
                    .ToHashSet();

                var trigger = overdue.FirstOrDefault(p => !alreadyCompounded.Contains(p.PaymentID));
                if (trigger != null)
                {
                    var outstandingRent = overdue.Sum(p => p.Amount);
                    var compoundAmount = ArrearsPolicy.CalculateCompound(outstandingRent);

                    var compound = new Payment
                    {
                        AgreementID = agreement.AgreementID,
                        Agreement = agreement,
                        DueDate = today.AddDays(ArrearsPolicy.CompoundDueDays),
                        Amount = compoundAmount,
                        PaymentType = PaymentType.Compound,
                        PaymentStatus = PaymentStatus.Pending,
                        RentMonthNumber = 0,
                        ReceiptPath = string.Empty
                    };
                    _context.Payments.Add(compound);

                    AddNotice(agreement, trigger, ArrearsNoticeType.Compound, today,
                        ArrearsPolicy.WholeMonthsBetween(trigger.DueDate, today), compoundAmount,
                        $"Compound of RM {compoundAmount:N0} issued: rent has now been outstanding for "
                        + $"{monthsMissed} months (RM {outstandingRent:N0} unpaid) and the security deposit "
                        + $"no longer covers it. The compound is payable by {compound.DueDate:d MMM yyyy}, "
                        + "after which the Landlord may terminate the tenancy.");
                }
            }

            // Compounds fall overdue on their own due date, with no grace period.
            foreach (var compound in payments.Where(p => p.PaymentType == PaymentType.Compound
                                                         && p.PaymentStatus == PaymentStatus.Pending
                                                         && p.DueDate < today))
            {
                compound.PaymentStatus = PaymentStatus.Overdue;
            }

            await _context.SaveChangesAsync();

            // Email the notices just raised, plus any earlier one whose send failed.
            await RetryUnsentNoticeEmailsAsync(agreement);

            return Summarise(agreement, agreement.Payments, agreement.ArrearsNotices, today);
        }

        // Evaluates every active tenancy on the owner's properties, newest arrears first.
        public async Task<List<ArrearsSummary>> EvaluateForOwnerAsync(int ownerId, DateTime? asOf = null)
        {
            var agreements = await _context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property)
                .Include(a => a.Application).ThenInclude(app => app.Tenant)
                .Include(a => a.Payments)
                .Include(a => a.ArrearsNotices)
                .Where(a => a.Application.Property.OwnerID == ownerId && a.Status == AgreementStatus.Active)
                .ToListAsync();

            var summaries = new List<ArrearsSummary>();
            foreach (var agreement in agreements)
            {
                summaries.Add(await EvaluateAsync(agreement, asOf));
            }

            return summaries
                .OrderByDescending(s => s.MonthsMissed)
                .ThenByDescending(s => s.OutstandingRent)
                .ToList();
        }

        // Ends the tenancy for non-payment, per clause 3: only once a compound has been issued
        // and has itself gone unpaid past its own due date. Returns false if that condition
        // isn't met (nothing to terminate for, or the tenancy is no longer active).
        public async Task<bool> TerminateForArrearsAsync(RentalAgreement agreement, DateTime? asOf = null)
        {
            var today = (asOf ?? DateTime.Today).Date;

            if (agreement.Status != AgreementStatus.Active)
            {
                return false;
            }

            var overdueCompound = agreement.Payments
                .Where(p => p.PaymentType == PaymentType.Compound && p.PaymentStatus == PaymentStatus.Overdue)
                .OrderBy(p => p.DueDate)
                .FirstOrDefault();

            if (overdueCompound == null)
            {
                return false;
            }

            agreement.Status = AgreementStatus.Terminated;

            var notice = AddNotice(agreement, overdueCompound, ArrearsNoticeType.Termination, today,
                ArrearsPolicy.WholeMonthsBetween(overdueCompound.DueDate, today), 0,
                $"Tenancy terminated by the Landlord for non-payment: the compound of RM {overdueCompound.Amount:N0} "
                + $"fell due on {overdueCompound.DueDate:d MMM yyyy} and remains unpaid.");

            await _context.SaveChangesAsync();

            await NotifyTenantAsync(agreement, notice);
            return true;
        }

        // Marks the tenant's unread notices as seen once they have opened the agreement page.
        public async Task AcknowledgeAsync(RentalAgreement agreement)
        {
            var unseen = agreement.ArrearsNotices.Where(n => !n.IsAcknowledged).ToList();
            if (unseen.Count == 0)
            {
                return;
            }

            foreach (var notice in unseen)
            {
                notice.IsAcknowledged = true;
            }

            await _context.SaveChangesAsync();
        }

        private ArrearsNotice AddNotice(RentalAgreement agreement, Payment payment, ArrearsNoticeType type,
            DateTime issuedDate, int monthsOverdue, decimal amount, string details)
        {
            var notice = new ArrearsNotice
            {
                AgreementID = agreement.AgreementID,
                Agreement = agreement,
                PaymentID = payment?.PaymentID > 0 ? payment.PaymentID : null,
                Payment = payment?.PaymentID > 0 ? payment : null,
                NoticeType = type,
                IssuedDate = issuedDate,
                MonthsOverdue = monthsOverdue,
                Amount = amount,
                Details = details,
                IsAcknowledged = false
            };

            _context.ArrearsNotices.Add(notice);

            // Keep the in-memory graph consistent for the view that rendered this request.
            agreement.ArrearsNotices ??= new List<ArrearsNotice>();
            if (!agreement.ArrearsNotices.Contains(notice))
            {
                agreement.ArrearsNotices.Add(notice);
            }

            return notice;
        }

        // Emails every notice on the agreement that has not been sent yet (EmailedDate null):
        // the ones just raised on this pass, and any earlier notice whose send previously
        // failed. Ordered oldest-first so the tenant receives them in the order they were issued.
        private async Task RetryUnsentNoticeEmailsAsync(RentalAgreement agreement)
        {
            var unsent = (agreement.ArrearsNotices ?? new List<ArrearsNotice>())
                .Where(n => n.EmailedDate == null)
                .OrderBy(n => n.IssuedDate)
                .ThenBy(n => n.ArrearsNoticeID)
                .ToList();

            foreach (var notice in unsent)
            {
                await NotifyTenantAsync(agreement, notice);
            }
        }

        // Emails the tenant the wording recorded on the notice and stamps EmailedDate once
        // the send succeeds. A failure is logged and swallowed - the notice is already saved
        // and shown in-app, so a flaky mail server must not break the page that triggered the
        // evaluation; EmailedDate stays null and the next evaluation retries it.
        private async Task NotifyTenantAsync(RentalAgreement agreement, ArrearsNotice notice)
        {
            if (notice.EmailedDate != null)
            {
                return;
            }

            var tenant = agreement.Application?.Tenant
                ?? await _context.RentalApplications
                    .Where(a => a.ApplicationID == agreement.ApplicationID)
                    .Select(a => a.Tenant)
                    .FirstOrDefaultAsync();

            if (tenant == null || string.IsNullOrWhiteSpace(tenant.Email))
            {
                return;
            }

            var property = agreement.Application?.Property;
            var propertyLine = property != null ? $"Property: {property.Title}, {property.City}\n\n" : "";

            var body = $"Hi {tenant.FullName},\n\n"
                + propertyLine
                + notice.Details
                + "\n\nYou can review this and settle any outstanding amount from your rental agreement page on RentEasy.\n\n"
                + "- RentEasy";

            try
            {
                await _emailSender.SendAsync(tenant.Email, SubjectFor(notice.NoticeType), body);
                notice.EmailedDate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to email {NoticeType} notice to tenant {TenantId} for agreement {AgreementId}; will retry on the next evaluation.",
                    notice.NoticeType, tenant.UserID, agreement.AgreementID);
            }
        }

        private static string SubjectFor(ArrearsNoticeType type) => type switch
        {
            ArrearsNoticeType.Reminder => "Rent overdue - reminder from RentEasy",
            ArrearsNoticeType.DepositDeduction => "RentEasy: security deposit used to cover unpaid rent",
            ArrearsNoticeType.Compound => "RentEasy: late-payment compound charged to your tenancy",
            ArrearsNoticeType.Termination => "RentEasy: your tenancy has been terminated for non-payment",
            _ => "Update on your RentEasy tenancy"
        };

        private static ArrearsSummary Summarise(RentalAgreement agreement, ICollection<Payment> payments,
            ICollection<ArrearsNotice> notices, DateTime today)
        {
            payments ??= new List<Payment>();
            notices ??= new List<ArrearsNotice>();

            var unpaidRent = payments
                .Where(p => p.PaymentType == PaymentType.Rent
                            && p.PaymentStatus != PaymentStatus.Paid
                            && p.DueDate.AddDays(ArrearsPolicy.GraceDays) < today)
                .ToList();

            var settledFromDeposit = payments.Count(p => p.PaymentType == PaymentType.Rent && p.SettledFromDeposit);

            return new ArrearsSummary
            {
                Agreement = agreement,
                MonthsMissed = unpaidRent.Count + settledFromDeposit,
                OutstandingRent = unpaidRent.Sum(p => p.Amount),
                OutstandingCompounds = payments
                    .Where(p => p.PaymentType == PaymentType.Compound && p.PaymentStatus != PaymentStatus.Paid)
                    .Sum(p => p.Amount),
                DepositDeducted = agreement.DepositAmount - agreement.DepositBalance,
                OldestOverdueDate = unpaidRent.OrderBy(p => p.DueDate).FirstOrDefault()?.DueDate,
                LatestNotice = notices.OrderByDescending(n => n.IssuedDate)
                                      .ThenByDescending(n => n.ArrearsNoticeID)
                                      .FirstOrDefault()
            };
        }
    }
}
