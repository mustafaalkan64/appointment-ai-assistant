using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Enums;
using AppointmentBooking.Domain.Exceptions;
using AppointmentBooking.Domain.ValueObjects;

namespace AppointmentBooking.Domain.Entities;

public sealed class Organization : AggregateRoot<Guid>, IAuditableEntity, ISoftDeletable
{
    private readonly List<OrganizationUser> _users = [];
    private readonly List<WeeklyAvailability> _weeklyAvailabilities = [];
    private readonly List<SpecialAvailability> _specialAvailabilities = [];

    public string Name { get; private set; } = string.Empty;
    public Slug Slug { get; private set; } = null!;
    public Email Email { get; private set; } = null!;
    public PhoneNumber PhoneNumber { get; private set; } = null!;
    public string Address { get; private set; } = string.Empty;
    public TimeZoneId TimeZone { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public IReadOnlyCollection<OrganizationUser> Users => _users.AsReadOnly();
    public IReadOnlyCollection<WeeklyAvailability> WeeklyAvailabilities => _weeklyAvailabilities.AsReadOnly();
    public IReadOnlyCollection<SpecialAvailability> SpecialAvailabilities => _specialAvailabilities.AsReadOnly();

    private Organization() { }

    public static Organization Create(
        string name,
        string slug,
        string email,
        string phoneNumber,
        string address,
        string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Organization name cannot be empty.");

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = Slug.Create(slug),
            Email = Email.Create(email),
            PhoneNumber = PhoneNumber.Create(phoneNumber),
            Address = address?.Trim() ?? string.Empty,
            TimeZone = TimeZoneId.Create(timeZoneId ?? "Europe/Istanbul"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return org;
    }

    public void UpdateDetails(string name, string address, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Organization name cannot be empty.");

        Name = name.Trim();
        Address = address?.Trim() ?? string.Empty;
        TimeZone = TimeZoneId.Create(timeZoneId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateContactInfo(string email, string phoneNumber)
    {
        Email = Email.Create(email);
        PhoneNumber = PhoneNumber.Create(phoneNumber);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public OrganizationUser AddUser(string email, string passwordHash, string fullName, OrganizationRole role)
    {
        var validEmail = Email.Create(email);
        if (_users.Any(u => u.Email == validEmail.Value && !u.IsDeleted))
            throw new DomainValidationException($"User with email '{email}' already exists in this organization.");

        var user = OrganizationUser.Create(Id, validEmail.Value, passwordHash, fullName, role);
        _users.Add(user);
        UpdatedAt = DateTimeOffset.UtcNow;
        return user;
    }

    public void SetWeeklySchedule(DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan endTime, int slotDurationMinutes)
    {
        if (endTime <= startTime)
            throw new DomainValidationException($"End time ({endTime}) must be greater than start time ({startTime}).");

        if (slotDurationMinutes < 5 || slotDurationMinutes > 240)
            throw new DomainValidationException("Slot duration must be between 5 and 240 minutes.");

        var existing = _weeklyAvailabilities.FirstOrDefault(w => w.DayOfWeek == dayOfWeek);
        if (existing != null)
        {
            existing.Update(startTime, endTime, slotDurationMinutes);
        }
        else
        {
            _weeklyAvailabilities.Add(WeeklyAvailability.Create(Id, dayOfWeek, startTime, endTime, slotDurationMinutes));
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CloseDayOfWeek(DayOfWeek dayOfWeek)
    {
        var existing = _weeklyAvailabilities.FirstOrDefault(w => w.DayOfWeek == dayOfWeek);
        if (existing != null)
        {
            existing.Deactivate();
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public SpecialAvailability AddSpecialAvailability(DateOnly date, bool isClosed, string? reason, TimeSpan? startTime = null, TimeSpan? endTime = null, int? slotDurationMinutes = null)
    {
        var existing = _specialAvailabilities.FirstOrDefault(s => s.Date == date);
        if (existing != null)
        {
            _specialAvailabilities.Remove(existing);
        }

        var special = SpecialAvailability.Create(Id, date, isClosed, reason, startTime, endTime, slotDurationMinutes);
        _specialAvailabilities.Add(special);
        UpdatedAt = DateTimeOffset.UtcNow;
        return special;
    }
}

public sealed class OrganizationUser : Entity<Guid>, IMustHaveTenant, IAuditableEntity, ISoftDeletable
{
    public Guid OrganizationId { get; set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public OrganizationRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    private OrganizationUser() { }

    internal static OrganizationUser Create(Guid organizationId, string email, string passwordHash, string fullName, OrganizationRole role)
    {
        return new OrganizationUser
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string fullName, OrganizationRole role)
    {
        FullName = fullName.Trim();
        Role = role;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class WeeklyAvailability : Entity<Guid>, IMustHaveTenant
{
    public Guid OrganizationId { get; set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public int SlotDurationMinutes { get; private set; }
    public bool IsActive { get; private set; }

    private WeeklyAvailability() { }

    public static WeeklyAvailability Create(Guid organizationId, DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan endTime, int slotDurationMinutes)
    {
        return new WeeklyAvailability
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            SlotDurationMinutes = slotDurationMinutes,
            IsActive = true
        };
    }

    public void Update(TimeSpan startTime, TimeSpan endTime, int slotDurationMinutes)
    {
        StartTime = startTime;
        EndTime = endTime;
        SlotDurationMinutes = slotDurationMinutes;
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}

public sealed class SpecialAvailability : Entity<Guid>, IMustHaveTenant
{
    public Guid OrganizationId { get; set; }
    public DateOnly Date { get; private set; }
    public bool IsClosed { get; private set; }
    public string? Reason { get; private set; }
    public TimeSpan? StartTime { get; private set; }
    public TimeSpan? EndTime { get; private set; }
    public int? SlotDurationMinutes { get; private set; }

    private SpecialAvailability() { }

    internal static SpecialAvailability Create(
        Guid organizationId,
        DateOnly date,
        bool isClosed,
        string? reason,
        TimeSpan? startTime,
        TimeSpan? endTime,
        int? slotDurationMinutes)
    {
        if (!isClosed && (startTime == null || endTime == null || endTime <= startTime))
            throw new DomainValidationException("Open special day must specify a valid start and end time interval.");

        return new SpecialAvailability
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Date = date,
            IsClosed = isClosed,
            Reason = reason?.Trim(),
            StartTime = startTime,
            EndTime = endTime,
            SlotDurationMinutes = slotDurationMinutes
        };
    }
}
