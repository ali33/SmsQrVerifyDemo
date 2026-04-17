namespace SmsQrVerifyDemo.Models
{
    public sealed class SmsWebhookRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Body { get; set; }
    }
}
