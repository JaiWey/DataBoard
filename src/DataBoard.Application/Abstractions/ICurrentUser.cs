namespace DataBoard.Application.Abstractions;

public interface ICurrentUser
{
    /// <summary>Null when the request is anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>True for a temporary guest account, which may try the app but not reach outside it.</summary>
    bool IsGuest { get; }
}
