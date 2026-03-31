namespace DevWorkspaceHub.Models;

/// <summary>
/// Supported shell types for terminal sessions.
/// </summary>
public enum ShellType
{
    WSL,
    PowerShell,
    CMD,
    Cmd = CMD,
    GitBash,
    Bash = GitBash,
    Wsl = WSL
}

public static class ShellTypeExtensions
{
    /// <summary>
    /// Gets the executable path for the given shell type.
    /// </summary>
    public static string GetExecutablePath(this ShellType shellType) => shellType switch
    {
        ShellType.WSL => "wsl.exe",
        ShellType.PowerShell => "powershell.exe",
        ShellType.CMD => "cmd.exe",
        ShellType.GitBash => @"C:\Program Files\Git\bin\bash.exe",
        _ => "cmd.exe"
    };

    /// <summary>
    /// Gets command-line arguments for the shell.
    /// </summary>
    public static string GetArguments(this ShellType shellType) => shellType switch
    {
        ShellType.WSL => "--cd ~",
        ShellType.PowerShell => "-NoLogo",
        ShellType.CMD => "/K",
        ShellType.GitBash => "--login -i",
        _ => ""
    };

    /// <summary>
    /// Gets a friendly display name.
    /// </summary>
    public static string GetDisplayName(this ShellType shellType) => shellType switch
    {
        ShellType.WSL => "WSL (Ubuntu)",
        ShellType.PowerShell => "PowerShell",
        ShellType.CMD => "Command Prompt",
        ShellType.GitBash => "Git Bash",
        _ => shellType.ToString()
    };

    /// <summary>
    /// Gets an icon character for the shell type.
    /// </summary>
    public static string GetIconGlyph(this ShellType shellType) => shellType switch
    {
        ShellType.WSL => "\uE756",       // Linux penguin-like
        ShellType.PowerShell => "\uE7A8", // Terminal
        ShellType.CMD => "\uE756",        // Console
        ShellType.GitBash => "\uE74E",    // Git
        _ => "\uE756"
    };
}
