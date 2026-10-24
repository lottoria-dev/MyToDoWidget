namespace MyToDoWidget.Core;

// Browser work is acknowledged by the user; authenticated connection is verified separately.
public sealed class SetupGuideProgress
{
    public SetupGuideProgress(int completedSteps = 0, bool useExistingCode = false)
    {
        CompletedSteps = Math.Clamp(completedSteps, 0, 3);
        UseExistingCode = useExistingCode;
    }
    public int CompletedSteps { get; private set; }
    public bool UseExistingCode { get; set; }
    public bool SheetPageOpened { get; private set; }
    public bool ScriptCopied { get; private set; }
    public int ActiveStep => CompletedSteps + 1;
    public bool CanCopyScript => CompletedSteps >= 1;
    public bool CanConfirmSheet => SheetPageOpened || CompletedSteps >= 1;
    public bool CanConfirmInstall => CompletedSteps >= 1 && (ScriptCopied || CompletedSteps >= 2);
    public bool CanConfirmDeploy => CompletedSteps >= 2;
    public bool CanEnterConnection => UseExistingCode || CompletedSteps >= 3;
    public bool IsStepEnabled(int step) => step is >= 1 and <= 4 &&
        (step == 4 ? CanEnterConnection : CompletedSteps >= step - 1);
    public void NoteSheetPageOpened() => SheetPageOpened = true;
    public bool NoteScriptCopied()
    {
        if (!CanCopyScript) return false;
        ScriptCopied = true;
        return true;
    }
    public bool ConfirmSheet()
    {
        if (!CanConfirmSheet) return false;
        CompletedSteps = Math.Max(CompletedSteps, 1);
        return true;
    }
    public bool ConfirmInstall()
    {
        if (!CanConfirmInstall) return false;
        CompletedSteps = Math.Max(CompletedSteps, 2);
        return true;
    }
    public bool ConfirmDeploy()
    {
        if (!CanConfirmDeploy) return false;
        CompletedSteps = 3;
        return true;
    }
    public void Reset()
    {
        CompletedSteps = 0;
        SheetPageOpened = ScriptCopied = UseExistingCode = false;
    }
}
