using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarpBay.Api.Data;
using WarpBay.Api.Hubs;
using WarpBay.Api.Models;
using WarpBay.Api.Services;

namespace WarpBay.Api.Endpoints;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/services", async (WarpBayDb db) =>
            await db.Services.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync()).WithOpenApi();

        app.MapGet("/api/bays", async (WarpBayDb db) =>
            await db.Bays.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync()).WithOpenApi();

        app.MapGet("/api/techs", async (WarpBayDb db) =>
            await db.Techs.Where(t => t.IsActive).OrderBy(t => t.DisplayName).ToListAsync()).WithOpenApi();

        app.MapGet("/api/availability", async (Guid serviceId, string date, SchedulingService svc, CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(date, out var day)) return Results.BadRequest("date must be yyyy-MM-dd");
            try { return Results.Ok(await svc.AvailabilityAsync(serviceId, day, ct)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
        }).WithOpenApi();

        app.MapPost("/api/appointments", async (BookRequest req, HttpContext ctx, SchedulingService svc, IHubContext<ScheduleHub> hub, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Phone))
                return Results.BadRequest("FullName and Phone are required.");
            if (req.SlotStartUtc < DateTime.UtcNow.AddMinutes(-5))
                return Results.BadRequest("Slot is in the past.");
            var idem = ctx.Request.Headers.TryGetValue("Idempotency-Key", out var k) ? (string?)k.ToString() : null;
            var by = ctx.User.Identity?.IsAuthenticated is true
                ? ctx.User.FindFirstValue(ClaimTypes.Email) ?? "web" : "public-web";
            var (appt, err) = await svc.BookAsync(req, idem, by, ct);
            if (err is not null)
                return Results.Conflict(new { error = err, hint = "Try plate DEMO-409 to see this 409 path on purpose." });
            await hub.Clients.All.SendAsync("schedule-changed", new { day = appt!.SlotStartUtc.ToString("yyyy-MM-dd") }, ct);
            return Results.Created($"/api/appointments/{appt!.Id}", appt);
        }).WithOpenApi();

        app.MapGet("/api/appointments", async (string? day, Guid? techId, string? status, string? q, int page, int pageSize, WarpBayDb db) =>
        {
            page = Math.Clamp(page is 0 ? 1 : page, 1, 50);
            pageSize = Math.Clamp(pageSize is 0 ? 20 : pageSize, 1, 100);
            var query = db.Appointments.AsQueryable();
            if (DateOnly.TryParse(day, out var d))
            {
                var start = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var end = d.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                query = query.Where(a => a.SlotStartUtc >= start && a.SlotStartUtc <= end);
            }
            if (techId.HasValue) query = query.Where(a => a.TechId == techId);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(a => (a.Notes ?? "").Contains(q));
            var total = await query.CountAsync();
            var items = await query.OrderBy(a => a.SlotStartUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new { total, page, pageSize, items });
        }).RequireAuthorization().WithOpenApi();

        app.MapPatch("/api/appointments/{id:guid}/status", async (Guid id, StatusChange req, ClaimsPrincipal me, WarpBayDb db, IHubContext<ScheduleHub> hub, CancellationToken ct) =>
        {
            var role = me.FindFirstValue(ClaimTypes.Role) ?? "";
            var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (appt is null) return Results.NotFound();
            if (!ApptStatus.All.Contains(req.To)) return Results.BadRequest("Unknown status.");
            if (!ApptStatus.CanTransition(appt.Status, req.To, role))
                return Results.StatusCode(403);
            try
            {
                var from = appt.Status;
                appt.Status = req.To;
                db.StatusHistory.Add(new StatusHistory
                {
                    AppointmentId = id, From = from, To = req.To,
                    ChangedBy = me.FindFirstValue(ClaimTypes.Email) ?? role
                });
                await db.SaveChangesAsync(ct);
                await hub.Clients.All.SendAsync("schedule-changed", new { day = appt.SlotStartUtc.ToString("yyyy-MM-dd") }, ct);
                return Results.Ok(appt);
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Updated by someone else. Reload and retry." }); }
        }).RequireAuthorization().WithOpenApi();

        app.MapGet("/api/admin/reports/day", async (string date, WarpBayDb db) =>
        {
            if (!DateOnly.TryParse(date, out var d)) return Results.BadRequest("date must be yyyy-MM-dd");
            var start = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = d.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var appts = await db.Appointments.Where(a => a.SlotStartUtc >= start && a.SlotStartUtc <= end).ToListAsync();
            var serviceIds = appts.Select(a => a.ServiceId).Distinct().ToList();
            var prices = await db.Services.Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.PriceCents);
            var revenue = appts.Where(a => a.Status != ApptStatus.Cancelled && a.Status != ApptStatus.NoShow)
                .Sum(a => prices.TryGetValue(a.ServiceId, out var p) ? p : 0);
            var bays = await db.Bays.CountAsync(b => b.IsActive);
            return Results.Ok(new
            {
                date,
                total = appts.Count,
                byStatus = appts.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.Count()),
                revenueCents = revenue,
                utilizationPct = bays is 0 ? 0 : Math.Round(100.0 * appts.Count / Math.Max(1, bays * 10), 1),
            });
        }).RequireAuthorization(Roles.Admin + "," + Roles.Manager).WithOpenApi();

        app.MapGet("/api/demo/credentials", (IConfiguration cfg) =>
        {
            if (cfg.GetValue("Demo:Enabled", true) is false) return Results.NotFound();
            return Results.Ok(new[]
            {
                new { role = "Admin", email = "admin@warp-bay.demo", password = "Demo123!" },
                new { role = "Manager", email = "manager@warp-bay.demo", password = "Demo123!" },
                new { role = "Tech", email = "tech@warp-bay.demo", password = "Demo123!" },
                new { role = "Customer", email = "customer@warp-bay.demo", password = "Demo123!" },
            });
        }).WithOpenApi();

        app.MapPost("/api/demo/reset", async (WarpBayDb db, IConfiguration cfg) =>
        {
            if (cfg.GetValue("Demo:Enabled", true) is false) return Results.NotFound();
            await Seed.SeedData.ResetDemoAsync(db);
            return Results.Ok(new { ok = true });
        }).WithOpenApi();
    }
}

public record StatusChange(string To);
