using Microsoft.EntityFrameworkCore;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Services
{
    // Runs the late-rent ladder (ArrearsService) across every active tenancy on a timer,
    // so reminders - and the deposit deduction / compound steps after them - are raised and
    // emailed even when neither the tenant nor the owner opens a page. Page views still
    // trigger the same evaluation on demand; this is the safety net. Every action the ladder
    // takes is idempotent and duplicate-guarded, so an extra daily pass changes nothing.
    public class ArrearsReminderHostedService : BackgroundService
    {
        // Rent is a daily-granularity concern, so one sweep a day is enough. The first runs
        // shortly after startup rather than immediately, to stay out of the way of the
        // migration + demo-data seeding that Program.cs does on boot.
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ArrearsReminderHostedService> _logger;

        public ArrearsReminderHostedService(IServiceScopeFactory scopeFactory,
            ILogger<ArrearsReminderHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!await DelayAsync(StartupDelay, stoppingToken))
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Arrears reminder sweep failed; will try again next interval.");
                }

                if (!await DelayAsync(Interval, stoppingToken))
                {
                    return;
                }
            }
        }

        // Waits for the given delay, returning false if shutdown was requested while waiting.
        private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task SweepAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var arrears = scope.ServiceProvider.GetRequiredService<ArrearsService>();

            var agreements = await context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property)
                .Include(a => a.Application).ThenInclude(app => app.Tenant)
                .Include(a => a.Payments)
                .Include(a => a.ArrearsNotices)
                .Where(a => a.Status == AgreementStatus.Active)
                .ToListAsync(stoppingToken);

            _logger.LogInformation("Arrears reminder sweep: evaluating {Count} active tenancies.", agreements.Count);

            foreach (var agreement in agreements)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await arrears.EvaluateAsync(agreement);
            }
        }
    }
}
