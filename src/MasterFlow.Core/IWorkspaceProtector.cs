namespace MasterFlow.Core;

public interface IWorkspaceProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}
