using HouseRentalSystem.Data;
using HouseRentalSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Stripe;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Session backs the self-contained CAPTCHA on the sign-in / register / forgot-password
// forms (CaptchaService): the expected answer is held server-side and never sent to
// the browser. AddDistributedMemoryCache is the in-process store Session needs.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddSingleton<CaptchaService>();

// Stripe Checkout is used for deposit/rent payments (see PaymentController).
// The secret key is read from user-secrets in development (never committed) -
// set it with: dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
// Register EF Core with SQL Server Express
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(3),
            errorNumbersToAdd: null)));

// Late-rent handling (reminder -> deposit deduction -> compound). Runs on demand when an
// agreement or the owner's rent collection page is opened, and once a day for every active
// tenancy via the hosted service below, so reminders still go out if nobody opens a page.
builder.Services.AddScoped<ArrearsService>();
builder.Services.AddHostedService<ArrearsReminderHostedService>();

// Counts behind the admin "needs attention" banner and the red dots on the admin nav
// (booking approvals / maintenance / support / cancellation requests). Scoped so the
// layout computes them once per page load.
builder.Services.AddScoped<AdminAlertsService>();

// Registration verification emails. Falls back to logging instead of sending when no
// Smtp:Host is configured, so the app still works out of the box without SMTP secrets.
if (string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
{
    builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}

// Register cookie-based authentication (per assignment: do NOT use ASP.NET Core Identity)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Security/Login";
        options.AccessDeniedPath = "/Security/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    });
var app = builder.Build();

// Apply any pending EF Core migrations automatically, so a fresh LocalDB instance on a
// teammate's machine gets created/updated without a manual `dotnet ef database update`.
// Then add demo listings/bookings/agreements/payments on top of the migration-seeded
// accounts, so there is something to demonstrate beyond three empty logins. Idempotent
// (see DemoDataSeeder).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    try
    {
        await DemoDataSeeder.SeedAsync(db);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Skipped demo data seeding.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();