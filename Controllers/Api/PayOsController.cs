using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayOS;
using PayOS.Models;
using PayOS.Models.Webhooks;
using PayOS.Models.V2.PaymentRequests;
using Microsoft.AspNetCore.SignalR;
using UniMap360.Hubs;
using UniMap360.Models;
using UniMap360.Services.Business;

namespace UniMap360.Controllers.Api
{
    [ApiController]
    [Route("api/subscriptions")]
    public class PayOsController : ControllerBase
    {
        private readonly UniMap360ProContext _context;
        private readonly ISubscriptionService _subscriptionService;
        private readonly PayOSClient _payOs;
        private readonly IHubContext<RealtimeHub> _hubContext;
        private readonly IBillingSettingsService _billingSettingsService;

        public PayOsController(
            UniMap360ProContext context,
            ISubscriptionService subscriptionService,
            PayOSClient payOs,
            IHubContext<RealtimeHub> hubContext,
            IBillingSettingsService billingSettingsService)
        {
            _context = context;
            _subscriptionService = subscriptionService;
            _payOs = payOs;
            _hubContext = hubContext;
            _billingSettingsService = billingSettingsService;
        }

        /// <summary>
        /// Tạo link thanh toán VietQR qua PayOS
        /// </summary>
        [HttpPost("create-payment-link")]
        [Authorize]
        public async Task<IActionResult> CreatePaymentLink([FromBody] PayOsSubscribeRequest request)
        {
            if (!await _billingSettingsService.IsBillingEnforcedAsync())
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Cổng thanh toán đang tạm đóng. Tính năng VIP hiện đang được mở miễn phí."
                });
            }

            var accountId = GetCurrentAccountId();
            if (!accountId.HasValue) return Unauthorized(new { message = "Không hợp lệ." });

            var account = await _context.Accounts.FindAsync(accountId.Value);
            if (account == null) return NotFound(new { message = "Không tìm thấy tài khoản." });

            var role = account.UserRole;
            if (role != "Host" && role != "Employer")
            {
                return BadRequest(new { message = "Chỉ tài khoản Chủ trọ hoặc Nhà tuyển dụng mới cần đăng ký gói VIP." });
            }

            // Xác định planCode tự động từ vai trò tài khoản (Host -> HostVIP, Employer -> EmployerVIP)
            string planCode = role == "Host" ? "HostVIP" : "EmployerVIP";

            // Seed/Check Subscription Plan
            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == planCode);
            if (plan == null)
            {
                plan = new SubscriptionPlan
                {
                    Code = planCode,
                    Name = role == "Host" ? "Gói VIP Chủ Trọ" : "Gói VIP Nhà Tuyển Dụng",
                    RoleScope = role == "Host" ? "Host" : "Employer",
                    PriceVnd = 36000,
                    BillingCycle = "Monthly",
                    IsActive = true
                };
                _context.SubscriptionPlans.Add(plan);
                await _context.SaveChangesAsync();
            }

            // Sinh mã giao dịch duy nhất cho đơn hàng (OrderCode)
            long orderCode = 0;
            bool inserted = false;
            int retries = 0;
            PaymentOrder? pendingOrder = null;

            while (!inserted && retries < 10)
            {
                // Sinh mã order ngẫu nhiên bảo mật trong khoảng an toàn cho PayOS và MB Bank
                orderCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000000, 99999999);

                var exists = await _context.PaymentOrders.AnyAsync(o => o.OrderCode == orderCode);
                if (!exists)
                {
                    pendingOrder = new PaymentOrder
                    {
                        OrderCode = orderCode,
                        AccountId = accountId.Value,
                        PlanCode = planCode,
                        Amount = 36000,
                        Currency = "VND",
                        Status = "Pending",
                        Provider = "PayOS",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                    };

                    _context.PaymentOrders.Add(pendingOrder);
                    try
                    {
                        await _context.SaveChangesAsync();
                        inserted = true;
                    }
                    catch (DbUpdateException)
                    {
                        _context.Entry(pendingOrder).State = EntityState.Detached;
                        retries++;
                    }
                }
                else
                {
                    retries++;
                }
            }

            if (!inserted || pendingOrder == null)
            {
                return StatusCode(500, new { message = "Không thể khởi tạo mã đơn hàng duy nhất." });
            }

            // Link quay lại web sau khi thanh toán xong hoặc huỷ thanh toán (Xây dựng an toàn từ Host/Scheme của Server)
            string returnUrl = $"{Request.Scheme}://{Request.Host}/Home/Pricing?status=success";
            string cancelUrl = $"{Request.Scheme}://{Request.Host}/Home/Pricing?status=cancelled";

            // Tạo danh sách item thanh toán
            var item = new PaymentLinkItem { Name = plan.Name, Quantity = 1, Price = 36000 };
            var itemsList = new System.Collections.Generic.List<PaymentLinkItem> { item };

            // Tạo PaymentData gửi lên PayOS
            var paymentRequest = new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = 36000,
                Description = $"UM360 VIP {orderCode}",
                Items = itemsList,
                CancelUrl = cancelUrl,
                ReturnUrl = returnUrl
            };

            try
            {
                var createResult = await _payOs.PaymentRequests.CreateAsync(paymentRequest);

                pendingOrder.CheckoutUrl = createResult.CheckoutUrl;
                pendingOrder.QrCode = createResult.QrCode;
                pendingOrder.ProviderPaymentLinkId = createResult.PaymentLinkId;
                pendingOrder.UpdatedAt = DateTime.UtcNow;

                _context.PaymentOrders.Update(pendingOrder);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    checkoutUrl = createResult.CheckoutUrl,
                    orderCode = orderCode,
                    qrCode = createResult.QrCode,
                    bin = createResult.Bin,
                    accountNumber = createResult.AccountNumber,
                    accountName = createResult.AccountName,
                    amount = createResult.Amount,
                    description = createResult.Description
                });
            }
            catch (Exception ex)
            {
                pendingOrder.Status = "Failed";
                pendingOrder.FailureReason = ex.Message;
                pendingOrder.UpdatedAt = DateTime.UtcNow;

                _context.PaymentOrders.Update(pendingOrder);
                await _context.SaveChangesAsync();

                return StatusCode(500, new { message = $"Lỗi tạo cổng thanh toán PayOS: {ex.Message}" });
            }
        }

        /// <summary>
        /// Webhook tiếp nhận tín hiệu thanh toán thành công từ PayOS
        /// </summary>
        [HttpPost("payos-webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> PayOsWebhook([FromBody] Webhook webhookBody)
        {
            if (webhookBody == null) return BadRequest();

            try
            {
                // 1. Xác thực chữ ký số bằng Checksum Key của PayOS
                var verifiedData = await _payOs.Webhooks.VerifyAsync(webhookBody);
                if (verifiedData == null)
                {
                    return BadRequest(new { message = "Chữ ký webhook không hợp lệ." });
                }

                var rawJson = System.Text.Json.JsonSerializer.Serialize(webhookBody);

                // 2. Bắt đầu transaction Serializable để tránh race condition (Tuân thủ luật không dùng Raw SQL)
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                var order = await _context.PaymentOrders
                    .FirstOrDefaultAsync(o => o.OrderCode == verifiedData.OrderCode);

                if (order == null)
                {
                    await transaction.CommitAsync();
                    return NotFound(new { message = "Mã đơn hàng không tồn tại." });
                }

                // Nếu đơn hàng đã Completed từ trước (Tránh xử lý lặp - Webhook Replay)
                if (order.Status == "Completed")
                {
                    await transaction.CommitAsync();
                    return Ok(new { message = "Đơn hàng đã được xử lý thành công trước đó." });
                }

                // Nếu đơn hàng đã Failed từ trước
                if (order.Status == "Failed")
                {
                    await transaction.CommitAsync();
                    return Ok(new { message = "Đơn hàng đã thất bại trước đó." });
                }

                if (order.Status != "Pending")
                {
                    await transaction.CommitAsync();
                    return BadRequest(new { message = $"Trạng thái đơn hàng không hợp lệ: {order.Status}" });
                }

                // Giao dịch không thành công
                if (!webhookBody.Success)
                {
                    order.Status = "Failed";
                    order.RawWebhookJson = rawJson;
                    order.FailureReason = webhookBody.Description ?? "Giao dịch thất bại theo phản hồi Webhook.";
                    order.UpdatedAt = DateTime.UtcNow;

                    _context.PaymentOrders.Update(order);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { message = "Ghi nhận giao dịch thất bại." });
                }

                // Kiểm tra khớp số tiền
                if (verifiedData.Amount != order.Amount)
                {
                    order.Status = "Failed";
                    order.RawWebhookJson = rawJson;
                    order.FailureReason = $"Sai lệch số tiền: Webhook báo {verifiedData.Amount}, DB mong đợi {order.Amount}.";
                    order.UpdatedAt = DateTime.UtcNow;

                    _context.PaymentOrders.Update(order);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return BadRequest(new { message = "Số tiền thanh toán không khớp." });
                }

                // Kích hoạt gói VIP dựa trên AccountId và PlanCode lưu trong DB đơn hàng
                var account = await _context.Accounts.FindAsync(order.AccountId);
                if (account != null)
                {
                    var sub = await _subscriptionService.CreatePaidSubscriptionAsync(order.AccountId, order.PlanCode);

                    order.Status = "Completed";
                    order.ProcessedAt = DateTime.UtcNow;
                    order.RawWebhookJson = rawJson;
                    order.UpdatedAt = DateTime.UtcNow;

                    _context.PaymentOrders.Update(order);
                    
                    try
                    {
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex) when (
                        ex.Message.Contains("40001") || ex.Message.Contains("serialization") ||
                        ex.InnerException?.Message.Contains("40001") == true || ex.InnerException?.Message.Contains("serialization") == true ||
                        ex.InnerException?.InnerException?.Message.Contains("40001") == true || ex.InnerException?.InnerException?.Message.Contains("serialization") == true)
                    {
                        // Concurrency conflict resolved by another concurrent request. Rollback and return Ok.
                        try
                        {
                            await transaction.RollbackAsync();
                        }
                        catch {}

                        // Read status again from DB without tracking to confirm it was completed
                        var checkOrder = await _context.PaymentOrders
                            .AsNoTracking()
                            .FirstOrDefaultAsync(o => o.OrderCode == verifiedData.OrderCode);

                        if (checkOrder != null && checkOrder.Status == "Completed")
                        {
                            return Ok(new { message = "Đơn hàng đã được xử lý thành công trước đó." });
                        }

                        throw;
                    }

                    // Gửi thông báo real-time qua SignalR
                    await _hubContext.Clients.Group($"user:{order.AccountId}").SendAsync("ReceivePaymentSuccess", new { planCode = order.PlanCode });

                    return Ok(new { message = "Thanh toán Webhook được ghi nhận và kích hoạt gói VIP thành công!" });
                }
                else
                {
                    await transaction.CommitAsync();
                    return NotFound(new { message = "Không tìm thấy tài khoản gắn liền với đơn hàng." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayOS Webhook Error]: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private int? GetCurrentAccountId()
        {
            var accountIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? User.FindFirst("id")?.Value;
            return int.TryParse(accountIdClaim, out var accountId) ? accountId : null;
        }
    }

    public class PayOsSubscribeRequest
    {
        public string PlanCode { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
    }
}


