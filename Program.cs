using SmsQrVerifyDemo.Models;
using System.Text.Json.Serialization;
namespace SmsQrVerifyDemo
{
    
    public class Program
    {
        public static void Main(string[] args)
        {


            var builder = WebApplication.CreateBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.SerializerOptions.WriteIndented = true;
            });

            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            var store = new VerificationStore();

            // Số SMS gateway mà user sẽ gửi tới.
            // Demo: anh đổi thành số thật của nhà cung cấp SMS nếu có.
            const string smsGatewayNumber = "8088";

            // Base URL public mà SMS gateway có thể gọi webhook vào.
            // Khi chạy local thì tạm thời để localhost chỉ để demo logic.
            // Khi triển khai thật phải là domain public ví dụ:
            // https://verify.meta.vn
            var publicBaseUrl = "http://localhost:5000";

            app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

            /// <summary>
            /// Tạo phiên xác minh mới.
            /// Client sẽ nhận sessionId, token, qrText, smsUri.
            /// </summary>
            app.MapPost("/api/verification/create", (CreateVerificationRequest req) =>
            {
                if (string.IsNullOrWhiteSpace(req.PhoneNumber))
                {
                    return Results.BadRequest(new { error = "phone_number_required" });
                }

                var sessionId = Guid.NewGuid().ToString("N");
                var token = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

                var record = new VerificationRecord
                {
                    SessionId = sessionId,
                    Token = token,
                    PhoneNumber = NormalizePhone(req.PhoneNumber),
                    Status = VerificationStatus.Pending,
                    CreatedUtc = DateTime.UtcNow,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(10)
                };

                store.Upsert(record);

                // Nội dung SMS mà điện thoại sẽ gửi
                // Có thể dùng format đơn giản để SMS gateway dễ parse
                var smsBody = $"VERIFY {sessionId} {token}";

                // sms: URI để iPhone/Android mở app SMS và soạn sẵn nội dung
                // Một số máy dùng ?body=, một số dùng &body= tùy ngữ cảnh
                var smsUri = $"sms:{smsGatewayNumber}&body={Uri.EscapeDataString(smsBody)}";

                // QR code thường chỉ cần chứa sms: URI là đủ
                var qrText = smsUri;

                return Results.Ok(new
                {
                    sessionId,
                    token,
                    phoneNumber = record.PhoneNumber,
                    expiresUtc = record.ExpiresUtc,
                    smsGatewayNumber,
                    smsBody,
                    smsUri,
                    qrText,
                    statusUrl = $"{publicBaseUrl}/api/verification/status/{sessionId}",

                    // endpoint này dùng để demo giả lập SMS nếu chưa có SMS gateway thật
                    demoSimulateSmsUrl = $"{publicBaseUrl}/api/sms/simulate"
                });
            });

            /// <summary>
            /// Kiểm tra trạng thái phiên xác minh.
            /// Frontend sẽ poll endpoint này.
            /// </summary>
            app.MapGet("/api/verification/status/{sessionId}", (string sessionId) =>
            {
                var record = store.Get(sessionId);
                if (record is null)
                {
                    return Results.NotFound(new { error = "session_not_found" });
                }

                return Results.Ok(new
                {
                    sessionId = record.SessionId,
                    phoneNumber = record.PhoneNumber,
                    status = record.Status.ToString(),
                    verifiedUtc = record.VerifiedUtc,
                    expiresUtc = record.ExpiresUtc
                });
            });

            /// <summary>
            /// Webhook nhận SMS từ nhà cung cấp SMS.
            /// Đây là endpoint production thật.
            /// Nhà cung cấp SMS sẽ POST các trường như From, To, Body...
            /// </summary>
            app.MapPost("/api/sms/webhook", async (HttpContext http) =>
            {
                SmsWebhookRequest? req = null;

                if (http.Request.HasFormContentType)
                {
                    var form = await http.Request.ReadFormAsync();
                    req = new SmsWebhookRequest
                    {
                        From = form["From"].ToString(),
                        To = form["To"].ToString(),
                        Body = form["Body"].ToString()
                    };
                }
                else
                {
                    req = await http.Request.ReadFromJsonAsync<SmsWebhookRequest>();
                }

                if (req is null)
                {
                    return Results.BadRequest(new { error = "invalid_payload" });
                }

                var result = ProcessIncomingSms(req.From, req.To, req.Body, store, smsGatewayNumber);
                if (!result.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = result.Error,
                        detail = result.Detail
                    });
                }

                return Results.Ok(new
                {
                    ok = true,
                    sessionId = result.SessionId
                });
            });

            /// <summary>
            /// Endpoint demo để giả lập SMS gateway gọi vào hệ thống.
            /// Dùng khi anh chưa có SMS gateway thật.
            /// </summary>
            app.MapPost("/api/sms/simulate", (SimulateSmsRequest req) =>
            {
                var result = ProcessIncomingSms(req.From, req.To, req.Body, store, smsGatewayNumber);
                if (!result.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = result.Error,
                        detail = result.Detail
                    });
                }

                return Results.Ok(new
                {
                    ok = true,
                    sessionId = result.SessionId
                });
            });

            app.Run();

            static SmsProcessResult ProcessIncomingSms(
                string? from,
                string? to,
                string? body,
                VerificationStore store,
                string smsGatewayNumber)
            {
                if (string.IsNullOrWhiteSpace(from))
                {
                    return SmsProcessResult.Fail("from_required", "Missing sender number");
                }

                if (string.IsNullOrWhiteSpace(to))
                {
                    return SmsProcessResult.Fail("to_required", "Missing destination number");
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    return SmsProcessResult.Fail("body_required", "Missing SMS body");
                }

                var normalizedFrom = NormalizePhone(from);
                var normalizedTo = NormalizePhone(to);

                if (normalizedTo != NormalizePhone(smsGatewayNumber))
                {
                    return SmsProcessResult.Fail("invalid_destination", "SMS was not sent to configured gateway number");
                }

                // Format mong muốn: VERIFY {sessionId} {token}
                var parts = body.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 3 || !parts[0].Equals("VERIFY", StringComparison.OrdinalIgnoreCase))
                {
                    return SmsProcessResult.Fail("invalid_sms_format", "Expected format: VERIFY {sessionId} {token}");
                }

                var sessionId = parts[1];
                var token = parts[2];

                var record = store.Get(sessionId);
                if (record is null)
                {
                    return SmsProcessResult.Fail("session_not_found", $"Session '{sessionId}' not found");
                }

                if (record.ExpiresUtc < DateTime.UtcNow)
                {
                    return SmsProcessResult.Fail("session_expired", "Verification session has expired");
                }

                if (!string.Equals(record.Token, token, StringComparison.OrdinalIgnoreCase))
                {
                    return SmsProcessResult.Fail("token_invalid", "Invalid token");
                }

                if (!string.Equals(record.PhoneNumber, normalizedFrom, StringComparison.OrdinalIgnoreCase))
                {
                    return SmsProcessResult.Fail("phone_mismatch", $"Incoming phone '{normalizedFrom}' does not match expected '{record.PhoneNumber}'");
                }

                record.Status = VerificationStatus.Verified;
                record.VerifiedUtc = DateTime.UtcNow;
                store.Upsert(record);

                return SmsProcessResult.Ok(sessionId);
            }

            static string NormalizePhone(string? phone)
            {
                if (string.IsNullOrWhiteSpace(phone))
                    return string.Empty;

                var chars = phone.Trim().Where(c => char.IsDigit(c) || c == '+').ToArray();
                var normalized = new string(chars);

                if (normalized.StartsWith("00"))
                    normalized = "+" + normalized[2..];

                return normalized;
            }


        }
    }
}
