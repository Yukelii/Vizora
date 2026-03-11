using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Vizora.Services
{
    public interface IUserContextService
    {
        string GetRequiredUserId();
    }

    public class HttpContextUserContextService : IUserContextService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextUserContextService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetRequiredUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                throw new UnauthorizedAccessException("An authenticated user is required.");
            }

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         user.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new UnauthorizedAccessException("Authenticated user ID claim is missing.");
            }

            return userId;
        }
    }
}
