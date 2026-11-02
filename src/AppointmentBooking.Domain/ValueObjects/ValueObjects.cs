using System.Text.RegularExpressions;
using AppointmentBooking.Domain.Common;
using AppointmentBooking.Domain.Exceptions;

namespace AppointmentBooking.Domain.ValueObjects;

public sealed class AppointmentNumber : ValueObject
{
    public string Value { get; }

    private AppointmentNumber(string value)
    {
        Value = value;
    }

    public static AppointmentNumber Create(DateTimeOffset date)
    {
        var randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var datePrefix = date.ToString("yyyyMMdd");
        return new AppointmentNumber($"APT-{datePrefix}-{randomSuffix}");
    }

    public static AppointmentNumber FromExisting(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainValidationException("Appointment number cannot be empty.");

        return new AppointmentNumber(value.Trim().ToUpperInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

public sealed class PhoneNumber : ValueObject
{
    private static readonly Regex PhoneRegex = new(@"^\+?[1-9]\d{6,14}$", RegexOptions.Compiled);

    public string Value { get; }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public static PhoneNumber Create(string rawNumber)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            throw new DomainValidationException("Phone number cannot be empty.");

        var clean = Regex.Replace(rawNumber.Trim(), @"[\s\-\(\)]", "");
        if (!PhoneRegex.IsMatch(clean))
            throw new DomainValidationException($"Invalid phone number format: '{rawNumber}'. Must be E.164 compatible (e.g. +905551234567).");

        return new PhoneNumber(clean);
    }

    public static PhoneNumber FromExisting(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

public sealed class Email : ValueObject
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string rawEmail)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
            throw new DomainValidationException("Email cannot be empty.");

        var trimmed = rawEmail.Trim().ToLowerInvariant();
        if (!EmailRegex.IsMatch(trimmed))
            throw new DomainValidationException($"Invalid email format: '{rawEmail}'.");

        return new Email(trimmed);
    }

    public static Email FromExisting(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

public sealed class Slug : ValueObject
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    public string Value { get; }

    private Slug(string value)
    {
        Value = value;
    }

    public static Slug Create(string rawSlug)
    {
        if (string.IsNullOrWhiteSpace(rawSlug))
            throw new DomainValidationException("Slug cannot be empty.");

        var normalized = rawSlug.Trim().ToLowerInvariant();
        if (!SlugRegex.IsMatch(normalized))
            throw new DomainValidationException($"Invalid slug format: '{rawSlug}'. Only lowercase letters, numbers, and hyphens are allowed.");

        return new Slug(normalized);
    }

    public static Slug FromExisting(string value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

public sealed class TimeSlot : ValueObject
{
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }
    public TimeSpan Duration => EndUtc - StartUtc;

    private TimeSlot(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    public static TimeSlot Create(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc <= startUtc)
            throw new DomainValidationException($"End time ({endUtc}) must be strictly after start time ({startUtc}).");

        return new TimeSlot(startUtc.ToUniversalTime(), endUtc.ToUniversalTime());
    }

    public bool Overlaps(TimeSlot other)
    {
        return StartUtc < other.EndUtc && EndUtc > other.StartUtc;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StartUtc;
        yield return EndUtc;
    }

    public override string ToString() => $"{StartUtc:s} - {EndUtc:s} UTC";
}

public sealed class TimeZoneId : ValueObject
{
    public string Value { get; }

    private TimeZoneId(string value)
    {
        Value = value;
    }

    public static TimeZoneId Create(string rawTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(rawTimeZoneId))
            rawTimeZoneId = "Europe/Istanbul";

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(rawTimeZoneId);
            return new TimeZoneId(rawTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (rawTimeZoneId.Equals("Turkey Standard Time", StringComparison.OrdinalIgnoreCase))
                return new TimeZoneId("Europe/Istanbul");

            throw new DomainValidationException($"Timezone '{rawTimeZoneId}' is not recognized by the operating system.");
        }
    }

    public static TimeZoneId FromExisting(string value) => new(value);

    public TimeZoneInfo GetTimeZoneInfo()
    {
        return TimeZoneInfo.FindSystemTimeZoneById(Value);
    }

    public DateTime ToLocal(DateTimeOffset utcDateTime)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime.UtcDateTime, GetTimeZoneInfo());
    }

    public DateTimeOffset ToUtc(DateTime localDateTime)
    {
        var tz = GetTimeZoneInfo();
        // If unspecified kind, assume local in that timezone
        var localUnspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localUnspecified, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
