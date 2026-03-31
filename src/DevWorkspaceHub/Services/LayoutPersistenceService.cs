using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <inheritdoc />
public class LayoutPersistenceService : ILayoutPersistenceService
{
    private static readonly string WorkspacesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "DevWorkspaceHub", "workspaces");

    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task SaveAsync(WorkspaceModel workspace)
    {
        await _lock.WaitAsync();
        try
        {
            Directory.CreateDirectory(WorkspacesDir);
            var path = GetPath(workspace.Id);
            var json = JsonSerializer.Serialize(workspace, JsonOptions);
            await File.WriteAllTextAsync(path, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<WorkspaceModel?> LoadAsync(string workspaceId)
    {
        var path = GetPath(workspaceId);
        if (!File.Exists(path)) return null;

        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<WorkspaceModel>(json, JsonOptions);
        }
        catch
        {
            return null; // corrupted file — treat as missing
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<string>> ListWorkspaceIdsAsync()
    {
        Directory.CreateDirectory(WorkspacesDir);
        var ids = Directory.GetFiles(WorkspacesDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    public async Task DeleteAsync(string workspaceId)
    {
        var path = GetPath(workspaceId);
        if (!File.Exists(path)) return;

        await _lock.WaitAsync();
        try
        {
            File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string GetPath(string workspaceId)
        => Path.Combine(WorkspacesDir, $"{workspaceId}.json");
}
