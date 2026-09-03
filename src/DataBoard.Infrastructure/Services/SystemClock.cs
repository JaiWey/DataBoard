using DataBoard.Application.Abstractions;

namespace DataBoard.Infrastructure.Services;

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
