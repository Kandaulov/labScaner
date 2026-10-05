using System.Globalization;
using System.Security.Claims;
using LabScaner.Core.Abstractions;

namespace LabScaner.Web.Security;

/// <summary>Текущий преподаватель — вошедший пользователь с ролью «Преподаватель».</summary>
internal sealed class HttpCurrentTeacher(IHttpContextAccessor accessor) : ICurrentTeacher
{
    public int? TeacherId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true || !user.IsInRole(Roles.Teacher))
            {
                return null;
            }

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        }
    }
}
