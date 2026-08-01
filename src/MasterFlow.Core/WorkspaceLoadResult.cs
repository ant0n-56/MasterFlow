namespace MasterFlow.Core;

public sealed record WorkspaceLoadResult(MasterWorkspace Workspace, bool RecoveredFromBackup);
