using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluidVoice.Core;

namespace FluidVoice.Ai;

public sealed record LlmMessage(string Role, string Content, string? ToolCallId = null, List<LlmToolCall>? ToolCalls = null);
public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);
public sealed record LlmTool(string Name, string Description, JsonObject ParametersSchema);
public sealed record LlmResponse(string Content, string Thinking, List<LlmToolCall> ToolCalls);

public sealed class LlmRequest
{
    public required string ProviderId { get; init; }
    public required string Model { get; init; }
    public required List<LlmMessage> Messages { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public List<LlmTool>? Tools { get; init; }
    public int TimeoutSeconds { get; init; } = 30; // LLMClient.defaultTimeoutSeconds
    public bool Stream { get; init; }
    public Action<string>? OnContentDelta { get; init; }
    public Action<string>? OnThinkingDelta { get; init; }
}

/// <summary>
/// LLM client (port of LLMClient.swift): OpenAI-compatible chat completions with SSE
/// streaming and tool calls, Anthropic messages API (non-streaming), reasoning-model
/// parameter handling, and thinking-tag parsing (nemotron / qwen / separate-field).
/// </summary>
public static class LlmClient
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    public static bool IsReasoningModel(string model)
    {
        var m = model.ToLowerInvariant();
        return m.Contains("o1") || m.Contains("o3") || m.Contains("gpt-5");
    }

    public static bool IsLocalEndpoint(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1") return true;
        if (host.StartsWith("127.") || host.StartsWith("10.") || host.StartsWith("192.168.")) return true;
        if (host.StartsWith("172."))
        {
            var parts = host.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31)
                return true;
        }
        return false;
    }

    public static async Task<LlmResponse> CallAsync(LlmRequest request, CancellationToken ct)
    {
        if (request.ProviderId == ProviderCatalog.FluidLocalId)
            await LocalAiServer.EnsureRunningAsync(ct);

        var baseUrl = ProviderCatalog.BaseUrlFor(request.ProviderId);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException($"Provider {request.ProviderId} has no base URL");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, request.TimeoutSeconds)));

        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(attempt), timeoutCts.Token);
            try
            {
                return request.ProviderId == "anthropic"
                    ? await CallAnthropicAsync(request, baseUrl, timeoutCts.Token)
                    : await CallOpenAiCompatAsync(request, baseUrl, timeoutCts.Token);
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                Log.Warn("llm", $"Attempt {attempt + 1} failed: {ex.Message}");
            }
            catch (IOException ex)
            {
                lastError = ex;
                Log.Warn("llm", $"Attempt {attempt + 1} failed: {ex.Message}");
            }
        }
        throw new InvalidOperationException($"LLM request failed: {lastError?.Message}", lastError);
    }

    // ---------------- OpenAI-compatible ----------------

    private static string OpenAiEndpoint(string baseUrl)
    {
        if (baseUrl.Contains("/chat/completions") || baseUrl.Contains("/api/chat") || baseUrl.Contains("/api/generate"))
            return baseUrl;
        return baseUrl.TrimEnd('/') + "/chat/completions";
    }

    private static async Task<LlmResponse> CallOpenAiCompatAsync(LlmRequest request, string baseUrl, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["stream"] = request.Stream,
        };

        var messages = new JsonArray();
        foreach (var m in request.Messages)
        {
            var msg = new JsonObject { ["role"] = m.Role };
            if (m.Role == "tool")
            {
                msg["tool_call_id"] = m.ToolCallId ?? "";
                msg["content"] = m.Content;
            }
            else
            {
                msg["content"] = m.Content;
                if (m.ToolCalls is { Count: > 0 })
                {
                    var calls = new JsonArray();
                    foreach (var tc in m.ToolCalls)
                    {
                        calls.Add(new JsonObject
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = tc.Name, ["arguments"] = tc.ArgumentsJson },
                        });
                    }
                    msg["tool_calls"] = calls;
                }
            }
            messages.Add(msg);
        }
        body["messages"] = messages;

        // reasoning models: no temperature, max_completion_tokens (LLMClient.swift:313-354)
        bool reasoning = IsReasoningModel(request.Model);
        if (!reasoning && request.Temperature is { } temp) body["temperature"] = temp;
        if (request.MaxTokens is { } max)
            body[reasoning ? "max_completion_tokens" : "max_tokens"] = max;

        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.ParametersSchema.DeepClone(),
                    },
                });
            }
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, OpenAiEndpoint(baseUrl));
        var apiKey = ProviderCatalog.ApiKeyFor(request.ProviderId) ?? "";
        if (apiKey.Length > 0)
            httpReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        httpReq.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        if (!request.Stream)
        {
            using var resp = await Http.SendAsync(httpReq, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(text, 400)}");
            return ParseOpenAiResponse(text, request.Model);
        }

        using var streamResp = await Http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!streamResp.IsSuccessStatusCode)
        {
            var err = await streamResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"HTTP {(int)streamResp.StatusCode}: {Truncate(err, 400)}");
        }
        return await ReadSseStreamAsync(streamResp, request, ct);
    }

    private static LlmResponse ParseOpenAiResponse(string json, string model)
    {
        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        string content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? "" : "";
        string thinking = "";
        foreach (var field in new[] { "reasoning_content", "reasoning", "thought", "thinking" })
        {
            if (message.TryGetProperty(field, out var r) && r.ValueKind == JsonValueKind.String)
            {
                thinking = r.GetString() ?? "";
                break;
            }
        }
        var toolCalls = new List<LlmToolCall>();
        if (message.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcs.EnumerateArray())
            {
                var fn = tc.GetProperty("function");
                toolCalls.Add(new LlmToolCall(
                    tc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    fn.GetProperty("name").GetString() ?? "",
                    fn.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}"));
            }
        }
        var (tagThinking, cleaned) = StripThinkingTags(content, model);
        return new LlmResponse(cleaned, CombineThinking(thinking, tagThinking), toolCalls);
    }

    private static async Task<LlmResponse> ReadSseStreamAsync(HttpResponseMessage resp, LlmRequest request, CancellationToken ct)
    {
        var contentBuf = new StringBuilder();
        var thinkingBuf = new StringBuilder();
        var toolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        var tagParser = new ThinkingTagStreamParser(request.Model,
            s => { thinkingBuf.Append(s); request.OnThinkingDelta?.Invoke(s); },
            s => { contentBuf.Append(s); request.OnContentDelta?.Invoke(s); });

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]") break;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(payload); }
            catch { continue; }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;
                var delta = choices[0].TryGetProperty("delta", out var d) ? d : default;
                if (delta.ValueKind != JsonValueKind.Object) continue;

                foreach (var field in new[] { "reasoning_content", "reasoning", "thought", "thinking" })
                {
                    if (delta.TryGetProperty(field, out var r) && r.ValueKind == JsonValueKind.String)
                    {
                        var t = r.GetString() ?? "";
                        if (t.Length > 0)
                        {
                            thinkingBuf.Append(t);
                            request.OnThinkingDelta?.Invoke(t);
                        }
                    }
                }
                if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    var t = contentEl.GetString() ?? "";
                    if (t.Length > 0) tagParser.Feed(t);
                }
                if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        int idx = tc.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
                        if (!toolCalls.TryGetValue(idx, out var entry))
                            entry = ("", "", new StringBuilder());
                        if (tc.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                            entry.Id = id.GetString() ?? entry.Id;
                        if (tc.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                                entry.Name += name.GetString();
                            if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                                entry.Args.Append(args.GetString());
                        }
                        toolCalls[idx] = entry;
                    }
                }
            }
        }
        tagParser.Flush();

        var calls = toolCalls.OrderBy(kv => kv.Key)
            .Select(kv => new LlmToolCall(kv.Value.Id, kv.Value.Name, kv.Value.Args.ToString()))
            .Where(tc => tc.Name.Length > 0)
            .ToList();
        return new LlmResponse(contentBuf.ToString().Trim(), thinkingBuf.ToString(), calls);
    }

    // ---------------- Anthropic messages API ----------------

    private static async Task<LlmResponse> CallAnthropicAsync(LlmRequest request, string baseUrl, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxTokens ?? 8192,
        };
        if (request.Temperature is { } temp) body["temperature"] = temp;

        string? system = null;
        var messages = new JsonArray();
        foreach (var m in request.Messages)
        {
            if (m.Role == "system")
            {
                system = system is null ? m.Content : system + "\n\n" + m.Content;
                continue;
            }
            if (m.Role == "tool")
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = m.ToolCallId ?? "",
                            ["content"] = m.Content,
                        },
                    },
                });
                continue;
            }
            if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
            {
                var content = new JsonArray();
                if (!string.IsNullOrEmpty(m.Content))
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = m.Content });
                foreach (var tc in m.ToolCalls)
                {
                    JsonNode input;
                    try { input = JsonNode.Parse(tc.ArgumentsJson) ?? new JsonObject(); }
                    catch { input = new JsonObject(); }
                    content.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = tc.Id,
                        ["name"] = tc.Name,
                        ["input"] = input,
                    });
                }
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                continue;
            }
            messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        }
        if (system is not null) body["system"] = system;
        body["messages"] = messages;

        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = t.ParametersSchema.DeepClone(),
                });
            }
            body["tools"] = tools;
        }

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/messages");
        var apiKey = ProviderCatalog.ApiKeyFor("anthropic") ?? "";
        httpReq.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        httpReq.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01"); // ModelRepository.swift:273-276
        httpReq.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(httpReq, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(text, 400)}");

        using var doc = JsonDocument.Parse(text);
        var contentSb = new StringBuilder();
        var thinkingSb = new StringBuilder();
        var toolCalls = new List<LlmToolCall>();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            switch (block.GetProperty("type").GetString())
            {
                case "text":
                    contentSb.Append(block.GetProperty("text").GetString());
                    break;
                case "thinking":
                    thinkingSb.Append(block.TryGetProperty("thinking", out var th) ? th.GetString() : "");
                    break;
                case "tool_use":
                    toolCalls.Add(new LlmToolCall(
                        block.GetProperty("id").GetString() ?? "",
                        block.GetProperty("name").GetString() ?? "",
                        block.TryGetProperty("input", out var input) ? input.GetRawText() : "{}"));
                    break;
            }
        }
        var streamedContent = contentSb.ToString().Trim();
        request.OnContentDelta?.Invoke(streamedContent);
        return new LlmResponse(streamedContent, thinkingSb.ToString(), toolCalls);
    }

    /// <summary>Fetch available model ids from an OpenAI-compatible /models endpoint.</summary>
    public static async Task<List<string>> ListModelsAsync(string providerId, CancellationToken ct)
    {
        if (providerId == ProviderCatalog.FluidLocalId)
            return LocalAiServer.Models.Select(m => m.Id).ToList();

        var baseUrl = ProviderCatalog.BaseUrlFor(providerId);
        using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
        var apiKey = ProviderCatalog.ApiKeyFor(providerId) ?? "";
        if (providerId == "anthropic")
        {
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else if (apiKey.Length > 0)
        {
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var resp = await Http.SendAsync(req, cts.Token);
        var text = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(text, 300)}");
        using var doc = JsonDocument.Parse(text);
        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    list.Add(id.GetString()!);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    // ---------------- thinking-tag handling (ThinkingParsers.swift / LLMClient.swift:886-934) ----------------

    private static string CombineThinking(string a, string b) =>
        (a, b) switch
        {
            ("", var x) => x,
            (var x, "") => x,
            var (x, y) => x + "\n" + y,
        };

    public static (string Thinking, string Cleaned) StripThinkingTags(string text, string model)
    {
        if (text.Length == 0) return ("", text);
        var thinking = new StringBuilder();
        var working = text;

        var lower = model.ToLowerInvariant();
        bool nemo = lower.Contains("nemotron") || lower.Contains("nemo");
        if (nemo)
        {
            // everything before the first closing tag is thinking (no opening tag)
            var idx = working.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = working.IndexOf("</thinking>", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                thinking.Append(working[..idx]);
                var close = working.IndexOf('>', idx);
                working = close >= 0 ? working[(close + 1)..] : "";
            }
        }

        var pairRegex = new Regex(@"<think(?:ing)?>([\s\S]*?)</think(?:ing)?>", RegexOptions.IgnoreCase);
        working = pairRegex.Replace(working, m =>
        {
            thinking.Append(m.Groups[1].Value);
            return "";
        });
        var orphanRegex = new Regex(@"^([\s\S]*?)</think(?:ing)?>", RegexOptions.IgnoreCase);
        working = orphanRegex.Replace(working, m =>
        {
            thinking.Append(m.Groups[1].Value);
            return "";
        });
        working = working
            .Replace("</think>", "").Replace("</thinking>", "")
            .Replace("<think>", "").Replace("<thinking>", "");
        return (thinking.ToString(), working.Trim());
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>
/// Streaming thinking-tag state machine (LLMClient.swift:597-791): initial → inThinking →
/// inContent, with partial tags held back across chunk boundaries.
/// </summary>
internal sealed class ThinkingTagStreamParser
{
    private enum State { Initial, InThinking, InContent }

    private static readonly string[] OpenTags = { "<think>", "<thinking>" };
    private static readonly string[] CloseTags = { "</think>", "</thinking>" };

    private readonly Action<string> _onThinking;
    private readonly Action<string> _onContent;
    private readonly bool _nemo;
    private State _state = State.Initial;
    private string _pending = "";

    public ThinkingTagStreamParser(string model, Action<string> onThinking, Action<string> onContent)
    {
        _onThinking = onThinking;
        _onContent = onContent;
        var lower = model.ToLowerInvariant();
        _nemo = lower.Contains("nemotron") || lower.Contains("nemo");
        if (_nemo) _state = State.InThinking; // nemo: thinking until first </think>
    }

    public void Feed(string chunk)
    {
        _pending += chunk;
        Process();
    }

    public void Flush()
    {
        if (_pending.Length == 0) return;
        Emit(_pending);
        _pending = "";
    }

    private void Process()
    {
        while (_pending.Length > 0)
        {
            var (tags, isClose) = _state == State.InThinking ? (CloseTags, true) : (OpenTags, false);
            if (_state == State.InContent)
            {
                // once in content, everything flows through (stray close tags are stripped)
                var strayIdx = IndexOfAny(_pending, CloseTags, out var strayTag);
                if (strayIdx < 0)
                {
                    var keep = TrailingPartialTagLength(_pending, CloseTags);
                    Emit(_pending[..^keep]);
                    _pending = _pending[^keep..];
                    return;
                }
                Emit(_pending[..strayIdx]);
                _pending = _pending[(strayIdx + strayTag.Length)..];
                continue;
            }

            var idx = IndexOfAny(_pending, tags, out var tag);
            if (idx < 0)
            {
                var keep = TrailingPartialTagLength(_pending, tags);
                Emit(_pending[..^keep]);
                _pending = _pending[^keep..];
                return;
            }
            Emit(_pending[..idx]);
            _pending = _pending[(idx + tag.Length)..];
            _state = isClose ? State.InContent : State.InThinking;
        }
    }

    private void Emit(string text)
    {
        if (text.Length == 0) return;
        if (_state == State.InThinking) _onThinking(text);
        else _onContent(text);
    }

    private static int IndexOfAny(string haystack, string[] needles, out string found)
    {
        int best = -1;
        found = "";
        foreach (var n in needles)
        {
            var i = haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (best < 0 || i < best))
            {
                best = i;
                found = n;
            }
        }
        return best;
    }

    /// <summary>How many trailing chars might be the start of one of the tags (hold them back).</summary>
    private static int TrailingPartialTagLength(string text, string[] tags)
    {
        int maxLen = tags.Max(t => t.Length);
        for (int len = Math.Min(maxLen - 1, text.Length); len > 0; len--)
        {
            var tail = text[^len..];
            if (tags.Any(t => t.StartsWith(tail, StringComparison.OrdinalIgnoreCase)))
                return len;
        }
        return 0;
    }
}
