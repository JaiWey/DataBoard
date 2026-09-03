namespace DataBoard.Application.Abstractions;

public interface ICurrentUser
{
    /// <summary>Null when the request is anonymous.</summary>
    Guid? UserId { get; }
}
