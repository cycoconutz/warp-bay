using Microsoft.EntityFrameworkCore;
using WarpBay.Api.Auth;
using WarpBay.Api.Data;
using WarpBay.Api.Models;

namespace WarpBay.Api.Seed;

// Fictional demo universe for "Warp Bay Auto Lab" — neon synthwave shop, BAY-07 flagship.
// All names/plates/phones are invented. DISCLAIMER: fictional demo data.
public static class SeedData
{
    public static async Task EnsureSeededAsync(WarpBayDb db)
    {
        await db.Database.EnsureCreatedAsync();
        if (!await db.Shops.AnyAsync())
            await SeedAllAsync(db);
        // Always sync the catalog so redeploys pick up service changes
        // without wiping appointments that reference old services.
        await SyncServiceCatalogAsync(db);
    }

    // Common vehicle services shown in the booking dropdown.
    // Runs on every boot: adds missing entries, refreshes duration/price,
    // and deactivates retired names (history stays intact via FK).
    public static async Task SyncServiceCatalogAsync(WarpBayDb db)
    {
        var existing = await db.Services.ToListAsync();
        foreach (var (name, mins, cents) in ServiceCatalog())
        {
            var match = existing.FirstOrDefault(s => s.Name == name);
            if (match is null)
                db.Services.Add(new ServiceOffering { Name = name, DurationMinutes = mins, PriceCents = cents });
            else
            {
                match.DurationMinutes = mins;
                match.PriceCents = cents;
                match.IsActive = true;
            }
        }
        var names = ServiceCatalog().Select(c => c.Name).ToHashSet();
        foreach (var s in existing.Where(s => !names.Contains(s.Name)))
            s.IsActive = false;
        await db.SaveChangesAsync();
    }

    private static (string Name, int Mins, int Cents)[] ServiceCatalog() =>
    [
        ("Oil Change", 30, 4999),
        ("Tire Rotation", 30, 2999),
        ("Brake Pad Replacement", 90, 19999),
        ("Wheel Alignment", 60, 9999),
        ("Battery Replacement", 30, 14999),
        ("Engine Air Filter Replacement", 15, 2999),
        ("Cabin Air Filter Replacement", 15, 3499),
        ("Spark Plug Replacement", 90, 18999),
        ("Transmission Fluid Service", 90, 17999),
        ("Coolant Flush", 60, 12999),
        ("A/C Inspection & Recharge", 60, 13999),
        ("Diagnostic Scan", 45, 8999),
        ("Wiper Blade Replacement", 15, 2499),
        ("Multipoint Inspection", 30, 0),
    ];

    public static async Task ResetDemoAsync(WarpBayDb db)
    {
        foreach (var set in new object[]
            { db.Reminders, db.StatusHistory, db.Appointments, db.Vehicles, db.Customers })
            db.RemoveRange((System.Collections.IEnumerable)set);
        await db.SaveChangesAsync();
        await SeedDynamicAsync(db);
    }

    private static async Task SeedAllAsync(WarpBayDb db)
    {
        var shop = new Shop { Name = "Warp Bay Auto Lab", TimeZone = "America/Los_Angeles", OpenHour = 8, CloseHour = 18 };
        db.Shops.Add(shop);

        var bays = new[] { "BAY-01", "BAY-02", "BAY-03", "BAY-04", "WARP BAY-07" }
            .Select(n => new Bay { ShopId = shop.Id, Name = n }).ToList();
        db.Bays.AddRange(bays);

        var techs = new (string Name, string Color)[]
        {
            ("Nova Reyes", "#22d3ee"), ("Jax Volt", "#f472b6"),
            ("Kilo Park", "#a78bfa"), ("Zed Marlowe", "#34d399"),
        }.Select(t => new TechProfile { DisplayName = t.Name, Color = t.Color }).ToList();
        db.Techs.AddRange(techs);

        await SyncServiceCatalogAsync(db);

        // Demo logins (same password everywhere for one-click explore)
        var users = new (string Email, string Role, string Name)[]
        {
            ("admin@warp-bay.demo", Roles.Admin, "Ada Admin"),
            ("manager@warp-bay.demo", Roles.Manager, "Mara Manager"),
            ("tech@warp-bay.demo", Roles.Tech, "Theo Tech"),
            ("customer@warp-bay.demo", Roles.Customer, "Cam Customer"),
        }.Select(u =>
        {
            var user = new AppUser { Email = u.Email, Role = u.Role, DisplayName = u.Name };
            user.PasswordHash = Passwords.Hash(user, "Demo123!");
            return user;
        }).ToList();
        db.Users.AddRange(users);
        // link tech demo user to first tech profile
        await db.SaveChangesAsync();
        techs[0].UserId = users.First(u => u.Role == Roles.Tech).Id;

        await db.SaveChangesAsync();
        await SeedDynamicAsync(db);
    }

    private static async Task SeedDynamicAsync(WarpBayDb db)
    {
        var rnd = new Random(2077);
        var shop = await db.Shops.FirstAsync();
        var bays = await db.Bays.ToListAsync();
        var techs = await db.Techs.ToListAsync();
        var services = await db.Services.ToListAsync();

        var first = new[] { "Ava", "Rin", "Milo", "Zoe", "Kai", "Lena", "Rex", "Ivy", "Owen", "Maya", "Theo", "June", "Felix", "Nia", "Otis" };
        var last = new[] { "Stardust", "Quasar", "Nebula", "Vortex", "Comet", "Pulsar", "Zenith", "Flux", "Drift", "Nova" };
        var makes = new[] { ("Volta", "Spark"), ("Nebula", "Cruiser"), ("Quasar", "Runner"), ("Ion", "Hatch"), ("Comet", "GT") };
        var plates = new[] { "WRP-101", "NBL-207", "QSR-309", "VLX-411", "CMT-512", "FLX-618", "ZTH-704", "PLS-808" };

        var customers = Enumerable.Range(0, 25).Select(i => new Customer
        {
            FullName = $"{first[rnd.Next(first.Length)]} {last[rnd.Next(last.Length)]}",
            Phone = $"(555) 010-{rnd.Next(1000, 9999)}",
            Email = $"driver{i:00}@example.com",
        }).ToList();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();

        var vehicles = customers.Take(20).Select((c, i) =>
        {
            var (mk, md) = makes[rnd.Next(makes.Length)];
            return new Vehicle
            {
                CustomerId = c.Id, Plate = plates[i % plates.Length] + $"-{i}",
                Year = (2016 + rnd.Next(9)).ToString(), Make = mk, Model = md,
            };
        }).ToList();
        db.Vehicles.AddRange(vehicles);
        await db.SaveChangesAsync();

        var statuses = new[] { ApptStatus.PickedUp, ApptStatus.Ready, ApptStatus.InService, ApptStatus.Confirmed, ApptStatus.Requested, ApptStatus.CheckedIn };
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
        var appts = new List<Appointment>();
        for (int dayOff = -4; dayOff <= 10; dayOff++)
        {
            int perDay = dayOff == 0 ? 8 : rnd.Next(2, 6);
            for (int k = 0; k < perDay; k++)
            {
                var svc = services[rnd.Next(services.Count)];
                var bay = bays[rnd.Next(bays.Count)];
                var tech = techs[rnd.Next(techs.Count)];
                var cust = customers[rnd.Next(customers.Count)];
                var veh = vehicles[rnd.Next(vehicles.Count)];
                var local = DateTime.Today.AddDays(dayOff).AddHours(9 + rnd.Next(8)).AddMinutes(rnd.Next(0, 2) * 30);
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
                var status = dayOff < 0 ? ApptStatus.PickedUp : dayOff == 0
                    ? statuses[rnd.Next(statuses.Length)] : ApptStatus.Requested;
                appts.Add(new Appointment
                {
                    ShopId = shop.Id, BayId = bay.Id, TechId = tech.Id, ServiceId = svc.Id,
                    CustomerId = cust.Id, VehicleId = veh.Id,
                    SlotStartUtc = startUtc, SlotEndUtc = startUtc.AddMinutes(svc.DurationMinutes),
                    Status = status, Notes = status == ApptStatus.Requested ? "Online booking" : null,
                });
            }
        }
        db.Appointments.AddRange(appts);
        await db.SaveChangesAsync();
        db.StatusHistory.AddRange(appts.Select(a =>
            new StatusHistory { AppointmentId = a.Id, From = "-", To = a.Status, ChangedBy = "seed" }));
        db.Reminders.AddRange(appts.Where(a => a.SlotStartUtc > DateTime.UtcNow).Take(20).Select(a =>
            new Reminder { AppointmentId = a.Id, ScheduledForUtc = a.SlotStartUtc.AddHours(-24) }));
        await db.SaveChangesAsync();
    }
}
