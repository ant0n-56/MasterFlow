using System.Security.Cryptography;
using MasterFlow.Core;

namespace MasterFlow.App;

public sealed class WindowsWorkspaceProtector : IWorkspaceProtector
{
    private static readonly byte[] OptionalEntropy = "MasterFlow.Workspace.v1"u8.ToArray();

    public byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, OptionalEntropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, OptionalEntropy, DataProtectionScope.CurrentUser);
}
