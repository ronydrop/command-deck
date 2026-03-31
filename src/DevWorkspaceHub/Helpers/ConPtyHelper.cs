using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DevWorkspaceHub.Helpers;

/// <summary>
/// P/Invoke wrappers for Windows Pseudo Console (ConPTY) API.
/// Enables creating real terminal sessions with full VT100/ANSI support.
/// </summary>
public static class ConPtyHelper
{
    // ─── Constants ───────────────────────────────────────────────────────────

    private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const int BUFFER_SIZE = 4096;

    // HRESULT values
    private const int S_OK = 0;

    // ─── Structs ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;

        public COORD(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    // ─── Native Methods ──────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // ─── Result Type ─────────────────────────────────────────────────────────

    /// <summary>
    /// Contains the handles and streams for an active ConPTY session.
    /// </summary>
    public class ConPtySession : IDisposable
    {
        public IntPtr PseudoConsoleHandle { get; init; }
        public PROCESS_INFORMATION ProcessInfo { get; init; }
        public FileStream InputWriter { get; init; } = null!;
        public FileStream OutputReader { get; init; } = null!;
        public IntPtr AttributeList { get; init; }

        private SafeFileHandle? _pipeInputRead;
        private SafeFileHandle? _pipeOutputWrite;

        internal void SetInternalHandles(SafeFileHandle pipeInputRead, SafeFileHandle pipeOutputWrite)
        {
            _pipeInputRead = pipeInputRead;
            _pipeOutputWrite = pipeOutputWrite;
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { InputWriter.Dispose(); } catch { }
            try { OutputReader.Dispose(); } catch { }
            try { _pipeInputRead?.Dispose(); } catch { }
            try { _pipeOutputWrite?.Dispose(); } catch { }

            if (ProcessInfo.hProcess != IntPtr.Zero)
            {
                TerminateProcess(ProcessInfo.hProcess, 0);
                WaitForSingleObject(ProcessInfo.hProcess, 3000);
                CloseHandle(ProcessInfo.hProcess);
            }

            if (ProcessInfo.hThread != IntPtr.Zero)
                CloseHandle(ProcessInfo.hThread);

            if (PseudoConsoleHandle != IntPtr.Zero)
                ClosePseudoConsole(PseudoConsoleHandle);

            if (AttributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(AttributeList);
                Marshal.FreeHGlobal(AttributeList);
            }

            GC.SuppressFinalize(this);
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new pseudo console session with the specified shell.
    /// </summary>
    /// <param name="command">The command/shell to execute (e.g., "wsl.exe", "powershell.exe").</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="columns">Number of columns for the terminal.</param>
    /// <param name="rows">Number of rows for the terminal.</param>
    /// <returns>A ConPtySession with input/output streams and process info.</returns>
    public static ConPtySession CreateSession(
        string command,
        string? workingDirectory = null,
        short columns = 120,
        short rows = 30)
    {
        // Create pipes for pseudo console I/O
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };

        // Pipe for ConPTY input: we write -> ConPTY reads
        if (!CreatePipe(out var pipeInputRead, out var pipeInputWrite, ref securityAttributes, 0))
            throw new InvalidOperationException($"Failed to create input pipe. Error: {Marshal.GetLastWin32Error()}");

        // Pipe for ConPTY output: ConPTY writes -> we read
        if (!CreatePipe(out var pipeOutputRead, out var pipeOutputWrite, ref securityAttributes, 0))
        {
            pipeInputRead.Dispose();
            pipeInputWrite.Dispose();
            throw new InvalidOperationException($"Failed to create output pipe. Error: {Marshal.GetLastWin32Error()}");
        }

        // Create the pseudo console
        var consoleSize = new COORD(columns, rows);
        int hr = CreatePseudoConsole(consoleSize, pipeInputRead, pipeOutputWrite, 0, out var pseudoConsoleHandle);
        if (hr != S_OK)
        {
            pipeInputRead.Dispose();
            pipeInputWrite.Dispose();
            pipeOutputRead.Dispose();
            pipeOutputWrite.Dispose();
            throw new InvalidOperationException($"Failed to create pseudo console. HRESULT: 0x{hr:X8}");
        }

        // Initialize the process thread attribute list
        var attributeList = CreateProcessAttributeList(pseudoConsoleHandle);

        // Set up startup info
        var startupInfo = new STARTUPINFOEX
        {
            StartupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFOEX>(),
                dwFlags = STARTF_USESTDHANDLES
            },
            lpAttributeList = attributeList
        };

        // Create the process
        if (!CreateProcessW(
            null,
            command,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            EXTENDED_STARTUPINFO_PRESENT,
            IntPtr.Zero,
            workingDirectory,
            ref startupInfo,
            out var processInfo))
        {
            int error = Marshal.GetLastWin32Error();
            ClosePseudoConsole(pseudoConsoleHandle);
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            pipeInputRead.Dispose();
            pipeInputWrite.Dispose();
            pipeOutputRead.Dispose();
            pipeOutputWrite.Dispose();
            throw new InvalidOperationException($"Failed to create process '{command}'. Win32 error: {error}");
        }

        // Create managed streams for I/O
        var inputStream = new FileStream(pipeInputWrite, FileAccess.Write, BUFFER_SIZE, false);
        var outputStream = new FileStream(pipeOutputRead, FileAccess.Read, BUFFER_SIZE, false);

        var session = new ConPtySession
        {
            PseudoConsoleHandle = pseudoConsoleHandle,
            ProcessInfo = processInfo,
            InputWriter = inputStream,
            OutputReader = outputStream,
            AttributeList = attributeList
        };

        session.SetInternalHandles(pipeInputRead, pipeOutputWrite);

        return session;
    }

    /// <summary>
    /// Resizes an active pseudo console.
    /// </summary>
    public static void Resize(IntPtr pseudoConsoleHandle, short columns, short rows)
    {
        if (pseudoConsoleHandle == IntPtr.Zero) return;
        var size = new COORD(columns, rows);
        int hr = ResizePseudoConsole(pseudoConsoleHandle, size);
        if (hr != S_OK)
            throw new InvalidOperationException($"Failed to resize pseudo console. HRESULT: 0x{hr:X8}");
    }

    /// <summary>
    /// Writes text input to the terminal.
    /// </summary>
    public static async Task WriteInputAsync(ConPtySession session, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await session.InputWriter.WriteAsync(bytes);
        await session.InputWriter.FlushAsync();
    }

    /// <summary>
    /// Reads output from the terminal asynchronously.
    /// </summary>
    public static async Task ReadOutputAsync(
        ConPtySession session,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BUFFER_SIZE];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await session.OutputReader.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead <= 0) break;

                string output = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                onOutput(output);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (IOException)
        {
            // Process exited, pipe broken
        }
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates and initializes a process thread attribute list with the pseudo console handle.
    /// </summary>
    private static IntPtr CreateProcessAttributeList(IntPtr pseudoConsoleHandle)
    {
        var size = IntPtr.Zero;

        // First call to get required size
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);

        // Allocate memory
        var attributeList = Marshal.AllocHGlobal(size);

        // Initialize the attribute list
        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        {
            Marshal.FreeHGlobal(attributeList);
            throw new InvalidOperationException(
                $"Failed to initialize process thread attribute list. Error: {Marshal.GetLastWin32Error()}");
        }

        // Set the pseudo console attribute
        if (!UpdateProcThreadAttribute(
            attributeList,
            0,
            (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            pseudoConsoleHandle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            throw new InvalidOperationException(
                $"Failed to update process thread attribute. Error: {Marshal.GetLastWin32Error()}");
        }

        return attributeList;
    }
}
