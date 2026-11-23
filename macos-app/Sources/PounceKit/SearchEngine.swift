import Foundation

/// Anything that can answer a Query as a stream of Early then Final Results.
public protocol SearchEngine: Sendable {
    func search(_ query: String, limit: Int) -> AsyncThrowingStream<SearchPhase, Error>
}

/// Problems the user can fix, with a message that says how.
public enum EngineError: Error, Equatable, LocalizedError {
    case notRunning(URL)
    case secretMissing(String)
    case secretRefused(String)
    case unexpected(status: Int)

    public var errorDescription: String? {
        switch self {
        case .notRunning(let url):
            "Pounce isn't running at \(url.absoluteString). Start Pounce and try again."
        case .secretMissing(let path):
            "Can't read \(path). Start Pounce once to create it."
        case .secretRefused(let path):
            "Pounce refused the secret in \(path). Is it from another Pounce install?"
        case .unexpected(let status):
            "Pounce answered with HTTP \(status)."
        }
    }
}
