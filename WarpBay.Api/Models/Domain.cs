namespace WarpBay.Api.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Tech = "Tech";
    public const string Customer = "Customer";
}

public static class ApptStatus
{
    public const string Requested = "Requested";
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = "CheckedIn";
    public const string InService = "InService";
    public const string Ready = "Ready";
    public const string PickedUp = "PickedUp";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";

    public static readonly string[] All =
        [Requested, Confirmed, CheckedIn, InService, Ready, PickedUp, Cancelled, NoShow];

    // role-gated transitions
    public static bool CanTransition(string from, string to, string role) => (from, to, role) switch
    {
        (_, Cancelled, _) when role is Roles.Admin or Roles.Manager or Roles.Customer => true,
        (Requested, Confirmed, _) when role is Roles.Admin or Roles.Manager => true,
        (Confirmed, CheckedIn, _) when role is Roles.Admin or Roles.Manager or Roles.Tech => true,
        (CheckedIn, InService, _) when role is Roles.Admin or Roles.Manager or Roles.Tech => true,
        (InService, Ready, _) when role is Roles.Admin or Roles.Manager or Roles.Tech => true,
        (Ready, PickedUp, _) when role is Roles.Admin or Roles.Manager => true,
        (_, NoShow, _) when role is Roles.Admin or Roles.Manager => true,
        _ => false
    };
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Customer;
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Shop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Warp Bay Auto Lab";
    public string TimeZone { get; set; } = "America/Los_Angeles";
    public int OpenHour { get; set; } = 8;
    public int CloseHour { get; set; } = 18;
}

public class Bay
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class TechProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Color { get; set; } = "#22d3ee";
    public bool IsActive { get; set; } = true;
}

public class ServiceOffering
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int DurationMinutes { get; set; } = 60;
    public int PriceCents { get; set; } = 9900;
    public bool IsActive { get; set; } = true;
}

public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
}

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string Plate { get; set; } = "";
    public string? Year { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
}

public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid BayId { get; set; }
    public Guid? TechId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? VehicleId { get; set; }
    public DateTime SlotStartUtc { get; set; }
    public DateTime SlotEndUtc { get; set; }
    public string Status { get; set; } = ApptStatus.Requested;
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Concurrency token: RowVersion as byte[] works on SqlServer; on Sqlite/Npgsql EF emulates via concurrency check.
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }
}

public class StatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppointmentId { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppointmentId { get; set; }
    public string Type { get; set; } = "Confirm24h";
    public DateTime ScheduledForUtc { get; set; }
    public DateTime? SentAt { get; set; }
    public string Status { get; set; } = "Queued";
}
