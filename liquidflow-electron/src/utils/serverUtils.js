const fs = require("fs");
const net = require("net");
const path = require("path");
const { killProcessGroup } = require("./process");

const GRACEFUL_STOP_TIMEOUT_MS = 5000;

function tryBind(port, host) {
  return new Promise((resolve) => {
    const s = net.createServer();
    s.once("error", () => resolve(false));
    s.once("listening", () => s.close(() => resolve(true)));
    s.listen(port, host);
  });
}

async function isPortAvailable(port) {
  return (
    (await tryBind(port, "0.0.0.0")) &&
    (await tryBind(port, "::")) &&
    (await tryBind(port, "127.0.0.1"))
  );
}

async function findAvailablePort(rangeStart, rangeEnd) {
  for (let port = rangeStart; port <= rangeEnd; port++) {
    if (await isPortAvailable(port)) return port;
  }
  throw new Error(`No available ports in range ${rangeStart}-${rangeEnd}`);
}

function resolveBinaryPath(binaryName) {
  const candidates = [];

  if (process.resourcesPath) {
    candidates.push(path.join(process.resourcesPath, "bin", binaryName));
  }

  const projectBinDir = path.resolve(__dirname, "..", "..", "resources", "bin");
  candidates.push(path.join(projectBinDir, binaryName));

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      try {
        fs.statSync(candidate);
        return candidate;
      } catch {
        // Can't access binary
      }
    }
  }

  return null;
}

// True CPU architecture of the machine, even when the Electron shell is running
// under x64 emulation on Windows-on-ARM. A native ARM64 child .exe (sherpa-onnx,
// whisper) launches and runs natively regardless of the parent's arch, so the STT
// engines get real ARM64 speed even inside an x64-emulated app. Windows sets
// PROCESSOR_ARCHITEW6432=ARM64 for a WOW64/emulated x64 process; a native arm64
// process reports process.arch === "arm64".
function getMachineArch() {
  if (process.arch === "arm64") return "arm64";
  if (process.platform === "win32") {
    const emulated = (
      process.env.PROCESSOR_ARCHITEW6432 ||
      process.env.PROCESSOR_ARCHITECTURE ||
      ""
    ).toUpperCase();
    if (emulated === "ARM64") return "arm64";
  }
  return process.arch;
}

// Architectures to try, most-preferred first, for a native engine binary. On ARM
// hardware we prefer a native arm64 build and fall back to x64 (which runs under
// emulation) when no arm64 binary is bundled for that engine.
function getEngineArchPriority() {
  return getMachineArch() === "arm64" ? ["arm64", "x64"] : [process.arch];
}

// Resolve the first existing binary across the arch-priority list. `makeName(arch)`
// builds the platform-arch-specific file name for a given arch.
function resolveArchBinaryPath(makeName) {
  for (const arch of getEngineArchPriority()) {
    const resolved = resolveBinaryPath(makeName(arch));
    if (resolved) return resolved;
  }
  return null;
}

async function gracefulStopProcess(proc) {
  killProcessGroup(proc, "SIGTERM");

  await new Promise((resolve) => {
    const timeout = setTimeout(() => {
      if (proc) killProcessGroup(proc, "SIGKILL");
      resolve();
    }, GRACEFUL_STOP_TIMEOUT_MS);

    if (proc) {
      proc.once("close", () => {
        clearTimeout(timeout);
        resolve();
      });
    } else {
      clearTimeout(timeout);
      resolve();
    }
  });
}

module.exports = {
  findAvailablePort,
  isPortAvailable,
  resolveBinaryPath,
  getMachineArch,
  getEngineArchPriority,
  resolveArchBinaryPath,
  gracefulStopProcess,
};
