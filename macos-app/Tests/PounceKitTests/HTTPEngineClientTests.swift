import Foundation
import Network
import Testing
@testable import PounceKit

/// The real HTTP client against a stand-in Engine on a real local socket.
@Suite(.serialized)
struct HTTPEngineClientTests {

    @Test func streams_early_then_final_results() async throws {
        let engine = try await StandInEngine.start()
        engine.earlyJSON = #"[{"path":"/n/a.md","name":"a.md","snippet":{"text":"early","highlights":[]}}]"#
        engine.finalJSON = #"[{"path":"/n/b.md","name":"b.md","snippet":{"text":"the canary","highlights":[{"start":4,"end":10}]}}]"#

        let phases = try await collect(engine.client().search("canary", limit: 8))

        #expect(phases == [
            .early([ResultItem(path: "/n/a.md", name: "a.md", snippet: Snippet(text: "early", highlights: []))]),
            .final([ResultItem(path: "/n/b.md", name: "b.md",
                               snippet: Snippet(text: "the canary", highlights: [.init(start: 4, end: 10)]))]),
        ])
    }

    @Test func sends_the_query_and_limit_with_a_bearer_token_and_logs_in_only_once() async throws {
        let engine = try await StandInEngine.start()
        let client = engine.client()

        _ = try await collect(client.search("canary rollout", limit: 3))
        _ = try await collect(client.search("helm", limit: 8))

        #expect(engine.searches == ["q=canary rollout&limit=3", "q=helm&limit=8"])
        #expect(engine.tokenRequests == 1)
    }

    @Test func logs_in_again_when_the_token_has_expired() async throws {
        let engine = try await StandInEngine.start()
        let client = engine.client()
        _ = try await collect(client.search("canary", limit: 8))

        engine.expireTokens()
        let phases = try await collect(client.search("canary", limit: 8))

        #expect(engine.tokenRequests == 2)
        #expect(phases.count == 2)
    }

    @Test func says_pounce_is_not_running_when_nothing_listens() async throws {
        let client = HTTPEngineClient(baseURL: URL(string: "http://127.0.0.1:9")!, secretFile: try secretFile("s"))

        await #expect(throws: EngineError.notRunning(URL(string: "http://127.0.0.1:9")!)) {
            try await collect(client.search("canary", limit: 8))
        }
    }

    @Test func says_the_secret_file_is_missing() async throws {
        let engine = try await StandInEngine.start()
        let missing = FileManager.default.temporaryDirectory.appendingPathComponent("no-such-dir/bootstrap-secret")
        let client = HTTPEngineClient(baseURL: engine.url, secretFile: missing)

        await #expect(throws: EngineError.secretMissing(missing.path)) {
            try await collect(client.search("canary", limit: 8))
        }
    }

    @Test func says_the_secret_was_refused() async throws {
        let engine = try await StandInEngine.start()
        let wrong = try secretFile("a-stale-secret")
        let client = HTTPEngineClient(baseURL: engine.url, secretFile: wrong)

        await #expect(throws: EngineError.secretRefused(wrong.path)) {
            try await collect(client.search("canary", limit: 8))
        }
    }
}

// MARK: - helpers

func collect(_ stream: AsyncThrowingStream<SearchPhase, Error>) async throws -> [SearchPhase] {
    var phases: [SearchPhase] = []
    for try await phase in stream { phases.append(phase) }
    return phases
}

func secretFile(_ secret: String) throws -> URL {
    let file = FileManager.default.temporaryDirectory.appendingPathComponent("pounce-\(UUID().uuidString)-secret")
    try (secret + "\n").write(to: file, atomically: true, encoding: .utf8)
    return file
}

/// A tiny HTTP/1.1 server that behaves like the Engine: POST /auth/token, then GET /search as server-sent events.
final class StandInEngine: @unchecked Sendable {
    static let secret = "the-bootstrap-secret"

    private let listener: NWListener
    private let lock = NSLock()
    private var validTokens: Set<String> = []
    private var issued = 0
    private(set) var tokenRequests = 0
    private(set) var searches: [String] = []
    var earlyJSON = "[]"
    var finalJSON = "[]"
    private(set) var url = URL(string: "http://127.0.0.1")!

    private init() throws {
        listener = try NWListener(using: .tcp, on: .any)
    }

    static func start() async throws -> StandInEngine {
        let engine = try StandInEngine()
        engine.listener.newConnectionHandler = { engine.serve($0) }
        let port: UInt16 = try await withCheckedThrowingContinuation { ready in
            engine.listener.stateUpdateHandler = { state in
                switch state {
                case .ready: ready.resume(returning: engine.listener.port!.rawValue)
                case .failed(let error): ready.resume(throwing: error)
                default: break
                }
                if case .ready = state { engine.listener.stateUpdateHandler = nil }
            }
            engine.listener.start(queue: .global())
        }
        engine.url = URL(string: "http://127.0.0.1:\(port)")!
        return engine
    }

    func client() -> HTTPEngineClient {
        HTTPEngineClient(baseURL: url, secretFile: try! secretFile(Self.secret))
    }

    func expireTokens() { lock.withLock { validTokens.removeAll() } }

    private func serve(_ connection: NWConnection) {
        connection.start(queue: .global())
        receive(connection, buffer: Data())
    }

    private func receive(_ connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65_536) { data, _, done, _ in
            var buffer = buffer
            if let data { buffer.append(data) }
            if let request = Request(buffer) {
                self.respond(to: request, on: connection)
            } else if !done {
                self.receive(connection, buffer: buffer)
            } else {
                connection.cancel()
            }
        }
    }

    private func respond(to request: Request, on connection: NWConnection) {
        let (status, type, body): (Int, String, String) = lock.withLock {
            if request.path == "/auth/token" {
                tokenRequests += 1
                guard request.body.contains("\"\(Self.secret)\"") else { return (401, "text/plain", "") }
                issued += 1
                let token = "header.payload\(issued).signature"
                validTokens.insert(token)
                return (200, "application/json", #"{"token":"\#(token)","expiresInSeconds":900}"#)
            }
            guard let auth = request.headers["authorization"], validTokens.contains(String(auth.dropFirst("Bearer ".count))) else {
                return (401, "text/plain", "")
            }
            searches.append(request.query.removingPercentEncoding ?? request.query)
            return (200, "text/event-stream", "event:early\ndata:\(earlyJSON)\n\nevent:final\ndata:\(finalJSON)\n\n")
        }
        let bytes = Data(body.utf8)
        let head = "HTTP/1.1 \(status) X\r\nContent-Type: \(type)\r\nContent-Length: \(bytes.count)\r\nConnection: close\r\n\r\n"
        connection.send(content: Data(head.utf8) + bytes, completion: .contentProcessed { _ in connection.cancel() })
    }

    /// A parsed request, or nil while more bytes are needed.
    private struct Request {
        let path: String
        let query: String
        let headers: [String: String]
        let body: String

        init?(_ data: Data) {
            guard let text = String(data: data, encoding: .utf8), let split = text.range(of: "\r\n\r\n") else { return nil }
            let lines = text[..<split.lowerBound].components(separatedBy: "\r\n")
            var headers: [String: String] = [:]
            for line in lines.dropFirst() {
                guard let colon = line.firstIndex(of: ":") else { continue }
                headers[line[..<colon].lowercased()] = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            }
            let body = String(text[split.upperBound...])
            guard body.utf8.count >= Int(headers["content-length"] ?? "0") ?? 0 else { return nil }
            let target = lines[0].split(separator: " ")[1]
            let parts = target.split(separator: "?", maxSplits: 1)
            self.path = String(parts[0])
            self.query = parts.count > 1 ? String(parts[1]).replacingOccurrences(of: "+", with: " ") : ""
            self.headers = headers
            self.body = body
        }
    }
}
