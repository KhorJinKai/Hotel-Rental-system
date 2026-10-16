using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;
using Stripe;
using Stripe.Checkout;

namespace HouseRentalSystem.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(ApplicationDbContext context, IConfiguration configuration, IEmailSender emailSender, ILogger<PaymentController> logger)
        {
            _context = context;
            _configuration = configuration;
            _emailSender = emailSender;
            _logger = logger;
        }

        // POST: /Payment/Checkout/12  (Tenant starts a Stripe Checkout session for a pending deposit/rent payment)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Checkout(int id)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var payment = await _context.Payments
                .Include(p => p.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Property)
                .Include(p => p.Agreement).ThenInclude(a => a.Payments)
                .FirstOrDefaultAsync(p => p.PaymentID == id);

            if (payment == null)
            {
                return NotFound();
            }

            if (payment.Agreement.Application.TenantID != tenantId)
            {
                return Forbid();
            }

            // The tenant must accept the tenancy agreement before its deposit can be paid.
            // The "I agree" step (RentalApplicationController.AcceptTerms) is also gated in the
            // view, but enforce it here too so the deposit can't be paid by posting straight
            // to this action.
            if (payment.PaymentType == PaymentType.Deposit
                && payment.Agreement.Application.TermsAcceptedDate == null)
            {
                TempData["Notification"] = "Please accept the tenancy agreement before paying the deposit.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
            }

            // Overdue rent and compounds are still payable - only an already settled item is not.
            if (payment.PaymentStatus == PaymentStatus.Paid)
            {
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
            }

            // Rent is cleared strictly in month order - the tenant cannot skip an unpaid month
            // and pay a later one. Deposit and compounds sit outside this ordering.
            if (payment.PaymentType == PaymentType.Rent)
            {
                var earlierUnpaid = payment.Agreement.Payments
                    .Where(p => p.PaymentType == PaymentType.Rent
                                && p.PaymentStatus != PaymentStatus.Paid
                                && p.RentMonthNumber < payment.RentMonthNumber)
                    .OrderBy(p => p.RentMonthNumber)
                    .FirstOrDefault();

                if (earlierUnpaid != null)
                {
                    TempData["Notification"] = $"Please settle month {earlierUnpaid.RentMonthNumber} rent "
                        + $"(due {earlierUnpaid.DueDate:d MMM yyyy}) before paying month {payment.RentMonthNumber}.";
                    return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
                }
            }

            var propertyTitle = payment.Agreement.Application.Property.Title;
            var description = payment.PaymentType switch
            {
                PaymentType.Deposit => $"Security deposit - {propertyTitle}",
                PaymentType.Compound => $"Late payment compound - {propertyTitle}",
                _ => $"Month {payment.RentMonthNumber} rent ({payment.DueDate:MMM yyyy}) - {propertyTitle}"
            };

            // Stripe fills in the real session id in place of this literal placeholder on redirect.
            var successUrl = Url.Action(nameof(Success), "Payment", new { paymentId = payment.PaymentID }, Request.Scheme)
                + "&session_id={CHECKOUT_SESSION_ID}";
            var cancelUrl = Url.Action(nameof(Cancelled), "Payment", new { paymentId = payment.PaymentID }, Request.Scheme);

            var currency = _configuration["Stripe:Currency"] ?? "myr";

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = currency,
                            UnitAmount = (long)Math.Round(payment.Amount * 100, MidpointRounding.AwayFromZero),
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = description
                            }
                        }
                    }
                },
                Metadata = new Dictionary<string, string> { { "PaymentID", payment.PaymentID.ToString() } },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl
            };

            var service = new SessionService();
            Session session;
            try
            {
                session = await service.CreateAsync(options);
            }
            catch (StripeException ex)
            {
                TempData["Notification"] = $"Could not start payment: {ex.Message}";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
            }

            return Redirect(session.Url);
        }

        // GET: /Payment/Success?paymentId=12&session_id=cs_test_...
        // Stripe redirects the tenant here after checkout. The session is re-verified against Stripe's
        // API server-side (never trust the redirect query string alone) before marking the payment paid.
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Success(int paymentId, string session_id)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var payment = await _context.Payments
                .Include(p => p.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Property).ThenInclude(prop => prop.Owner)
                .Include(p => p.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Tenant)
                .FirstOrDefaultAsync(p => p.PaymentID == paymentId);

            if (payment == null)
            {
                return NotFound();
            }

            if (payment.Agreement.Application.TenantID != tenantId)
            {
                return Forbid();
            }

            if (payment.PaymentStatus != PaymentStatus.Paid && !string.IsNullOrEmpty(session_id))
            {
                var service = new SessionService();
                Session session;
                try
                {
                    session = await service.GetAsync(session_id);
                }
                catch (StripeException)
                {
                    TempData["Notification"] = "We couldn't confirm your payment with Stripe. Please try again.";
                    return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
                }

                var sessionPaymentId = session.Metadata != null && session.Metadata.TryGetValue("PaymentID", out var pid) ? pid : null;

                if (session.PaymentStatus == "paid" && sessionPaymentId == payment.PaymentID.ToString())
                {
                    // Claim this payment as paid with a single conditional UPDATE. If the tenant
                    // refreshed the Stripe return page and a concurrent request already recorded
                    // it, this touches 0 rows and the one-time side effects below (receipt email,
                    // property reservation) are skipped.
                    var receiptRef = session.PaymentIntentId ?? session.Id;
                    var justPaid = await _context.Payments
                        .Where(p => p.PaymentID == payment.PaymentID && p.PaymentStatus != PaymentStatus.Paid)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(p => p.PaymentStatus, PaymentStatus.Paid)
                            .SetProperty(p => p.PaymentDate, DateTime.Now)
                            .SetProperty(p => p.ReceiptPath, receiptRef));

                    if (justPaid > 0)
                    {
                        // Bring the tracked entity in step with the row just updated so the
                        // receipt renders the settled payment; don't let SaveChanges rewrite it.
                        payment.PaymentStatus = PaymentStatus.Paid;
                        payment.PaymentDate = DateTime.Now;
                        payment.ReceiptPath = receiptRef;
                        _context.Entry(payment).State = EntityState.Unchanged;

                        // The payment is already recorded at this point - an email hiccup shouldn't
                        // make the tenant think the payment itself failed, so this is best-effort.
                        try
                        {
                            var tenantEmail = payment.Agreement.Application.Tenant.Email;
                            var subject = $"Your RentEasy payment receipt - {ReceiptService.ReceiptNumber(payment)}";
                            var emailBody = ReceiptService.BuildEmailHtml(payment);
                            await _emailSender.SendAsync(tenantEmail, subject, emailBody, isHtml: true);
                            TempData["Notification"] = $"Payment successful. A receipt has been emailed to {tenantEmail}.";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to email receipt for payment {PaymentId}.", payment.PaymentID);
                            TempData["Notification"] = "Payment successful. We couldn't email your receipt, but you can view it any time from this page.";
                        }

                        // A paid deposit is what actually reserves the property and sends the
                        // request to the owner - see RentalApplicationController.Create, which
                        // creates the agreement and this payment but leaves the application
                        // AwaitingPayment and the property untouched until this point.
                        var application = payment.Agreement.Application;
                        if (payment.PaymentType == PaymentType.Deposit && application.Status == ApplicationStatus.AwaitingPayment)
                        {
                            // Reserve the property with one atomic UPDATE ... WHERE IsAvailable = 1,
                            // so that when two tenants' deposits for the same property clear at
                            // almost the same moment, exactly one reservation stands and the other
                            // is refunded.
                            var reserved = await _context.Properties
                                .Where(p => p.PropertyID == application.PropertyID && p.IsAvailable)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsAvailable, false));

                            if (reserved == 0)
                            {
                                application.Status = ApplicationStatus.Rejected;
                                application.RejectionReason = "This property was reserved by another tenant while your payment was being processed.";
                                payment.Agreement.Status = AgreementStatus.Terminated;

                                _context.Payments.Add(new Payment
                                {
                                    AgreementID = payment.Agreement.AgreementID,
                                    DueDate = DateTime.Now,
                                    PaymentDate = DateTime.Now,
                                    Amount = payment.Amount,
                                    PaymentType = PaymentType.Refund,
                                    PaymentStatus = PaymentStatus.Paid,
                                    RentMonthNumber = 0,
                                    ReceiptPath = "Automatic refund - property reserved by another tenant"
                                });

                                payment.Agreement.DepositBalance = 0;

                                TempData["Notification"] = "This property was just reserved by another tenant. Your deposit has been refunded in full.";
                            }
                            else
                            {
                                application.Status = ApplicationStatus.Pending;
                                application.Property.IsAvailable = false;
                                _context.Entry(application.Property).State = EntityState.Unchanged;
                            }

                            await _context.SaveChangesAsync();
                        }
                    }
                }
                else
                {
                    TempData["Notification"] = "Payment could not be confirmed yet. If you completed checkout, please refresh in a moment.";
                }
            }

            return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
        }

        // GET: /Payment/Cancelled?paymentId=12  (Tenant backed out of Stripe Checkout without paying)
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Cancelled(int paymentId)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var payment = await _context.Payments
                .Include(p => p.Agreement).ThenInclude(a => a.Application)
                .FirstOrDefaultAsync(p => p.PaymentID == paymentId);

            if (payment == null)
            {
                return NotFound();
            }

            if (payment.Agreement.Application.TenantID != tenantId)
            {
                return Forbid();
            }

            TempData["Notification"] = "Payment cancelled.";
            return RedirectToAction("Details", "RentalAgreement", new { applicationId = payment.Agreement.ApplicationID });
        }

        // GET: /Payment/Receipt/12  (Tenant or the property's owner can view/print a paid receipt)
        [Authorize(Roles = "Owner,Tenant")]
        public async Task<IActionResult> Receipt(int id)
        {
            var payment = await LoadReceiptPaymentAsync(id);
            if (payment == null)
            {
                return NotFound();
            }

            if (!IsPartyToPayment(payment))
            {
                return Forbid();
            }

            if (payment.PaymentStatus != PaymentStatus.Paid)
            {
                return NotFound();
            }

            return View(payment);
        }

        // POST: /Payment/ResendReceipt/12  (Tenant re-triggers the receipt email, e.g. if it was missed)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> ResendReceipt(int id)
        {
            var payment = await LoadReceiptPaymentAsync(id);
            if (payment == null)
            {
                return NotFound();
            }

            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (payment.Agreement.Application.TenantID != tenantId || payment.PaymentStatus != PaymentStatus.Paid)
            {
                return Forbid();
            }

            try
            {
                var tenantEmail = payment.Agreement.Application.Tenant.Email;
                var subject = $"Your RentEasy payment receipt - {ReceiptService.ReceiptNumber(payment)}";
                await _emailSender.SendAsync(tenantEmail, subject, ReceiptService.BuildEmailHtml(payment), isHtml: true);
                TempData["Notification"] = $"Receipt resent to {tenantEmail}.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resend receipt for payment {PaymentId}.", payment.PaymentID);
                TempData["Notification"] = "Couldn't send the email right now - please try again shortly.";
            }

            return RedirectToAction(nameof(Receipt), new { id });
        }

        private async Task<Payment> LoadReceiptPaymentAsync(int id)
        {
            return await _context.Payments
                .Include(p => p.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Property).ThenInclude(prop => prop.Owner)
                .Include(p => p.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Tenant)
                .FirstOrDefaultAsync(p => p.PaymentID == id);
        }

        private bool IsPartyToPayment(Payment payment)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            return payment.Agreement.Application.TenantID == userId || payment.Agreement.Application.Property.OwnerID == userId;
        }
    }
}
