namespace kstech.Utilities
{
    public static class BusinessTime
    {
        private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

        public static DateTime Today => ConvertUtcToBusinessTime(DateTime.UtcNow).Date;

        public static DateTime ConvertUtcToBusinessTime(DateTime utcDateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(EnsureUtcKind(utcDateTime), BusinessTimeZone);
        }

        public static DateTime ConvertBusinessDateStartToUtc(DateTime localDate)
        {
            var localUnspecified = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(localUnspecified, BusinessTimeZone);
        }

        public static DateTime ConvertBusinessDateEndToUtc(DateTime localDate)
        {
            var localUnspecifiedEnd = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified)
                .AddDays(1)
                .AddTicks(-1);

            return TimeZoneInfo.ConvertTimeToUtc(localUnspecifiedEnd, BusinessTimeZone);
        }

        public static DateTime ConvertBusinessDateTimeToUtc(DateTime localDateTime)
        {
            var localUnspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(localUnspecified, BusinessTimeZone);
        }

        public static DateTime EnsureUtcKind(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static TimeZoneInfo ResolveBusinessTimeZone()
        {
            var candidateIds = new[]
            {
                "Singapore Standard Time", // Windows (PH-compatible)
                "Asia/Manila" // IANA fallback
            };

            foreach (var id in candidateIds)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                    continue;
                }
                catch (InvalidTimeZoneException)
                {
                    continue;
                }
            }

            return TimeZoneInfo.Utc;
        }
    }
}
