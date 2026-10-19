using DataBoard.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DataBoard.Server.Infrastructure;

/// <summary>Reads the signed-in user's id from the validated JWT ("sub" claim).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
	public Guid? UserId
	{
		get
		{
			var sub = accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
			return Guid.TryParse(sub, out var id) ? id : null;
		}
	}

	public bool IsGuest => accessor.HttpContext?.User.FindFirst(GuestClaim)?.Value == "true";

	public const string GuestClaim = "guest";
}
