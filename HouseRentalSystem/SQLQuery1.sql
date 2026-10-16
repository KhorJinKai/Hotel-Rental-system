-- The troubled tenancy = "Fully Furnished Single Room in PJ", landlord owner2@rental.com
DECLARE @agrID int = (
    SELECT ag.AgreementID
    FROM RentalAgreements ag
    JOIN RentalApplications ra ON ra.ApplicationID = ag.ApplicationID
    JOIN Properties p          ON p.PropertyID = ra.PropertyID
    WHERE p.Title = 'Fully Furnished Single Room in PJ' AND ag.Status = 0);

DECLARE @tenantID int = (
    SELECT ra.TenantID FROM RentalAgreements ag
    JOIN RentalApplications ra ON ra.ApplicationID = ag.ApplicationID
    WHERE ag.AgreementID = @agrID);

-- point that tenant at your real inbox (safe if it already is)
UPDATE Users SET Email = 'amgone45@gmail.com'
WHERE UserID = @tenantID
  AND NOT EXISTS (SELECT 1 FROM Users x WHERE x.Email = 'amgone45@gmail.com' AND x.UserID <> @tenantID);

-- rewind the ladder so the emails re-fire
DELETE FROM ArrearsNotices WHERE AgreementID = @agrID;
DELETE FROM Payments WHERE AgreementID = @agrID AND PaymentType = 2;
UPDATE RentalAgreements SET DepositBalance = DepositAmount WHERE AgreementID = @agrID;
UPDATE Payments
SET PaymentStatus = 1, SettledFromDeposit = 0, PaymentDate = NULL, ReceiptPath = ''
WHERE AgreementID = @agrID AND PaymentType = 0 AND RentMonthNumber >= 3;