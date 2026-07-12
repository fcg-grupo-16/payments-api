using System;
using System.Threading.Tasks;

namespace Fcg.Payments.Api.Services
{
    public class PixPaymentService : IPaymentService
    {
        public async Task<PaymentResult> ProcessPixAsync(string orderId, decimal amount)
        {
            try
            {
                // Estrutura pronta para receber a futura integração com a Efí (Gerencianet)
                string pixCopiaEColaSimulado = $"00020126360014br.gov.bcb.pix0114suachavepix2523coloca_o_valor_aqui_id_{orderId}";

                return await Task.FromResult(new PaymentResult
                {
                    Success = true,
                    Message = "Cobrança Pix gerada com sucesso na Efí.",
                    TransactionId = Guid.NewGuid().ToString(),
                    QrCode = pixCopiaEColaSimulado
                });
            }
            catch (Exception ex)
            {
                return await Task.FromResult(new PaymentResult
                {
                    Success = false,
                    Message = $"Erro ao gerar Pix: {ex.Message}"
                });
            }
        }
    }
}