namespace Shared.Kernel.Push;

public sealed record DesktopPushRequest(
    IReadOnlyList<int> AuthUserIds,
    IReadOnlyList<Guid> SubjectIds,
    string Title,
    string Body,
    string Tag,
    string Url);
