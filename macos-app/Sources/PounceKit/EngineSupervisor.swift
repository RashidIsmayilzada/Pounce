import Foundation

/// Where the bundled Engine's pieces are, and where it keeps its data.
public struct EngineLayout: Sendable, Equatable {
    public let java: URL
    public let jar: URL
    public let modelDir: URL
    public let home: URL

    public init(java: URL, jar: URL, modelDir: URL, home: URL) {
        self.java = java
        self.jar = jar
        self.modelDir = modelDir
        self.home = home
    }

    /// Inside an app bundle: `Contents/Resources/{runtime,engine,models}`; data in Application Support,
    /// or in POUNCE_HOME when set (used by the smoke check to keep away from real folders).
    public static func bundled(resources: URL) -> EngineLayout {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let home = ProcessInfo.processInfo.environment["POUNCE_HOME"].map { URL(fileURLWithPath: $0) }
            ?? support.appendingPathComponent("Pounce")
        return EngineLayout(
            java: resources.appendingPathComponent("runtime/bin/java"),
            jar: resources.appendingPathComponent("engine/pounce-engine.jar"),
            modelDir: resources.appendingPathComponent("models/bge-small-en-v1.5"),
            home: home)
    }

    /// Small heap, low idle footprint and Lucene's SIMD path (see docs/research); settings the user can edit live
    /// in `pounce.yml` next to the Index.
    var arguments: [String] {
        ["-Xmx256m", "-XX:+UseG1GC", "-XX:+UseCompactObjectHeaders", "-XX:G1PeriodicGCInterval=60000",
         "--add-modules", "jdk.incubator.vector", "--enable-native-access=ALL-UNNAMED",
         "-Dspring.config.additional-location=optional:file:\(home.appendingPathComponent("pounce.yml").path)",
         // The Engine exits when the Launcher does, even if the Launcher is killed outright.
         "-Dpounce.parent-pid=\(ProcessInfo.processInfo.processIdentifier)",
         "-jar", jar.path,
         "--pounce.home=\(home.path)",
         "--pounce.model-dir=\(modelDir.path)"]
    }

    /// The Engine makes no network calls: DJL stays offline with its telemetry off, natives cached under home.
    var environment: [String: String] {
        ["DJL_OFFLINE": "true", "OPT_OUT_TRACKING": "true",
         "DJL_CACHE_DIR": home.appendingPathComponent("cache/djl").path]
    }
}

/// A running Engine process.
@MainActor
public protocol EngineProcess: AnyObject {
    func terminate()
}

/// Starts processes; `onExit` is called on the main actor with the exit code.
@MainActor
public protocol ProcessStarter {
    func start(executable: URL, arguments: [String], environment: [String: String],
               onExit: @escaping @MainActor (Int32) -> Void) throws -> any EngineProcess
}

/// Answers whether an Engine is already listening (for example one left from an earlier run).
public protocol EngineProbe: Sendable {
    func isAlive() async -> Bool
}

/// Keeps the bundled Engine running while the Launcher runs: reuses one that is already answering, otherwise
/// starts it; restarts it once if it dies unexpectedly; stops it when asked.
@MainActor
public final class EngineSupervisor {

    public enum State: Equatable, Sendable {
        case stopped, reusing, running, failed(String)
    }

    public private(set) var state: State = .stopped
    private let layout: EngineLayout
    private let starter: any ProcessStarter
    private let probe: any EngineProbe
    private let maxRestarts: Int
    private var restarts = 0
    private var process: (any EngineProcess)?
    private var stopping = false

    public init(layout: EngineLayout, starter: any ProcessStarter, probe: any EngineProbe, maxRestarts: Int = 1) {
        self.layout = layout
        self.starter = starter
        self.probe = probe
        self.maxRestarts = maxRestarts
    }

    public func start() async {
        stopping = false
        if await probe.isAlive() {
            state = .reusing
            return
        }
        launch()
    }

    public func stop() {
        stopping = true
        process?.terminate()
        process = nil
        state = .stopped
    }

    private func launch() {
        do {
            process = try starter.start(executable: layout.java, arguments: layout.arguments,
                                        environment: layout.environment) { [weak self] code in
                self?.exited(code)
            }
            state = .running
        } catch {
            state = .failed("Pounce couldn't start its search engine: \(error.localizedDescription)")
        }
    }

    private func exited(_ code: Int32) {
        process = nil
        guard !stopping else { return }
        if restarts < maxRestarts {
            restarts += 1
            launch()
        } else {
            state = .failed("Pounce's search engine stopped unexpectedly (exit code \(code)).")
        }
    }
}

/// Starts real child processes with Foundation's Process; the child's output goes to a log file.
@MainActor
public final class FoundationProcessStarter: ProcessStarter {
    private let log: URL

    public init(log: URL) { self.log = log }

    public func start(executable: URL, arguments: [String], environment: [String: String],
                      onExit: @escaping @MainActor (Int32) -> Void) throws -> any EngineProcess {
        let process = Process()
        process.executableURL = executable
        process.arguments = arguments
        process.environment = ProcessInfo.processInfo.environment.merging(environment) { _, new in new }
        try FileManager.default.createDirectory(at: log.deletingLastPathComponent(), withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: log.path, contents: nil)
        let output = try FileHandle(forWritingTo: log)
        process.standardOutput = output
        process.standardError = output
        process.terminationHandler = { finished in
            let code = finished.terminationStatus
            Task { @MainActor in onExit(code) }
        }
        try process.run()
        return RunningProcess(process)
    }

    private final class RunningProcess: EngineProcess {
        private let process: Process
        init(_ process: Process) { self.process = process }
        func terminate() { if process.isRunning { process.terminate() } }
    }
}

/// An Engine is alive if its API answers at all (401 without a token counts).
public struct HTTPEngineProbe: EngineProbe {
    let url: URL

    public init(baseURL: URL) { url = baseURL.appendingPathComponent("search") }

    public func isAlive() async -> Bool {
        var request = URLRequest(url: url)
        request.timeoutInterval = 1
        guard let (_, response) = try? await URLSession.shared.data(for: request) else { return false }
        return (response as? HTTPURLResponse) != nil
    }
}
