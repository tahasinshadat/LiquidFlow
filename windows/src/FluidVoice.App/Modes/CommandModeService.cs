using System.Text.Json;
using System.Text.Json.Nodes;
using FluidVoice.Ai;
using FluidVoice.App;
using FluidVoice.Core;

namespace FluidVoice.Modes;

public enum ChatRole { User, Assistant, Tool }
public enum StepType { Normal, Thinking, Checking, Executing, Verifying, Success, Failure }

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public string? ToolCallId { get; set; }
    public string? ToolCommand { get; set; }
    public string? ToolPurpose { get; set; }
    public StepType StepType { get; set; } = StepType.Normal;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<ChatMessage> Messages { get; set; } = new();
}

/// <summary>
/// Command Mode: an agentic loop that controls the PC by voice
/// (CommandModeService.swift). One tool — execute_terminal_command — runs
/// PowerShell; the model checks prerequisites, executes, verifies.
/// Temperature 0.1, max 20 turns, destructive-command confirmation gate.
/// </summary>
public sealed class CommandModeService
{
    public const int MaxTurns = 20; // CommandModeService.swift:17
    private const int MaxChats = 30;

    public event Action? StateChanged;
    public event Action<string>? StreamingText;
    /// <summary>Raised when a destructive command needs user confirmation.</summary>
    public event Action<string>? ConfirmationNeeded;

    public ChatSession Current { get; private set; } = new();
    public List<ChatSession> RecentChats { get; } = new();
    public bool IsProcessing { get; private set; }
    public string? PendingCommandJson { get; private set; }

    private CancellationTokenSource? _cts;
    private (LlmToolCall Call, string? WorkingDir, string? Purpose)? _pendingToolCall;

    public CommandModeService()
    {
        LoadChats();
    }

    // ---- system prompt: structure identical to CommandModeService.swift:736-815,
    //      shell/apps sections adapted from zsh+osascript to PowerShell+Windows ----
    public const string SystemPrompt =
"""
You are an autonomous, thoughtful Windows terminal agent. Execute user requests reliably and safely.

## AGENTIC WORKFLOW (Follow this pattern):

### 1. PRE-FLIGHT CHECKS (Always do this first!)
Before ANY action, verify prerequisites:
- File operations: Check if file/folder exists first (`Test-Path`, `Get-ChildItem`)
- Deletions: List contents before removing, confirm target exists
- Modifications: Read current state before changing
- Installations: Check if already installed (`Get-Command <name>`, `--version`)

### 2. EXECUTE WITH CONTEXT
When calling execute_terminal_command, ALWAYS include a `purpose` parameter explaining:
- "checking" - Verifying something exists/state
- "executing" - Performing the main action
- "verifying" - Confirming the result
Example purposes: "Checking if image1.png exists", "Creating the backup directory", "Verifying file was deleted"

### 3. POST-ACTION VERIFICATION
After modifying anything, verify it worked:
- Created file? `Test-Path` to confirm it exists
- Deleted file? `Test-Path` to confirm it's gone
- Modified content? `Get-Content` to verify changes
- Installed app? Check version/existence

### 4. HANDLE FAILURES GRACEFULLY
- If something doesn't exist: Tell the user clearly
- If command fails: Analyze error, try alternative approach
- If permission denied: Explain and suggest solutions
- Never assume success without verification

## RESPONSE FORMAT:
- Keep reasoning brief and clear
- State what you're checking/doing before each command
- After verification, give a clear success/failure summary
- Use natural language, not code comments

## SAFETY RULES:
- For destructive ops (Remove-Item, Move-Item, overwrite): ALWAYS check target exists first
- Show what will be affected before destroying
- List contents before bulk deletes
- Use full absolute paths when possible

## EXAMPLES OF GOOD BEHAVIOR:

User: "Delete image1.png in Downloads"
You: First check if it exists
→ execute_terminal_command(command: "Get-ChildItem ~\Downloads\image1.png", purpose: "Checking if image1.png exists")
If exists → execute_terminal_command(command: "Remove-Item ~\Downloads\image1.png", purpose: "Deleting the file")
Then verify → execute_terminal_command(command: "Test-Path ~\Downloads\image1.png", purpose: "Verifying file was deleted")
Finally: "✓ Successfully deleted image1.png from Downloads."

User: "Create a project folder with a readme"
You: → Check if folder exists, create it, create readme, verify both

## NATIVE WINDOWS APP CONTROL:
Use PowerShell for app launching and system actions:

### Apps:
- Launch app: `Start-Process notepad` / `Start-Process "C:\Path\To\App.exe"`
- Launch Store app: `Start-Process "shell:AppsFolder\<AppUserModelId>"`
- Open URL in default browser: `Start-Process "https://example.com"`
- Open folder in Explorer: `Start-Process explorer.exe -ArgumentList "C:\Path"`

### System:
- Settings pages: `Start-Process "ms-settings:display"` (bluetooth, sound, privacy-microphone, ...)
- Lock the PC: `rundll32.exe user32.dll,LockWorkStation`
- Empty recycle bin: `Clear-RecycleBin -Force`
- Screenshot: `Start-Process ms-screenclip:` (Snipping Tool)
- Volume/media: use the dedicated keys if asked, or `Set-Process` audio via apps

### Scheduled tasks / processes:
- List processes: `Get-Process | Sort-Object CPU -Descending | Select-Object -First 10`
- Stop a process: `Stop-Process -Name <name>` (destructive — confirm target first)

The user is on Windows with PowerShell. Be thorough but efficient.
When task is complete, provide a clear summary starting with ✓ or ✗.
""";

    private static readonly LlmTool TerminalTool = new(
        "execute_terminal_command",
        """
        Execute a PowerShell command on the user's Windows computer.
        Use this for file operations (Get-ChildItem, Get-Content, New-Item, Remove-Item), git commands, winget, npm, python, or any CLI tool.

        IMPORTANT: Follow the agentic workflow:
        1. ALWAYS check prerequisites first (file exists, command available)
        2. Execute the main action
        3. Verify the result

        Returns JSON with: success (bool), output (stdout), error (stderr), exitCode, purpose.
        """,
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["command"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The PowerShell command to execute (e.g., 'Get-ChildItem', 'git status', 'Remove-Item file.txt')",
                },
                ["workingDirectory"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional working directory path. Defaults to user's home directory.",
                },
                ["purpose"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Brief description of why this command is being run. Must be one of:\n- 'checking' (verifying prerequisites)\n- 'executing' (main action)\n- 'verifying' (confirming result)\nExample: 'Checking if config.json exists'",
                },
            },
            ["required"] = new JsonArray("command", "purpose"),
        });

    // destructive detection (CommandModeService.swift:594-634, Windows-extended)
    private static readonly string[] DestructivePrefixes =
    {
        "rm ", "rmdir ", "rd ", "del ", "erase ", "mv ", "move ", "sudo ", "kill ", "pkill ", "killall ",
        "taskkill", "stop-process", "remove-item", "move-item", "clear-content", "clear-recyclebin",
        "format", "chmod ", "chown ", "dd ", "mkfs", "> ", "truncate ", "shred ", "reg delete", "diskpart",
    };
    private static readonly string[] DestructivePatterns =
    {
        "| rm ", "| sudo ", "| dd ", "; rm ", "; sudo ", "&& rm ", "&& sudo ", "xargs rm", "xargs -i",
        "rm -", "| remove-item", "; remove-item", "&& remove-item", "-recurse -force", "| stop-process",
    };

    public static bool IsDestructiveCommand(string command)
    {
        var lower = command.TrimStart().ToLowerInvariant();
        return DestructivePrefixes.Any(lower.StartsWith) || DestructivePatterns.Any(lower.Contains);
    }

    // ------------------------------------------------------------------

    public void NewChat()
    {
        if (Current.Messages.Count > 0) SaveCurrent();
        Current = new ChatSession();
        StateChanged?.Invoke();
    }

    public void OpenChat(string id)
    {
        var chat = RecentChats.FirstOrDefault(c => c.Id == id);
        if (chat is null) return;
        SaveCurrent();
        Current = chat;
        StateChanged?.Invoke();
    }

    public void DeleteCurrentChat()
    {
        RecentChats.RemoveAll(c => c.Id == Current.Id);
        Current = new ChatSession();
        PersistChats();
        StateChanged?.Invoke();
    }

    public async Task ProcessUserCommandAsync(string text)
    {
        if (IsProcessing || string.IsNullOrWhiteSpace(text)) return;

        var providerId = ProviderCatalog.EffectiveCommandModeProviderId();
        if (providerId.Length == 0)
        {
            var msg = "Command Mode needs a verified AI provider with tool support. Configure one in Settings → AI Enhancement.";
            Current.Messages.Add(new ChatMessage { Role = ChatRole.Assistant, Content = msg, StepType = StepType.Failure });
            Notifications.NotifyCommandModeSetup(msg);
            StateChanged?.Invoke();
            return;
        }

        Current.Messages.Add(new ChatMessage { Role = ChatRole.User, Content = text });
        if (Current.Messages.Count == 1)
            Current.Title = text.Length <= 50 ? text : text[..50]; // title = first message ≤50 chars
        Current.UpdatedAt = DateTime.Now;
        SaveCurrent();
        StateChanged?.Invoke();

        IsProcessing = true;
        _cts = new CancellationTokenSource();
        try
        {
            await ProcessTurnsAsync(providerId, _cts.Token);
        }
        catch (Exception ex)
        {
            Current.Messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = $"Error: {ex.Message}",
                StepType = StepType.Failure,
            });
            Log.Error("command", "Command mode failed", ex);
        }
        finally
        {
            IsProcessing = false;
            SaveCurrent();
            StateChanged?.Invoke();
        }
    }

    private async Task ProcessTurnsAsync(string providerId, CancellationToken ct)
    {
        var model = ProviderCatalog.EffectiveCommandModeModel()
            ?? throw new InvalidOperationException("No model selected for Command Mode");

        for (int turn = 0; turn < MaxTurns; turn++)
        {
            ct.ThrowIfCancellationRequested();

            var messages = new List<LlmMessage> { new("system", SystemPrompt) };
            foreach (var m in Current.Messages)
            {
                messages.Add(m.Role switch
                {
                    ChatRole.User => new LlmMessage("user", m.Content),
                    ChatRole.Tool => new LlmMessage("tool", m.Content, m.ToolCallId),
                    _ => m.ToolCommand is not null
                        ? new LlmMessage("assistant", m.Content, ToolCalls: new List<LlmToolCall>
                        {
                            new(m.ToolCallId ?? "", "execute_terminal_command",
                                JsonSerializer.Serialize(new { command = m.ToolCommand, purpose = m.ToolPurpose })),
                        })
                        : new LlmMessage("assistant", m.Content),
                });
            }

            var response = await LlmClient.CallAsync(new LlmRequest
            {
                ProviderId = providerId,
                Model = model,
                Messages = messages,
                Temperature = 0.1,                       // CommandModeService.swift:901
                MaxTokens = LlmClient.IsReasoningModel(model) ? 32_000 : null,
                Tools = new List<LlmTool> { TerminalTool },
                Stream = Settings.Current.EnableAIStreaming && providerId != "anthropic",
                OnContentDelta = s => StreamingText?.Invoke(s),
            }, ct);

            var toolCall = response.ToolCalls.FirstOrDefault(tc => tc.Name == "execute_terminal_command");
            if (toolCall is null)
            {
                // final answer
                Current.Messages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = response.Content,
                    StepType = response.Content.StartsWith('✓') ? StepType.Success
                        : response.Content.StartsWith('✗') ? StepType.Failure : StepType.Normal,
                });
                StateChanged?.Invoke();
                return;
            }

            string command = "", purpose = "executing";
            string? workingDir = null;
            try
            {
                using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
                if (doc.RootElement.TryGetProperty("command", out var c)) command = c.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("purpose", out var p)) purpose = p.GetString() ?? "executing";
                if (doc.RootElement.TryGetProperty("workingDirectory", out var w)) workingDir = w.GetString();
            }
            catch (Exception ex)
            {
                Log.Warn("command", $"Bad tool arguments: {ex.Message}");
            }

            Current.Messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = response.Content,
                ToolCallId = toolCall.Id,
                ToolCommand = command,
                ToolPurpose = purpose,
                StepType = purpose switch
                {
                    "checking" => StepType.Checking,
                    "verifying" => StepType.Verifying,
                    _ => StepType.Executing,
                },
            });
            StateChanged?.Invoke();

            // confirmation gate for destructive commands (CommandModeView.swift:154)
            if (Settings.Current.CommandModeConfirmBeforeExecute && IsDestructiveCommand(command))
            {
                _pendingToolCall = (toolCall, workingDir, purpose);
                PendingCommandJson = command;
                ConfirmationNeeded?.Invoke(command);
                StateChanged?.Invoke();
                return; // resumes via ConfirmPendingAsync / CancelPending
            }

            await ExecuteToolAndContinueAsync(toolCall, command, workingDir, purpose, ct);
        }

        Current.Messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "Reached maximum steps limit. Please review the progress and continue if needed.",
            StepType = StepType.Failure,
        });
        StateChanged?.Invoke();
    }

    private async Task ExecuteToolAndContinueAsync(LlmToolCall toolCall, string command, string? workingDir, string purpose, CancellationToken ct)
    {
        var result = await TerminalService.ExecuteAsync(command, workingDir, ct);
        Current.Messages.Add(new ChatMessage
        {
            Role = ChatRole.Tool,
            Content = TerminalService.ToJson(result, purpose),
            ToolCallId = toolCall.Id,
            StepType = result.Success ? StepType.Success : StepType.Failure,
        });
        Current.UpdatedAt = DateTime.Now;
        SaveCurrent();
        StateChanged?.Invoke();
    }

    public async Task ConfirmPendingAsync()
    {
        if (_pendingToolCall is not { } pending) return;
        _pendingToolCall = null;
        PendingCommandJson = null;
        IsProcessing = true;
        StateChanged?.Invoke();
        _cts = new CancellationTokenSource();
        try
        {
            string command = "";
            try
            {
                using var doc = JsonDocument.Parse(pending.Call.ArgumentsJson);
                if (doc.RootElement.TryGetProperty("command", out var c)) command = c.GetString() ?? "";
            }
            catch { }
            await ExecuteToolAndContinueAsync(pending.Call, command, pending.WorkingDir, pending.Purpose ?? "executing", _cts.Token);
            var providerId = ProviderCatalog.EffectiveCommandModeProviderId();
            if (providerId.Length > 0) await ProcessTurnsAsync(providerId, _cts.Token);
        }
        catch (Exception ex)
        {
            Current.Messages.Add(new ChatMessage { Role = ChatRole.Assistant, Content = $"Error: {ex.Message}", StepType = StepType.Failure });
        }
        finally
        {
            IsProcessing = false;
            SaveCurrent();
            StateChanged?.Invoke();
        }
    }

    public void CancelPending()
    {
        _pendingToolCall = null;
        PendingCommandJson = null;
        Current.Messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "Command cancelled by user.",
            StepType = StepType.Failure,
        });
        SaveCurrent();
        StateChanged?.Invoke();
    }

    public void CancelProcessing() => _cts?.Cancel();

    // ---- persistence (ChatHistoryStore.swift: max 30 chats) ----

    private void SaveCurrent()
    {
        if (Current.Messages.Count == 0) return;
        var existing = RecentChats.FindIndex(c => c.Id == Current.Id);
        if (existing >= 0) RecentChats[existing] = Current;
        else RecentChats.Insert(0, Current);
        while (RecentChats.Count > MaxChats)
            RecentChats.RemoveAt(RecentChats.Count - 1);
        PersistChats();
    }

    private void PersistChats()
    {
        try
        {
            File.WriteAllText(AppPaths.ChatHistoryFile, JsonSerializer.Serialize(RecentChats));
        }
        catch (Exception ex)
        {
            Log.Warn("command", $"Failed to persist chats: {ex.Message}");
        }
    }

    private void LoadChats()
    {
        try
        {
            if (!File.Exists(AppPaths.ChatHistoryFile)) return;
            var chats = JsonSerializer.Deserialize<List<ChatSession>>(File.ReadAllText(AppPaths.ChatHistoryFile));
            if (chats is not null) RecentChats.AddRange(chats.OrderByDescending(c => c.UpdatedAt));
        }
        catch (Exception ex)
        {
            Log.Warn("command", $"Failed to load chats: {ex.Message}");
        }
    }
}
