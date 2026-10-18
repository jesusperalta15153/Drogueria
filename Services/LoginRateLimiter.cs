using System.Collections.Concurrent;

namespace Drogueria.Services
{
    public interface ILoginRateLimiter
    {
        bool IsBlocked(string ip, string email, out int secondsRemaining);
        void RecordFailedAttempt(string ip, string email);
        void ResetAttempts(string ip, string email);
    }

    public class LoginRateLimiter : ILoginRateLimiter
    {
        private class AttemptRecord
        {
            public int Failures { get; set; }
            public DateTime WindowStart { get; set; }
            public DateTime? BlockedUntil { get; set; }
        }

        private static readonly ConcurrentDictionary<string, AttemptRecord> _records = new();

        private const int MaxFailures = 5;
        private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);

        private static string GetKey(string ip, string email)
        {
            var cleanIp = (ip ?? "unknown").Trim().ToLowerInvariant();
            var cleanEmail = (email ?? "unknown").Trim().ToLowerInvariant();
            return $"{cleanIp}|{cleanEmail}";
        }

        public bool IsBlocked(string ip, string email, out int secondsRemaining)
        {
            secondsRemaining = 0;
            var key = GetKey(ip, email);

            if (_records.TryGetValue(key, out var record))
            {
                lock (record)
                {
                    var now = DateTime.UtcNow;
                    if (record.BlockedUntil.HasValue)
                    {
                        if (record.BlockedUntil.Value > now)
                        {
                            secondsRemaining = (int)Math.Ceiling((record.BlockedUntil.Value - now).TotalSeconds);
                            return true;
                        }

                        // El bloqueo expiró, limpiar el registro
                        record.Failures = 0;
                        record.BlockedUntil = null;
                        record.WindowStart = now;
                    }
                }
            }

            return false;
        }

        public void RecordFailedAttempt(string ip, string email)
        {
            var key = GetKey(ip, email);
            var now = DateTime.UtcNow;

            var record = _records.GetOrAdd(key, _ => new AttemptRecord
            {
                Failures = 0,
                WindowStart = now,
                BlockedUntil = null
            });

            lock (record)
            {
                if (now - record.WindowStart > WindowDuration)
                {
                    record.Failures = 0;
                    record.WindowStart = now;
                }

                record.Failures++;

                if (record.Failures >= MaxFailures)
                {
                    record.BlockedUntil = now.Add(BlockDuration);
                }
            }
        }

        public void ResetAttempts(string ip, string email)
        {
            var key = GetKey(ip, email);
            _records.TryRemove(key, out _);
        }
    }
}
