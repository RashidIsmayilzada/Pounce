import Foundation

/// Talks to the Engine over its localhost REST API: exchanges the bootstrap secret for a JWT (kept until the
/// Engine refuses it, then fetched again once), and reads each search as a stream of `early` then `final` events.
public final class HTTPEngineClient: SearchEngine {

    public let baseURL: URL
    private let secretFile: URL
    private let session: URLSession
    private let tokens = TokenStore()

    public init(baseURL: URL, secretFile: URL, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.secretFile = secretFile
        self.session = session
    }

    /// The default location: `~/Library/Application Support/Pounce/bootstrap-secret`, Engine on port 7431.
    public static func local() -> HTTPEngineClient {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return HTTPEngineClient(baseURL: URL(string: "http://127.0.0.1:7431")!,
                                secretFile: support.appendingPathComponent("Pounce/bootstrap-secret"))
    }

    public func search(_ query: String, limit: Int) -> AsyncThrowingStream<SearchPhase, Error> {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    try await self.stream(query, limit: limit, into: continuation, mayRetry: true)
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: self.explain(error))
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    private func stream(_ query: String, limit: Int,
                        into continuation: AsyncThrowingStream<SearchPhase, Error>.Continuation,
                        mayRetry: Bool) async throws {
        var components = URLComponents(url: baseURL.appendingPathComponent("search"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "q", value: query), URLQueryItem(name: "limit", value: String(limit))]
        var request = URLRequest(url: components.url!)
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        request.setValue("Bearer \(try await token())", forHTTPHeaderField: "Authorization")

        let (bytes, response) = try await session.bytes(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 401 && mayRetry {
            await tokens.forget()
            return try await stream(query, limit: limit, into: continuation, mayRetry: false)
        }
        guard status == 200 else { throw EngineError.unexpected(status: status) }

        var event = ""
        for try await line in bytes.lines {
            if line.hasPrefix("event:") {
                event = line.dropFirst("event:".count).trimmingCharacters(in: .whitespaces)
            } else if line.hasPrefix("data:") {
                let items = try JSONDecoder().decode([ResultItem].self, from: Data(line.dropFirst("data:".count).utf8))
                continuation.yield(event == "final" ? .final(items) : .early(items))
                if event == "final" { return }
            }
        }
    }

    private func token() async throws -> String {
        if let cached = await tokens.current { return cached }
        let secret: String
        do {
            secret = try String(contentsOf: secretFile, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines)
        } catch {
            throw EngineError.secretMissing(secretFile.path)
        }
        var request = URLRequest(url: baseURL.appendingPathComponent("auth/token"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(["secret": secret])
        let (data, response) = try await session.data(for: request)
        switch (response as? HTTPURLResponse)?.statusCode ?? 0 {
        case 200:
            let token = try JSONDecoder().decode(TokenResponse.self, from: data).token
            await tokens.remember(token)
            return token
        case 401:
            throw EngineError.secretRefused(secretFile.path)
        case let status:
            throw EngineError.unexpected(status: status)
        }
    }

    private func explain(_ error: Error) -> Error {
        if let urlError = error as? URLError,
           [.cannotConnectToHost, .cannotFindHost, .networkConnectionLost, .timedOut, .notConnectedToInternet]
               .contains(urlError.code) {
            return EngineError.notRunning(baseURL)
        }
        return error
    }

    private struct TokenResponse: Decodable {
        let token: String
    }

    private actor TokenStore {
        private(set) var current: String?
        func remember(_ token: String) { current = token }
        func forget() { current = nil }
    }
}
