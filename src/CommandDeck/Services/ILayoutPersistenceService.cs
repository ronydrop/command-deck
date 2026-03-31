using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Saves and loads workspace layout (camera + item positions) to/from JSON.
/// Stored at: %APPDATA%\CommandDeck\workspaces\{workspaceId}.json
/// </summary>
public interface ILayoutPersistenceService
{
    /// <summary>Persist the given workspace model to disk.</summary>
    Task SaveAsync(WorkspaceModel workspace);

    /// <summary>Load a workspace by id. Returns null if the file does not exist.</summary>
    Task<WorkspaceModel?> LoadAsync(string workspaceId);

    /// <summary>Lists all workspace JSON files on disk.</summary>
    Task<IReadOnlyList<string>> ListWorkspaceIdsAsync();

    /// <summary>Deletes a workspace JSON file by id.</summary>
    Task DeleteAsync(string workspaceId);
}
