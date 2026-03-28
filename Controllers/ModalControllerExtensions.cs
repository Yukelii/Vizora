using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Vizora.Models;

namespace Vizora.Controllers
{
    internal static class ModalControllerExtensions
    {
        private const string RequestedWithHeader = "X-Requested-With";
        private const string XmlHttpRequest = "XMLHttpRequest";
        private const string ModalStateHeader = "X-Vizora-Modal-State";
        private const string ModalOutcomeHeader = "X-Vizora-Modal-Outcome";

        internal static bool IsModalRequest(this Controller controller)
        {
            return string.Equals(
                controller.Request.Headers[RequestedWithHeader],
                XmlHttpRequest,
                StringComparison.OrdinalIgnoreCase);
        }

        internal static IActionResult ModalSuccess(this Controller controller, string? message = null)
        {
            controller.SetModalState(ModalUiState.Success);
            controller.Response.Headers[ModalOutcomeHeader] = ModalUiState.Success;
            return controller.Json(new ModalSubmitResult
            {
                Status = "success",
                ReloadPage = true,
                Message = message
            });
        }

        internal static IActionResult ModalError(
            this Controller controller,
            string message,
            string size = "sm",
            int statusCode = StatusCodes.Status500InternalServerError,
            string outcome = ModalUiState.Error,
            string state = ModalUiState.Error)
        {
            var normalizedState = ModalUiState.Normalize(state);
            if (normalizedState == ModalUiState.Idle)
            {
                normalizedState = ModalUiState.Error;
            }

            controller.SetModalStateAndStatus(normalizedState, statusCode, outcome);

            return controller.PartialView(
                "~/Views/Shared/_Modal.cshtml",
                new Dictionary<string, object?>
                {
                    ["Size"] = size,
                    ["Variant"] = "form",
                    ["State"] = normalizedState,
                    ["BodyPartial"] = "~/Views/Shared/_ModalError.cshtml",
                    ["BodyModel"] = new ModalErrorViewModel { Message = message }
                });
        }

        internal static void SetModalState(this Controller controller, string state)
        {
            var normalizedState = ModalUiState.Normalize(state);
            controller.ViewData["ModalState"] = normalizedState;
            controller.Response.Headers[ModalStateHeader] = normalizedState;
        }

        internal static void SetModalStateAndStatus(
            this Controller controller,
            string state,
            int statusCode)
        {
            controller.SetModalStateAndStatus(state, statusCode, state);
        }

        internal static void SetModalStateAndStatus(
            this Controller controller,
            string state,
            int statusCode,
            string outcome)
        {
            controller.SetModalState(state);
            controller.Response.Headers[ModalOutcomeHeader] = string.IsNullOrWhiteSpace(outcome)
                ? ModalUiState.Normalize(state)
                : outcome.Trim().ToLowerInvariant();
            if (controller.IsModalRequest())
            {
                controller.Response.StatusCode = statusCode;
            }
        }

        internal static void SetModalConflictBanner(
            this Controller controller,
            string message,
            string reloadUrl,
            string reloadModalTitle,
            IEnumerable<ConcurrencyFieldComparisonViewModel>? comparisons = null,
            bool allowOverwrite = false)
        {
            controller.ViewData["ModalConflictBanner"] = new ModalConflictBannerViewModel
            {
                Message = message,
                ReloadUrl = reloadUrl,
                ReloadModalTitle = reloadModalTitle,
                FieldComparisons = comparisons?.ToList() ?? new List<ConcurrencyFieldComparisonViewModel>(),
                AllowOverwrite = allowOverwrite
            };
        }

        internal static void ClearModalConflictBanner(this Controller controller)
        {
            controller.ViewData.Remove("ModalConflictBanner");
        }

        internal static string GetCurrentUserId(this Controller controller)
        {
            return controller.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? controller.User.Identity?.Name
                ?? "unknown";
        }

        internal static void LogModalLifecycle<TController>(
            this Controller controller,
            ILogger<TController> logger,
            string entityType,
            int? entityId,
            string stage)
        {
            if (!controller.IsModalRequest())
            {
                return;
            }

            logger.LogInformation(
                "Modal lifecycle event. Stage={Stage}, EntityType={EntityType}, EntityId={EntityId}, UserId={UserId}",
                stage,
                entityType,
                entityId,
                controller.GetCurrentUserId());
        }

        internal static void LogModalFailure<TController>(
            this Controller controller,
            ILogger<TController> logger,
            string entityType,
            int? entityId,
            string failureType,
            Exception? exception = null,
            bool renderedInModalResponse = false)
        {
            if (!controller.IsModalRequest())
            {
                return;
            }

            var userId = controller.GetCurrentUserId();
            if (exception == null)
            {
                logger.LogWarning(
                    "Modal operation failed. EntityType={EntityType}, EntityId={EntityId}, UserId={UserId}, FailureType={FailureType}, RenderedInModalResponse={RenderedInModalResponse}",
                    entityType,
                    entityId,
                    userId,
                    failureType,
                    renderedInModalResponse);
                return;
            }

            logger.LogError(
                exception,
                "Modal operation failed unexpectedly. EntityType={EntityType}, EntityId={EntityId}, UserId={UserId}, FailureType={FailureType}, RenderedInModalResponse={RenderedInModalResponse}",
                entityType,
                entityId,
                userId,
                failureType,
                renderedInModalResponse);
        }
    }
}
