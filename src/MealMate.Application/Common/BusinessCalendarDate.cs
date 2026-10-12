namespace MealMate.Application.Common;

public static class BusinessCalendarDate
{
    public static DateOnly KuwaitToday(DateTime utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Arab Standard Time" : "Asia/Kuwait");
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}
