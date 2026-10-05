namespace MyToDoWidget.Services;

internal static class BuildIdentity
{
#if MYTODO_TEST_BUILD
    public const bool IsTest = true;
    public const string StateFolder = "MyToDoWidget.Test";
    public const string InstancePrefix = @"Local\MyToDoWidget.Test";
    public const string DisplayName = "MyToDo (테스트)";
#else
    public const bool IsTest = false;
    public const string StateFolder = "MyToDoWidget";
    public const string InstancePrefix = @"Local\MyToDoWidget.0.1";
    public const string DisplayName = "MyToDo";
#endif
}
