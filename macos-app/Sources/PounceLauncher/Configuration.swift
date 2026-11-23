import Foundation
import PounceKit

/// Where the Engine is: POUNCE_URL and POUNCE_HOME override the defaults, as for the `pounce` command.
enum Configuration {
    static func engine() -> HTTPEngineClient {
        let environment = ProcessInfo.processInfo.environment
        let defaults = HTTPEngineClient.local()
        let url = environment["POUNCE_URL"].flatMap(URL.init(string:)) ?? defaults.baseURL
        guard let home = environment["POUNCE_HOME"] else {
            return HTTPEngineClient(baseURL: url, secretFile: defaultSecret())
        }
        return HTTPEngineClient(baseURL: url, secretFile: URL(fileURLWithPath: home).appendingPathComponent("bootstrap-secret"))
    }

    private static func defaultSecret() -> URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Pounce/bootstrap-secret")
    }
}
