# Chuẩn Hóa TargetType Nghiệp Vụ

Tài liệu này liệt kê các định danh chính thức được phép lưu trữ trong cột `TargetType` của các bảng trong cơ sở dữ liệu (ví dụ: `Notifications`, `AdminAuditLogs`, `Reviews`).

## 1. Bảng `Notifications`

`TargetType` xác định loại đối tượng mà thông báo này nhắc tới. Hệ thống hiện áp dụng **CHECK constraint** (`CK_Notifications_TargetType`) cho các giá trị sau:

| Giá trị | Mô tả | Đối tượng liên kết (`TargetId`) |
|---------|-------|---------------------------------|
| `Room` | Thông báo liên quan đến phòng trọ (xem phòng, phê duyệt, v.v.) | `RoomId` trong bảng `Rooms` |
| `Job` | Thông báo liên quan đến việc làm (ứng tuyển, phản hồi, v.v.) | `JobId` trong bảng `Jobs` |
| `Conversation` | Thông báo có tin nhắn chat mới | `ConversationId` trong bảng `Conversations` |
| `NULL` | Thông báo hệ thống chung (không link tới entity nào) | Không có (`NULL`) |

## 2. Bảng `Reviews`

Bảng đánh giá cũng có `TargetType` để hỗ trợ đa hình (polymorphic reviews). Các giá trị được chấp nhận (qua regex validation của DTO và business logic):

| Giá trị | Mô tả | Đối tượng liên kết (`TargetId`) |
|---------|-------|---------------------------------|
| `room` | Sinh viên đánh giá một phòng trọ | `RoomId` |
| `job` | Sinh viên đánh giá một công việc / nhà tuyển dụng | `JobId` |

*(Lưu ý: Bảng Review đang sử dụng chữ thường `room`, `job` ở cấp API)*

## 3. Mã nguồn App C#
Để đồng bộ, C# App định nghĩa các giá trị này trong class constant: `UniMap360.Constants.ContentTargetTypes`:

```csharp
public static class ContentTargetTypes
{
    public const string Room = "Room";
    public const string Job = "Job";
    public const string Conversation = "Conversation";
}
```
*Hãy sử dụng các hằng số này thay vì hardcode string khi tạo Notifications.*
