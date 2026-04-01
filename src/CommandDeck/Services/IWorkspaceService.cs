namespace CommandDeck.Services;

/// <summary>
/// Composite interface that combines canvas item management (<see cref="ICanvasItemsService"/>)
/// with workspace lifecycle operations (<see cref="IWorkspaceLifecycleService"/>).
/// Maintained for backwards compatibility — all existing consumers continue to resolve
/// <see cref="IWorkspaceService"/> unchanged while the two concerns are now separable.
/// </summary>
public interface IWorkspaceService : ICanvasItemsService, IWorkspaceLifecycleService
{
}
