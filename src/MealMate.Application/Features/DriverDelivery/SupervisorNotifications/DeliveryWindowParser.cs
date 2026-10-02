using System.Text.RegularExpressions;

namespace MealMate.Application.Features.DriverDelivery.SupervisorNotifications;

/// <summary>
/// Resolves the promised delivery window end for a trip stop from its slot
/// label (e.g. "09:30-10:30 ص") with the trip shift as fallback. Kuwait is
/// UTC+3 year-round with no daylight saving.
/// </summary>
public static partial class DeliveryWindowParser
{
    public const double KuwaitUtcOffsetHours = 3.0;

    [GeneratedRegex(@"(\d{1,2})[:.](\d{2})\s*[-–—]\s*(\d{1,2})[:.](\d{2})")]
    private static partial Regex RangeRegex();

    public static bool TryGetExpectedEndTimeUtc(
        string? slotLabel,
        string? shiftName,
        DateTime utcNow,
        out DateTime expectedEndUtc)
    {
        var kuwaitNow = utcNow.AddHours(KuwaitUtcOffsetHours);
        var localDate = kuwaitNow.Date;

        int? endHour = null;
        int endMinute = 0;
        var isAm = false;
        var isPm = false;

        if (!string.IsNullOrWhiteSpace(slotLabel))
        {
            var match = RangeRegex().Match(slotLabel);
            if (match.Success)
            {
                endHour = int.Parse(match.Groups[3].Value);
                endMinute = int.Parse(match.Groups[4].Value);
                isAm = slotLabel.Contains('ص', StringComparison.Ordinal);
                isPm = slotLabel.Contains('م', StringComparison.Ordinal);
            }
        }

        if (endHour is null)
        {
            if (TryGetShiftWindowEnd(shiftName, out var shiftEnd))
            {
                endHour = shiftEnd.Hour;
                endMinute = shiftEnd.Minute;
            }
            else
            {
                // No promised window is known — cannot truthfully call it late.
                expectedEndUtc = default;
                return false;
            }
        }

        var hour = endHour.Value;
        if (isPm && hour < 12) hour += 12;
        else if (isAm && hour >= 12) hour -= 12;

        var localEnd = new DateTime(localDate.Year, localDate.Month, localDate.Day, hour, endMinute, 0, DateTimeKind.Unspecified);
        expectedEndUtc = DateTime.SpecifyKind(localEnd.AddHours(-KuwaitUtcOffsetHours), DateTimeKind.Utc);
        return true;
    }

    private static bool TryGetShiftWindowEnd(string? shiftName, out DateTime end)
    {
        var shift = shiftName ?? string.Empty;
        if (shift.Contains("مساء", StringComparison.Ordinal))
        {
            end = new DateTime(2000, 1, 1, 20, 0, 0);
            return true;
        }
        if (shift.Contains("ظهير", StringComparison.Ordinal))
        {
            end = new DateTime(2000, 1, 1, 14, 0, 0);
            return true;
        }
        if (shift.Contains("صباح", StringComparison.Ordinal) || shift.Contains("صبح", StringComparison.Ordinal))
        {
            end = new DateTime(2000, 1, 1, 10, 0, 0);
            return true;
        }

        end = default;
        return false;
    }
}
