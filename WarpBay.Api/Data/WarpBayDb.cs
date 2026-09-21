using Microsoft.EntityFrameworkCore;
using WarpBay.Api.Models;

namespace WarpBay.Api.Data;

public class WarpBayDb : DbContext
{
    public WarpBayDb(DbContextOptions<WarpBayDb> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<Bay> Bays => Set<Bay>();
    public DbSet<TechProfile> Techs => Set<TechProfile>();
    public DbSet<ServiceOffering> Services => Set<ServiceOffering>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<StatusHistory> StatusHistory => Set<StatusHistory>();
    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();
        b.Entity<Appointment>().HasIndex(a => a.IdempotencyKey).IsUnique();
        b.Entity<Appointment>().HasIndex(a => new { a.BayId, a.SlotStartUtc });
        b.Entity<Appointment>().HasIndex(a => new { a.TechId, a.SlotStartUtc });
        b.Entity<Vehicle>().HasIndex(v => v.Plate);
        b.Entity<Customer>().HasIndex(c => c.Phone);
    }
}
