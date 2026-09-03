using DataBoard.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace DataBoard.Infrastructure.Services;

internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("DataBoard.DataSource.ConnectionString");

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
