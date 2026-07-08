import React, { useEffect, useState } from "react";
import { call, isNative } from "./core.js";

// ---- tiny inline icon set (stroke, currentColor) ----
const Icon = ({ d, size = 20, fill = "none" }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill={fill} stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
    {d}
  </svg>
);
const IHome = <path d="M3 10.5 12 3l9 7.5M5 9.5V21h14V9.5" />;
const IChart = <><path d="M4 20V10M10 20V4M16 20v-6M22 20H2" /></>;
const IBook = <><path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5z" /><path d="M4 20.5A2.5 2.5 0 0 1 6.5 18H20v3" /></>;
const IClock = <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>;
const ISpark = <path d="M13 2 6 13h5l-1 9 7-11h-5z" />;
const IGear = <><circle cx="12" cy="12" r="3.2" /><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1" /></>;
const IMic = <><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M6 11a6 6 0 0 0 12 0M12 17v4" /></>;

const rail = [
  { key: "home", label: "Home", d: IHome },
  { key: "insights", label: "Insights", d: IChart },
  { key: "dictionary", label: "Dictionary", d: IBook },
  { key: "history", label: "History", d: IClock },
  { key: "scratchpad", label: "Scratchpad", d: ISpark },
];

function Rail({ active, setActive }) {
  return (
    <div className="flex w-[68px] shrink-0 flex-col items-center gap-1 py-3">
      <div className="mb-4 flex h-11 w-11 items-center justify-center rounded-[13px] bg-gradient-to-br from-teal to-teal-deep shadow-lg shadow-teal-deep/30">
        <span className="flex items-end gap-[2px]">
          <i className="block w-[3px] rounded-full bg-white/90" style={{ height: 8 }} />
          <i className="block w-[3px] rounded-full bg-white" style={{ height: 15 }} />
          <i className="block w-[3px] rounded-full bg-white/90" style={{ height: 8 }} />
        </span>
      </div>
      {rail.map((r) => (
        <button
          key={r.key}
          onClick={() => setActive(r.key)}
          title={r.label}
          className={`flex h-11 w-11 items-center justify-center rounded-xl transition-all duration-150 ${
            active === r.key ? "bg-card2 text-teal" : "text-muted hover:bg-card2/60 hover:text-text"
          }`}
        >
          <Icon d={r.d} />
        </button>
      ))}
      <div className="mt-auto flex flex-col items-center gap-1">
        <button className="flex h-11 w-11 items-center justify-center rounded-xl text-muted transition-all hover:bg-card2/60 hover:text-text" title="Settings">
          <Icon d={IGear} />
        </button>
      </div>
    </div>
  );
}

function SetupRow({ title, sub, done, action }) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-line bg-card2/60 px-4 py-3">
      <div className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-[11px] font-bold ${done ? "bg-teal-deep text-white" : "border border-line text-muted"}`}>
        {done ? "✓" : ""}
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-[13.5px] font-semibold">{title}</div>
        <div className="text-[11.5px] text-muted">{sub}</div>
      </div>
      {done ? (
        <span className="rounded-full bg-teal-soft px-3 py-1 text-[12px] font-semibold text-teal">✓ Done</span>
      ) : (
        <button className="rounded-lg border border-line bg-card px-3 py-1.5 text-[12px] font-medium hover:border-teal/50">{action}</button>
      )}
    </div>
  );
}

function Stat({ n, label }) {
  return (
    <div>
      <div className="font-display text-[30px] leading-none">{n}</div>
      <div className="mt-1 text-[12.5px] text-muted">{label}</div>
    </div>
  );
}

function Home({ state }) {
  const s = state?.stats ?? {};
  const profileModel = state?.selectedModel ?? "Parakeet TDT 0.6B v2";
  return (
    <div className="mx-auto max-w-[1120px]">
      <h1 className="text-[26px] font-semibold">{state?.greeting ?? "Welcome"}, {state?.name ?? "there"}</h1>
      <p className="mt-1 text-[14px] text-muted">Keep your hands on the work. LiquidFlow will handle the words.</p>

      <div className="mt-6 grid grid-cols-[1fr_300px] gap-6">
        <div className="space-y-5">
          {/* hero */}
          <div className="relative overflow-hidden rounded-2xl p-8" style={{ background: "linear-gradient(120deg,#0e1213 0%,#102a2b 55%,#185e5b 100%)" }}>
            <div className="pointer-events-none absolute -right-10 -top-16 h-64 w-64 rounded-full opacity-50 blur-2xl" style={{ background: "radial-gradient(circle,#4ad6c4aa,transparent 70%)" }} />
            <h2 className="font-display text-[30px] text-white">
              Make LiquidFlow sound like <span className="italic">you</span>
            </h2>
            <p className="mt-2 max-w-md text-[14px] font-semibold text-white/90">Finish setup for {profileModel}, then dictate into any Windows app.</p>
            <button className="mt-5 rounded-lg bg-white/95 px-4 py-2 text-[13px] font-semibold text-ink transition hover:bg-white">Finish setup</button>
          </div>

          {/* quick setup + how to use */}
          <div className="grid grid-cols-2 gap-4">
            <div className="rounded-2xl border border-line bg-card p-5">
              <div className="mb-3 flex items-center gap-2 text-[15px] font-semibold text-teal"><span>✓</span> Quick Setup</div>
              <div className="space-y-2.5">
                <SetupRow title="Voice Model Ready" sub="Speech recognition model loaded" done />
                <SetupRow title="Microphone Available" sub="Input device detected" done />
                <SetupRow title="Global Input Hooks" sub="Hotkeys + typing enabled" done />
                <SetupRow title="AI Enhancement" sub="Optional — local or cloud" action="Configure" />
              </div>
            </div>
            <div className="rounded-2xl border border-line bg-card p-5">
              <div className="mb-3 flex items-center gap-2 text-[15px] font-semibold text-teal">▸ How to Use</div>
              <ol className="space-y-4">
                {[["Start Recording", "Press your hotkey (Copilot key) in any app"], ["Speak Clearly", "Whispering works — quiet audio is boosted"], ["Auto-Type Result", "It lands in your focused app"]].map(([t, s], i) => (
                  <li key={i} className="flex gap-3">
                    <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-teal-soft text-[12px] font-bold text-teal">{i + 1}</span>
                    <div>
                      <div className="text-[13.5px] font-semibold">{t}</div>
                      <div className="text-[12px] text-muted">{s}</div>
                    </div>
                  </li>
                ))}
              </ol>
            </div>
          </div>
        </div>

        {/* right column */}
        <div className="space-y-5">
          <div className="flex flex-col gap-5 rounded-2xl border border-line bg-card p-6">
            <Stat n={s.totalWords >= 1000 ? (s.totalWords / 1000).toFixed(1) + "K" : (s.totalWords ?? 0)} label="total words" />
            <Stat n={s.wpm ?? 40} label="typing wpm" />
            <Stat n={s.streak ?? 0} label="day streak" />
          </div>
          <div className="rounded-2xl border border-line bg-card p-6">
            <div className="text-[16px] font-semibold">Your Voice Profile</div>
            <p className="mt-1 text-[12.5px] text-muted">Updates as LiquidFlow learns from your dictation.</p>
            <div className="mt-4 flex items-center gap-3">
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-card2">
                <div className="h-full w-1/3 rounded-full bg-teal" />
              </div>
              <span className="text-[12px] font-semibold text-teal">Active</span>
            </div>
            <div className="my-4 h-px bg-line" />
            <div className="flex items-center gap-2 text-[13px] font-semibold"><Icon d={IMic} size={16} /> {profileModel}</div>
            <div className="mt-1 text-[12px] text-muted">English</div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default function App() {
  const [active, setActive] = useState("home");
  const [state, setState] = useState(null);

  useEffect(() => {
    let alive = true;
    const load = () => call("getState").then((s) => alive && setState(s)).catch(() => {});
    load();
    // re-pull when the core signals settings/history changed
    import("./core.js").then(({ on }) => {
      on("settingsChanged", load);
      on("historyChanged", load);
    });
    return () => { alive = false; };
  }, []);

  return (
    <div className="flex h-full bg-ink">
      <Rail active={active} setActive={setActive} />
      <div className="flex-1 p-3 pl-0">
        <div className="h-full overflow-y-auto rounded-2xl border border-line bg-surface px-10 py-9">
          {active === "home" ? (
            <Home state={state} />
          ) : (
            <div className="mx-auto max-w-[1120px]">
              <h1 className="text-[26px] font-semibold capitalize">{active}</h1>
              <p className="mt-2 text-[14px] text-muted">Coming soon on the new UI — wiring this screen to the native core next.</p>
              {!isNative && <p className="mt-4 text-[12px] text-muted/70">(browser preview — running on mock data)</p>}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
