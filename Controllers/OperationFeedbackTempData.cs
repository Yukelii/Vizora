using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Vizora.DTOs;

namespace Vizora.Controllers
{
    internal static class OperationFeedbackTempData
    {
        private const string OperationFeedbackKey = "OperationFeedback";

        public static void Set(ITempDataDictionary tempData, OperationResultDto feedback)
        {
            tempData[OperationFeedbackKey] = JsonSerializer.Serialize(feedback);
        }

        public static OperationResultDto? Consume(ITempDataDictionary tempData)
        {
            if (!tempData.TryGetValue(OperationFeedbackKey, out var payload) ||
                payload is not string serialized ||
                string.IsNullOrWhiteSpace(serialized))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<OperationResultDto>(serialized);
            }
            catch (JsonException)
            {
                return new OperationResultDto
                {
                    Status = OperationOutcomeStatus.Failed,
                    UserMessage = "Unable to load operation feedback.",
                    IsDataTrusted = false
                };
            }
        }
    }
}
