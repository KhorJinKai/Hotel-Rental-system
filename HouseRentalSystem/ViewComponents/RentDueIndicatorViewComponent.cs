using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HouseRentalSystem.ViewComponents
{
    // Drives the small red dot on the tenant's "My bookings" nav link. It reads straight
    // from the payment due dates, so the dot appears the moment a rent instalment (or a
    // compound) goes past its due date unpaid - it does not wait for the arrears ladder
    // in ArrearsService to run on a page view.
    public class RentDueIndicatorViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public RentDueIndicatorViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (User.Identity is not { IsAuthenticated: true } || !User.IsInRole("Tenant"))
            {
                return Content(string.Empty);
            }

            var tenantId = int.Parse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            var overdueCount = await _context.Payments
                .Where(p => p.Agreement.Application.TenantID == tenantId
                            && p.Agreement.Status == AgreementStatus.Active
                            && (p.PaymentType == PaymentType.Rent || p.PaymentType == PaymentType.Compound)
                            && p.PaymentStatus != PaymentStatus.Paid
                            && p.DueDate < today)
                .CountAsync();

            return View(overdueCount);
        }
    }
}
