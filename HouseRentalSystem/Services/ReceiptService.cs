using System.Net;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Services
{
    // Builds the e-receipt for a paid Payment - both the HTML emailed to the tenant and
    // the on-site view (PaymentController.Receipt) render from the same field values, so
    // the two never drift apart even though the email needs fully inline styles.
    public static class ReceiptService
    {
        public static string ReceiptNumber(Payment payment) => $"RE-{payment.PaymentID:D6}";

        public static string Describe(Payment payment) => payment.PaymentType switch
        {
            PaymentType.Deposit => "Security deposit",
            PaymentType.Compound => "Late payment compound",
            PaymentType.Refund => "Refund",
            _ => $"Rent - month {payment.RentMonthNumber}"
        };

        // Requires payment.Agreement.Application.Property.Owner and .Tenant to be loaded.
        public static string BuildEmailHtml(Payment payment)
        {
            var agreement = payment.Agreement;
            var property = agreement.Application.Property;
            var tenant = agreement.Application.Tenant;
            var owner = property.Owner;

            string Row(string label, string value, bool strong = false) =>
                $"<tr><td style=\"padding:8px 0;color:#576373;font-size:13.5px;\">{WebUtility.HtmlEncode(label)}</td>" +
                $"<td style=\"padding:8px 0;text-align:right;font-size:13.5px;{(strong ? "font-weight:700;color:#1F2B3A;" : "")}\">{WebUtility.HtmlEncode(value)}</td></tr>";

            return $@"
<div style=""font-family:'Segoe UI',Arial,sans-serif;max-width:560px;margin:0 auto;color:#1F2B3A;"">
  <div style=""background:#0C447C;padding:20px 28px;border-radius:8px 8px 0 0;"">
    <span style=""color:#ffffff;font-size:20px;font-weight:800;"">RentEasy</span>
  </div>
  <div style=""border:1px solid #E3E7EC;border-top:none;padding:28px;border-radius:0 0 8px 8px;"">
    <h2 style=""margin:0 0 4px;font-size:18px;"">Payment receipt</h2>
    <p style=""margin:0 0 20px;color:#576373;font-size:13px;"">
      Receipt #{WebUtility.HtmlEncode(ReceiptNumber(payment))} &middot; {(payment.PaymentDate ?? payment.DueDate):d MMM yyyy, h:mm tt}
    </p>
    <table style=""width:100%;border-collapse:collapse;"">
      {Row("Property", property.Title)}
      {Row("Description", Describe(payment))}
      {Row("Paid by", tenant.FullName)}
      {Row("Paid to", owner.FullName)}
      {Row("Payment method", "Card via Stripe")}
      {Row("Reference", payment.ReceiptPath ?? "-")}
    </table>
    <div style=""border-top:1px solid #E3E7EC;margin-top:8px;padding-top:14px;display:flex;justify-content:space-between;align-items:center;"">
      <span style=""font-weight:700;font-size:14px;"">Amount paid</span>
      <span style=""font-weight:800;font-size:20px;color:#0F6E56;"">RM {payment.Amount:N2}</span>
    </div>
    <p style=""margin-top:24px;font-size:12px;color:#8A94A1;"">
      This is an automated receipt for your records. You can also view it any time from your rental agreement page.
    </p>
  </div>
  <p style=""text-align:center;font-size:11px;color:#8A94A1;margin-top:16px;"">RentEasy &middot; House / Room Rental System</p>
</div>";
        }
    }
}
