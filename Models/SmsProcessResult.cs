namespace SmsQrVerifyDemo.Models
{
    public sealed class SmsProcessResult
    {
        public bool Success { get; private set; }
        public string? SessionId { get; private set; }
        public string? Error { get; private set; }
        public string? Detail { get; private set; }

        public static SmsProcessResult Ok(string sessionId)
            => new() { Success = true, SessionId = sessionId };

        public static SmsProcessResult Fail(string error, string detail)
            => new() { Success = false, Error = error, Detail = detail };
    }
}
