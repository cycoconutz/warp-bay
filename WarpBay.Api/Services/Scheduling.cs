using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WarpBay.Api.Data;
using WarpBay.Api.Models;

namespace WarpBay.Api.Services;

public record SlotDto(DateTime StartUtc, DateTime EndUtc, Guid BayId, string BayName, Guid? TechId, string? TechName);

public class SchedulingService
{
    private readonly WarpBayDb _db;
    private readonly IMemoryCache _cache;
    public SchedulingService(WarpBayDb db, IMemoryCache cache) { _db = db; _cache = cache; }

    // Day grid in shop-local time, 30-min starts, 8:00-17:30. Excludes overlapping active appointments.
    public async Task<List<SlotDto>> AvailabilityAsync(Guid serviceId, DateOnly day, CancellationToken ct)
    {
        var key = $"avail:{serviceId}:{day:yyyy-MM-dd}";
        if (_cache.TryGetValue(key, out List<SlotDto>? cached) && cached is not null) return cached;

        var svc = await _db.Services.FirstOrDefaultAsync(s => s.Id == serviceId, ct)
            ?? throw new InvalidOperationException("unknown service");
        var shop = await _db.Shops.FirstAsync(ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(NormalizeTz(shop.TimeZone));
        var bays = await _db.Bays.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(ct);
        var techs = await _db.Techs.Where(t => t.IsActive).OrderBy(t => t.DisplayName).ToListAsync(ct);

        var dayStartLocal = new DateTime(day.Year, day.Month, day.Day, shop.OpenHour, 0, 0, DateTimeKind.Unspecified);
        var dayEndLocal = new DateTime(day.Year, day.Month, day.Day, shop.CloseHour, 0, 0, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, tz);

        var busy = await _db.Appointments
            .Where(a => a.SlotStartUtc < endUtc && a.SlotEndUtc > startUtc
                && a.Status != ApptStatus.Cancelled && a.Status != ApptStatus.NoShow && a.Status != ApptStatus.PickedUp)
            .ToListAsync(ct);

        var slots = new List<SlotDto>();
        for (var t = startUtc; t.AddMinutes(svc.DurationMinutes) <= endUtc; t = t.AddMinutes(30))
        {
            var sEnd = t.AddMinutes(svc.DurationMinutes);
            foreach (var bay in bays)
            {
                if (busy.Any(a => a.BayId == bay.Id && a.SlotStartUtc < sEnd && a.SlotEndUtc > t)) continue;
                // pick first free tech (or none) — tech overlap also guarded
                var tech = techs.FirstOrDefault(th =>
                    !busy.Any(a => a.TechId == th.Id && a.SlotStartUtc < sEnd && a.SlotEndUtc > t));
                slots.Add(new SlotDto(t, sEnd, bay.Id, bay.Name, tech?.Id, tech?.DisplayName));
                break; // one row per start time keeps UI clean; bay/tech auto-assigned, re-pickable at confirm
            }
        }
        _cache.Set(key, slots, TimeSpan.FromSeconds(60));
        return slots;
    }

    // Transactional booking with overlap guard -> 409 on conflict. DEMO-409 plate forces a conflict demo.
    public async Task<(Appointment? Appt, string? Error)> BookAsync(BookRequest req, string? idempotencyKey, string bookedBy, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.Plate) && req.Plate.Trim().ToUpperInvariant() == "DEMO-409")
            return (null, "Bay already booked for that slot (demo conflict). Pick another time.");

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.Appointments.FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null) return (existing, null);
        }

        var svc = await _db.Services.FirstOrDefaultAsync(s => s.Id == req.ServiceId, ct);
        if (svc is null || !svc.IsActive) return (null, "Unknown service.");
        var end = req.SlotStartUtc.AddMinutes(svc.DurationMinutes);

        // Transactions on relational providers only (InMemory test store ignores them).
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational())
            tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
        var clash = await _db.Appointments.AnyAsync(a =>
            a.BayId == req.BayId && a.Status != ApptStatus.Cancelled && a.Status != ApptStatus.NoShow &&
            a.SlotStartUtc < end && a.SlotEndUtc > req.SlotStartUtc, ct);
        if (clash) return (null, "Bay already booked for that slot. Pick another time.");
        if (req.TechId.HasValue)
        {
            var techClash = await _db.Appointments.AnyAsync(a =>
                a.TechId == req.TechId && a.Status != ApptStatus.Cancelled && a.Status != ApptStatus.NoShow &&
                a.SlotStartUtc < end && a.SlotEndUtc > req.SlotStartUtc, ct);
            if (techClash) return (null, "Tech already booked for that slot. Pick another time.");
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == req.Phone, ct);
        if (customer is null)
        {
            customer = new Customer { FullName = req.FullName, Phone = req.Phone, Email = req.Email };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);
        }
        Vehicle? vehicle = null;
        if (!string.IsNullOrWhiteSpace(req.Plate))
        {
            vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Plate == req.Plate!.Trim().ToUpperInvariant(), ct);
            if (vehicle is null)
            {
                vehicle = new Vehicle { CustomerId = customer.Id, Plate = req.Plate.Trim().ToUpperInvariant(), Make = req.Make, Model = req.Model, Year = req.Year };
                _db.Vehicles.Add(vehicle);
                await _db.SaveChangesAsync(ct);
            }
        }

        var shopId = (await _db.Shops.FirstAsync(ct)).Id;
        var appt = new Appointment
        {
            ShopId = shopId, BayId = req.BayId, TechId = req.TechId, ServiceId = svc.Id,
            CustomerId = customer.Id, VehicleId = vehicle?.Id,
            SlotStartUtc = req.SlotStartUtc, SlotEndUtc = end,
            Status = ApptStatus.Requested, Notes = req.Notes, IdempotencyKey = idempotencyKey,
        };
        _db.Appointments.Add(appt);
        _db.StatusHistory.Add(new StatusHistory { AppointmentId = appt.Id, From = "-", To = appt.Status, ChangedBy = bookedBy });
        _db.Reminders.Add(new Reminder { AppointmentId = appt.Id, Type = "Confirm24h", ScheduledForUtc = appt.SlotStartUtc.AddHours(-24) });
        await _db.SaveChangesAsync(ct);
        if (tx is not null) { await tx.CommitAsync(ct); await tx.DisposeAsync(); }
        return (appt, null);
        }
        finally { if (tx is not null) await tx.DisposeAsync(); }
    }

    private static string NormalizeTz(string tz) =>
        OperatingSystem.IsWindows() && tz == "America/Los_Angeles" ? "Pacific Standard Time" : tz;
}

public record BookRequest(
    Guid ServiceId, Guid BayId, Guid? TechId, DateTime SlotStartUtc,
    string FullName, string Phone, string? Email, string? Plate,
    string? Year, string? Make, string? Model, string? Notes);
