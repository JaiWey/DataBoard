namespace DataBoard.Application.Abstractions;

/// <summary>Encrypts secrets such as connection strings before they are stored.</summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}
