#nullable enable
using System;

namespace SCLOCUA
{
    /// <summary>Simple guard helpers to validate arguments.</summary>
    internal static class Guard
    {
        public static T NotNull<T>(T? v, string n) where T : class
            => v ?? throw new ArgumentNullException(n);

        public static string NotNullOrWhiteSpace(string? v, string n)
            => !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"{n} is null/empty.", n);
    }
}
