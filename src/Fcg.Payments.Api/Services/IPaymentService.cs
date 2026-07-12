using System.Threading.Tasks;

namespace Fcg.Payments.Api.Services
{
    public interface IPaymentService
    {
        Task<PaymentResult> ProcessPixAsync(string orderId, decimal amount);
    }

    public class PaymentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string QrCode { get; set; } = string.Empty;
    }
}