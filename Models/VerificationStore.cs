namespace SmsQrVerifyDemo.Models
{
    using System.Collections.Concurrent;

    public sealed class VerificationStore
    {
        private readonly ConcurrentDictionary<string, VerificationRecord> _records = new();

        public VerificationRecord? Get(string sessionId)
        {
            _records.TryGetValue(sessionId, out var value);
            return value;
        }

        public void Upsert(VerificationRecord record)
        {
            _records[record.SessionId] = record;
        }
    }
}
