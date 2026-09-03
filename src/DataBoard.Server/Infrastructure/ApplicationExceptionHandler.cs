using DataBoard.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DataBoard.Server.Infrastructure;

/// <summary>Turns application exceptions into consistent RFC 9457 problem responses. Unknown exceptions stay 500.</summary>
public sealed class ApplicationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
	{
		ProblemDetails? details = exception switch
		{
			ValidationException v => new ValidationProblemDetails(v.Errors.ToDictionary(e => e.Key, e => e.Value))
			{
				Status = StatusCodes.Status400BadRequest,
				Title = "Validation failed",
			},
			UnauthorizedException u => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = u.Message },
			ForbiddenException f => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden", Detail = f.Message },
			NotFoundException n => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = n.Message },
			ConflictException c => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = c.Message },
			_ => null,
		};

		if (details is null) return false;

		context.Response.StatusCode = details.Status!.Value;
		return await problems.TryWriteAsync(new ProblemDetailsContext
		{
			HttpContext = context,
			ProblemDetails = details,
			Exception = exception,
		});
	}
}
