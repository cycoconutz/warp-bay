using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WarpBay.Api.Data;
using WarpBay.Api.Models;
using WarpBay.Api.Services;

namespace WarpBay.Tests;

public class StateMachineTests
{
    [Theory]
    [InlineData(ApptStatus.Requested, ApptStatus.Confirmed, Roles.Manager, true)]
    [InlineData(ApptStatus.Requested, ApptStatus.Confirmed, Roles.Customer, false)]
    [InlineData(ApptStatus.InService, ApptStatus.Ready, Roles.Tech, true)]
    [InlineData(ApptStatus.Ready, ApptStatus.PickedUp, Roles.Tech, false)]
    [InlineData(ApptStatus.Ready, ApptStatus.PickedUp, Roles.Manager, true)]
    [InlineData(ApptStatus.Confirmed, ApptStatus.Cancelled, Roles.Customer, true)]
    public void Transitions_respect_roles(string from, string to, string role, bool expected)
        => Assert.Equal(expected, ApptStatus.CanTransition(from, to, role));
}

public class BookingTests
{
    private static WarpBayDb InMemoryDb()
    {
        var opt = new DbContextOptionsBuilder<WarpBayDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new WarpBayDb(opt);
    }

    [Fact]
    public async Task Double_booking_same_bay_returns_conflict()
    {
        using var db = InMemoryDb();
        var shop = new Shop(); db.Shops.Add(shop);
        var bay = new Bay { ShopId = shop.Id, Name = "BAY-01" }; db.Bays.Add(bay);
        var svc = new ServiceOffering { Name = "Warp Inspection", DurationMinutes = 30, PriceCents = 100 }; db.Services.Add(svc);
        await db.SaveChangesAsync();

        var sut = new SchedulingService(db, new MemoryCache(new MemoryCacheOptions()));
        var start = DateTime.UtcNow.AddHours(2);
        var req = new BookRequest(svc.Id, bay.Id, null, start, "Test Driver", "(555) 010-0001", null, "TST-001", null, null, null, null);
        var (first, err1) = await sut.BookAsync(req, "key-1", "test", CancellationToken.None);
        Assert.Null(err1); Assert.NotNull(first);
        var (second, err2) = await sut.BookAsync(req with { Phone = "(555) 010-0002", Plate = "TST-002" }, null, "test", CancellationToken.None);
        Assert.Null(second); Assert.NotNull(err2);
    }

    [Fact]
    public async Task Idempotent_retry_returns_same_appointment()
    {
        using var db = InMemoryDb();
        var shop = new Shop(); db.Shops.Add(shop);
        var bay = new Bay { ShopId = shop.Id, Name = "BAY-02" }; db.Bays.Add(bay);
        var svc = new ServiceOffering { Name = "Flux Rotation", DurationMinutes = 30, PriceCents = 100 }; db.Services.Add(svc);
        await db.SaveChangesAsync();

        var sut = new SchedulingService(db, new MemoryCache(new MemoryCacheOptions()));
        var start = DateTime.UtcNow.AddHours(3);
        var req = new BookRequest(svc.Id, bay.Id, null, start, "Idem Driver", "(555) 010-0003", null, "IDM-001", null, null, null, null);
        var (a, _) = await sut.BookAsync(req, "idem-key", "test", CancellationToken.None);
        var (b, _) = await sut.BookAsync(req, "idem-key", "test", CancellationToken.None);
        Assert.Equal(a!.Id, b!.Id);
    }

    [Fact]
    public async Task Demo_plate_forces_409_path()
    {
        using var db = InMemoryDb();
        db.Shops.Add(new Shop());
        await db.SaveChangesAsync();
        var sut = new SchedulingService(db, new MemoryCache(new MemoryCacheOptions()));
        var req = new BookRequest(Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow.AddHours(1),
            "Demo", "(555) 010-0004", null, "DEMO-409", null, null, null, null);
        var (appt, err) = await sut.BookAsync(req, null, "test", CancellationToken.None);
        Assert.Null(appt); Assert.Contains("demo conflict", err);
    }
}
