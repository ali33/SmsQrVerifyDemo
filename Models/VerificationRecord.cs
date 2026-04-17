namespace SmsQrVerifyDemo.Models
{
    public sealed class VerificationRecord
    {
        public string SessionId { get; set; } = default!;
        public string Token { get; set; } = default!;
        public string PhoneNumber { get; set; } = default!;
        public VerificationStatus Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime? VerifiedUtc { get; set; }
    }
}
