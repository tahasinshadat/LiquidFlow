using FluidVoice.Ai;
using FluidVoice.Core;
using FluidVoice.Text;
using FluidVoice.Typing;

namespace FluidVoice.Modes;

/// <summary>
/// Write / Rewrite ("Edit") mode (RewriteModeService.swift):
/// with a selection → rewrite it per the instruction; without → write new text.
/// Non-streaming by design, temperature 0.7. "Replace Original" restores focus
/// and types the result over the still-selected text.
/// </summary>
public sealed class RewriteModeService
{
    public event Action? StateChanged;

    public string OriginalText { get; private set; } = "";
    public string RewrittenText { get; private set; } = "";
    public string LastInstruction { get; private set; } = "";
    public string? LastError { get; private set; }
    public bool IsWriteMode => OriginalText.Length == 0;
    public bool IsProcessing { get; private set; }
    public FocusSnapshot? TargetFocus { get; private set; }

    /// <summary>Capture the selection + focus at hotkey time (before any UI shows).</summary>
    public void BeginSession(FocusSnapshot? focus)
    {
        TargetFocus = focus;
        OriginalText = SelectionReader.GetSelectedText() ?? "";
        RewrittenText = "";
        LastInstruction = "";
        LastError = null;
        StateChanged?.Invoke();
    }

    public async Task ApplyInstructionAsync(string instruction, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instruction) || IsProcessing) return;
        var providerId = ProviderCatalog.EffectiveRewriteModeProviderId();
        var model = ProviderCatalog.EffectiveRewriteModeModel();
        if (providerId.Length == 0 || model is null)
        {
            LastError = "Edit Mode needs a verified AI provider. Configure one in Settings → AI Enhancement.";
            StateChanged?.Invoke();
            return;
        }

        IsProcessing = true;
        LastInstruction = instruction;
        LastError = null;
        StateChanged?.Invoke();
        try
        {
            var appId = TargetFocus?.ProcessName;
            var (systemPrompt, _) = PromptStore.Resolve(PromptMode.Edit, appId);

            // context block for selected text (RewriteModeService.swift:245)
            var hasFollowUp = RewrittenText.Length > 0;
            var contextSource = hasFollowUp ? RewrittenText : OriginalText;
            if (contextSource.Length > 0)
                systemPrompt = systemPrompt + "\n\n" + PromptStore.RenderContextBlock(contextSource);

            // user message templates — verbatim from RewriteModeService.swift:104-119
            string userMessage;
            if (hasFollowUp)
                userMessage = $"Follow-up instruction: {instruction}\n\nApply this to the previous result. Output ONLY the updated text.";
            else if (OriginalText.Length > 0)
                userMessage = $"User's instruction: {instruction}\n\nApply the instruction to the selected context. Output ONLY the rewritten text, nothing else.";
            else
                userMessage = $"User's instruction: {instruction}\n\nOutput ONLY the requested text, nothing else.";

            var response = await LlmClient.CallAsync(new LlmRequest
            {
                ProviderId = providerId,
                Model = model,
                Messages = new List<LlmMessage>
                {
                    new("system", systemPrompt),
                    new("user", userMessage),
                },
                Temperature = 0.7,   // RewriteModeService.swift:373
                MaxTokens = LlmClient.IsReasoningModel(model) ? 32_000 : null,
                Stream = false,      // deliberately non-streaming (RewriteModeService.swift:347)
            }, ct);

            if (string.IsNullOrWhiteSpace(response.Content))
                throw new InvalidOperationException("Empty response from AI provider");
            RewrittenText = response.Content.Trim();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Warn("rewrite", $"Rewrite failed: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            StateChanged?.Invoke();
        }
    }

    /// <summary>"Replace Original": restore focus, type the result (replaces the selection).</summary>
    public async Task<bool> AcceptAsync()
    {
        if (RewrittenText.Length == 0) return false;
        var text = RewrittenText;
        var target = TargetFocus;
        return await Task.Run(() =>
        {
            if (target is not null) FocusTracker.Restore(target);
            Thread.Sleep(80);
            return TypingService.TypeTextInstantly(text, target);
        });
    }

    public void TryAgain()
    {
        RewrittenText = "";
        LastError = null;
        StateChanged?.Invoke();
    }
}
