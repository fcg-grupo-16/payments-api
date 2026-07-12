using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Fcg.Payments.Api.Services;

namespace Fcg.Payments.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Isso se traduz para api/payments
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public PaymentsController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        [HttpPost("pix")] // Isso se traduz para api/payments/pix
        public async Task<IActionResult> CreatePixPayment([FromBody] PixRequestDto request)
        {
            if (request == null || request.Amount <= 0)
            {
                return BadRequest("Dados de pagamento inválidos.");
            }

            var result = await _paymentService.ProcessPixAsync(request.OrderId, request.Amount);

            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(result);
        }
    }

    public class PixRequestDto
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

}