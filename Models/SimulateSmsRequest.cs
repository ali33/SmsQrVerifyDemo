namespace SmsQrVerifyDemo.Models
{
    public sealed class SimulateSmsRequest
    {
        public string From { get; set; } = default!;
        public string To { get; set; } = default!;
        public string Body { get; set; } = default!;
    }
}
