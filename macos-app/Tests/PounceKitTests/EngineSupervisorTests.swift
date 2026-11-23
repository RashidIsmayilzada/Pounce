import Foundation
import Testing
@testable import PounceKit

/// The Launcher keeps the bundled Engine running: start it, reuse one already running, restart once, stop on quit.
@MainActor
struct EngineSupervisorTests {

    let layout = EngineLayout(
        java: URL(fileURLWithPath: "/Applications/Pounce.app/Contents/Resources/runtime/bin/java"),
        jar: URL(fileURLWithPath: "/Applications/Pounce.app/Contents/Resources/engine/pounce-engine.jar"),
        modelDir: URL(fileURLWithPath: "/Applications/Pounce.app/Contents/Resources/models/bge-small-en-v1.5"),
        home: URL(fileURLWithPath: "/Users/me/Library/Application Support/Pounce"))
    let starter = FakeStarter()

    @Test func starts_the_bundled_engine_when_none_is_running() async {
        let supervisor = EngineSupervisor(layout: layout, starter: starter, probe: FakeProbe(alive: false))

        await supervisor.start()

        #expect(supervisor.state == .running)
        #expect(starter.started.count == 1)
        let launch = starter.started[0]
        #expect(launch.executable == layout.java)
        #expect(launch.arguments.suffix(4) == [
            "-jar", layout.jar.path,
            "--pounce.home=/Users/me/Library/Application Support/Pounce",
            "--pounce.model-dir=\(layout.modelDir.path)",
        ])
        #expect(launch.environment["DJL_OFFLINE"] == "true")
        #expect(launch.environment["OPT_OUT_TRACKING"] == "true")
    }

    @Test func reuses_an_engine_that_is_already_running() async {
        let supervisor = EngineSupervisor(layout: layout, starter: starter, probe: FakeProbe(alive: true))

        await supervisor.start()

        #expect(supervisor.state == .reusing)
        #expect(starter.started.isEmpty)
    }

    @Test func restarts_the_engine_once_after_a_crash_then_reports_it() async {
        let supervisor = EngineSupervisor(layout: layout, starter: starter, probe: FakeProbe(alive: false))
        await supervisor.start()

        starter.processes[0].exit(1)
        #expect(starter.started.count == 2)
        #expect(supervisor.state == .running)

        starter.processes[1].exit(1)
        #expect(starter.started.count == 2)
        #expect(supervisor.state == .failed("Pounce's search engine stopped unexpectedly (exit code 1)."))
    }

    @Test func stopping_terminates_the_engine_without_restarting_it() async {
        let supervisor = EngineSupervisor(layout: layout, starter: starter, probe: FakeProbe(alive: false))
        await supervisor.start()

        supervisor.stop()
        starter.processes[0].exit(143)

        #expect(starter.processes[0].terminated)
        #expect(starter.started.count == 1)
        #expect(supervisor.state == .stopped)
    }
}

// MARK: - fakes

struct FakeProbe: EngineProbe {
    let alive: Bool
    func isAlive() async -> Bool { alive }
}

@MainActor
final class FakeStarter: ProcessStarter {
    struct Launch {
        let executable: URL
        let arguments: [String]
        let environment: [String: String]
    }

    private(set) var started: [Launch] = []
    private(set) var processes: [FakeProcess] = []

    func start(executable: URL, arguments: [String], environment: [String: String],
               onExit: @escaping @MainActor (Int32) -> Void) throws -> any EngineProcess {
        started.append(Launch(executable: executable, arguments: arguments, environment: environment))
        let process = FakeProcess(onExit: onExit)
        processes.append(process)
        return process
    }
}

@MainActor
final class FakeProcess: EngineProcess {
    private let onExit: @MainActor (Int32) -> Void
    private(set) var terminated = false

    init(onExit: @escaping @MainActor (Int32) -> Void) { self.onExit = onExit }

    func terminate() { terminated = true }
    func exit(_ code: Int32) { onExit(code) }
}
