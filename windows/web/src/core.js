// Bridge client: talks to the native C# core over WebView2 postMessage.
// When running in a plain browser (vite dev, no WebView2) it falls back to mock
// data so the UI still renders for design work.

const wv = typeof window !== "undefined" ? window.chrome?.webview : null;
export const isNative = !!wv;

let seq = 1;
const pending = new Map();
const listeners = new Map(); // evt -> Set<cb>

if (wv) {
  wv.addEventListener("message", (e) => {
    const msg = e.data;
    if (msg && msg.evt) {
      const set = listeners.get(msg.evt);
      if (set) set.forEach((cb) => cb(msg.payload));
      return;
    }
    if (msg && typeof msg.id === "number" && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.ok ? resolve(msg.result) : reject(new Error(msg.error || "bridge error"));
    }
  });
}

export function call(method, args = {}) {
  if (!wv) return Promise.resolve(mock(method, args));
  return new Promise((resolve, reject) => {
    const id = seq++;
    pending.set(id, { resolve, reject });
    // post an object (not a JSON string) so C# WebMessageAsJson sees an object, not a quoted string
    wv.postMessage({ id, method, args });
    setTimeout(() => {
      if (pending.has(id)) {
        pending.delete(id);
        reject(new Error(`bridge timeout: ${method}`));
      }
    }, 15000);
  });
}

export function on(evt, cb) {
  if (!listeners.has(evt)) listeners.set(evt, new Set());
  listeners.get(evt).add(cb);
  return () => listeners.get(evt)?.delete(cb);
}

// ---- browser-dev mock data ----
function mock(method) {
  switch (method) {
    case "getState":
      return {
        name: "Tahasin",
        greeting: "Good evening",
        theme: "Dark",
        version: "1.6.2",
        hotkey: "Copilot key",
        selectedModel: "Parakeet TDT 0.6B v2",
        selectedModelId: "parakeet-tdt-0.6b-v2",
        aiProvider: "fluid-local",
        setupTested: false,
        stats: mock("getStats"),
        models: mock("getModels"),
        providers: mock("getProviders"),
      };
    case "getStats":
      return { totalWords: 117, wpm: 40, streak: 1, wordsToday: 42, aiRate: 0, topApps: [{ app: "Code", count: 3 }], daily: [] };
    case "getModels":
      return [
        { id: "parakeet-tdt-0.6b-v2", name: "Parakeet TDT 0.6B v2", tagline: "Fastest + Live Streaming", description: "NVIDIA Parakeet (ONNX int8). Near-instant, English only.", size: "700 MiB", ram: "~1.5 GB RAM", languages: "English", speed: 0.95, accuracy: 0.96, badge: "Recommended", engine: "Parakeet", livePreview: true, downloaded: true, selected: true },
        { id: "whisper-base", name: "Whisper Base", tagline: "Standard Choice", description: "Good balance of speed and accuracy.", size: "141 MiB", ram: "~0.6 GB RAM", languages: "99 Languages", speed: 0.8, accuracy: 0.6, badge: "Default", engine: "Whisper", livePreview: true, downloaded: false, selected: false },
      ];
    case "getProviders":
      return [
        { id: "fluid-local", name: "LiquidFlow Local AI", group: "On this PC", needsKey: false, configured: false, selected: true },
        { id: "ollama", name: "Ollama", group: "On this PC", needsKey: false, configured: false, selected: false },
        { id: "openai", name: "OpenAI", group: "Cloud providers", needsKey: true, configured: false, selected: false },
        { id: "anthropic", name: "Anthropic", group: "Cloud providers", needsKey: true, configured: false, selected: false },
        { id: "groq", name: "Groq", group: "Cloud providers", needsKey: true, configured: false, selected: false },
        { id: "google", name: "Google", group: "Cloud providers", needsKey: true, configured: false, selected: false },
        { id: "mistral", name: "Mistral", group: "Cloud providers", needsKey: true, configured: false, selected: false },
      ];
    case "getHistory":
      return [];
    case "getPrompt":
      return { body: "You are a FORMATTER, not an editor…", builtIn: "You are a FORMATTER…", customized: false };
    default:
      return { ok: true };
  }
}
