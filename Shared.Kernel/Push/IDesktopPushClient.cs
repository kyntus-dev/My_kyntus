namespace Shared.Kernel.Push;

public interface IDesktopPushClient
{
    Task NotifyAsync(DesktopPushRequest request, CancellationToken ct = default);
}
