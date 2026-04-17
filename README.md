# SmsQrVerifyDemo

Demo xác minh số điện thoại theo hướng "ngược": hệ thống **không gửi OTP ra ngoài**, mà yêu cầu người dùng **gửi một SMS có cấu trúc cố định** về số gateway của hệ thống. Khi SMS quay ngược về webhook, backend đối chiếu `sessionId`, `token` và số điện thoại gửi lên để xác nhận thuê bao thực sự đang sở hữu SIM đó.

Mục tiêu của hướng tiếp cận này:

- Không tốn phí gửi OTP chiều đi cho từng lượt xác minh.
- Giảm nguy cơ bị nhà mạng hoặc SMS provider đánh dấu spam do gửi OTP hàng loạt.
- Tránh trải nghiệm chờ OTP chiều đến bị chậm, thất lạc hoặc lệch tuyến.
- Phù hợp làm PoC cho các bài toán onboarding, xác minh SIM thật, hoặc khởi tạo liên kết số điện thoại với một phiên giao dịch.

## Ý tưởng hoạt động

Luồng xác minh trong demo:

1. Client nhập số điện thoại cần xác minh.
2. Backend tạo một phiên xác minh gồm:
   - `sessionId`
   - `token`
   - `smsBody` theo mẫu `VERIFY {sessionId} {token}`
   - `smsUri` dạng `sms:8088&body=...`
3. Frontend hiển thị QR chứa `smsUri`.
4. Người dùng quét QR hoặc mở trực tiếp link SMS để điện thoại tự soạn tin nhắn gửi tới số gateway.
5. SMS gateway thật sẽ gọi webhook `/api/sms/webhook` với `From`, `To`, `Body`.
6. Backend kiểm tra:
   - SMS có gửi đúng số gateway hay không
   - cú pháp SMS có hợp lệ hay không
   - `sessionId` có tồn tại và chưa hết hạn hay không
   - `token` có khớp hay không
   - số điện thoại gửi SMS (`From`) có trùng số đã khai báo ban đầu hay không
7. Nếu hợp lệ, trạng thái phiên chuyển từ `Pending` sang `Verified`.
8. Frontend polling `/api/verification/status/{sessionId}` để cập nhật kết quả.

## Điểm đáng chú ý

- Đây là demo `.NET 8` dạng minimal API.
- Dữ liệu đang lưu **in-memory** bằng `ConcurrentDictionary`, nên restart app sẽ mất session.
- Có sẵn endpoint giả lập SMS để test khi chưa có SMS gateway thật: `/api/sms/simulate`.
- Frontend là trang tĩnh trong `wwwroot/index.html`, đủ để thử end-to-end.

## Cấu trúc chính

- [Program.cs](/D:/META-SkyNet/SmsQrVerifyDemo/Program.cs) chứa toàn bộ API và logic xử lý SMS.
- [wwwroot/index.html](/D:/META-SkyNet/SmsQrVerifyDemo/wwwroot/index.html) là màn hình demo tạo session, hiển thị QR và polling trạng thái.
- [Models/VerificationStore.cs](/D:/META-SkyNet/SmsQrVerifyDemo/Models/VerificationStore.cs) là store in-memory.
- [Models/VerificationRecord.cs](/D:/META-SkyNet/SmsQrVerifyDemo/Models/VerificationRecord.cs) mô tả một phiên xác minh.

## Chạy local

Yêu cầu:

- .NET SDK 8.0

Chạy ứng dụng:

```powershell
dotnet run
```

Sau đó mở trình duyệt tại địa chỉ do ASP.NET Core in ra. Với cấu hình hiện tại, code đang gán `publicBaseUrl = "http://localhost:5000"` để dựng URL trả về cho client.

## Cách test nhanh

### Cách 1: test ngay trên UI

1. Mở trang chủ.
2. Nhập số điện thoại cần xác minh, ví dụ `+84901234567`.
3. Bấm `Tạo phiên xác minh`.
4. Hệ thống sinh:
   - `SessionId`
   - `SMS To`
   - `SMS Body`
   - QR code
5. Bấm `Giả lập SMS đã gửi`.
6. Nếu payload hợp lệ, trạng thái sẽ chuyển sang `Verified`.

### Cách 2: gọi API thủ công

Tạo phiên xác minh:

```http
POST /api/verification/create
Content-Type: application/json

{
  "phoneNumber": "+84901234567"
}
```

Ví dụ response:

```json
{
  "sessionId": "8b1c1f4f3d1142c79d1c10b36d72a123",
  "token": "A1B2C3D4",
  "phoneNumber": "+84901234567",
  "expiresUtc": "2026-04-17T08:20:00Z",
  "smsGatewayNumber": "8088",
  "smsBody": "VERIFY 8b1c1f4f3d1142c79d1c10b36d72a123 A1B2C3D4",
  "smsUri": "sms:8088&body=VERIFY%208b1c1f4f3d1142c79d1c10b36d72a123%20A1B2C3D4",
  "qrText": "sms:8088&body=VERIFY%208b1c1f4f3d1142c79d1c10b36d72a123%20A1B2C3D4",
  "statusUrl": "http://localhost:5000/api/verification/status/8b1c1f4f3d1142c79d1c10b36d72a123",
  "demoSimulateSmsUrl": "http://localhost:5000/api/sms/simulate"
}
```

Giả lập SMS đi vào hệ thống:

```http
POST /api/sms/simulate
Content-Type: application/json

{
  "from": "+84901234567",
  "to": "8088",
  "body": "VERIFY 8b1c1f4f3d1142c79d1c10b36d72a123 A1B2C3D4"
}
```

Kiểm tra trạng thái:

```http
GET /api/verification/status/{sessionId}
```

## Webhook production thật

Khi tích hợp SMS gateway thật, provider cần gọi:

```http
POST /api/sms/webhook
```

Payload có thể là:

- `application/x-www-form-urlencoded`
- hoặc JSON

Các field backend đang đọc:

- `From`
- `To`
- `Body`

Ví dụ JSON:

```json
{
  "from": "+84901234567",
  "to": "8088",
  "body": "VERIFY 8b1c1f4f3d1142c79d1c10b36d72a123 A1B2C3D4"
}
```

## Lợi ích so với OTP truyền thống

- Không phát sinh chi phí SMS OTP outbound trên mỗi lần xác minh.
- Người dùng chủ động gửi SMS nên backend không phải chờ khâu push OTP qua aggregator.
- Giảm áp lực compliance cho các template OTP outbound, brandname, quota gửi và tần suất gửi.
- Hạn chế một số trường hợp bot spam yêu cầu gửi OTP đến hàng loạt số điện thoại.

## Rủi ro và giới hạn

- Người dùng vẫn chịu chi phí một SMS chiều đi từ thiết bị của họ, trừ khi có chính sách bù phí.
- Không phù hợp với mọi tệp người dùng vì phải thao tác gửi SMS thủ công hoặc bán tự động qua `sms:` URI.
- `sms:` URI không đồng nhất tuyệt đối giữa các thiết bị và app SMS.
- Store hiện tại không có persistence, không có cleanup session hết hạn, không có retry hoặc audit log.
- Webhook chưa xác thực chữ ký từ SMS provider.
- Chưa có rate limit, chống replay, idempotency key hay cơ chế khóa session sau nhiều lần sai token.
- Nếu triển khai thật, `publicBaseUrl` phải là domain public thay vì `localhost`.

## Hướng nâng cấp nếu làm thật

- Thay store in-memory bằng Redis hoặc database.
- Thêm job dọn session hết hạn.
- Xác thực request webhook bằng signature hoặc allowlist IP của provider.
- Lưu log inbound SMS để audit và debug.
- Thêm rate limit cho tạo session và nhận SMS.
- Mã hóa hoặc rút gọn payload SMS để tránh lộ quá nhiều thông tin kỹ thuật.
- Tách token theo thời gian sống ngắn hơn và hỗ trợ trạng thái `Expired`, `Failed`, `Blocked`.
- Đồng bộ `publicBaseUrl`, `smsGatewayNumber` vào `appsettings.json`.

## Phù hợp cho mục đích nào

Demo này phù hợp để:

- thử nghiệm UX xác minh số điện thoại bằng QR + SMS
- PoC tích hợp SMS gateway inbound
- kiểm tra tính khả thi trước khi đầu tư luồng OTP chính thức
- giảm phụ thuộc vào outbound OTP trong một số kịch bản đặc thù

Không nên coi đây là giải pháp production hoàn chỉnh nếu chưa bổ sung bảo mật, persistence, monitoring và quy trình vận hành.
