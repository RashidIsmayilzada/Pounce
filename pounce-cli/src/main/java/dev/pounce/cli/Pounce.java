package dev.pounce.cli;

import picocli.CommandLine;
import picocli.CommandLine.Command;
import picocli.CommandLine.Option;
import picocli.CommandLine.Parameters;

import java.io.PrintWriter;
import java.net.URI;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.concurrent.Callable;

/** {@code pounce <words…>}: search your Mac through the running Pounce Engine. */
@Command(name = "pounce", mixinStandardHelpOptions = true, version = "pounce 0.1.0",
        description = "Search your files by what they say, through the Pounce Engine running on this Mac.")
public final class Pounce implements Callable<Integer> {

    enum Color { auto, always, never }

    @Parameters(arity = "1..*", paramLabel = "WORDS", description = "What to search for.")
    List<String> words;

    @Option(names = {"-n", "--limit"}, defaultValue = "8", description = "Most Results to show (default: ${DEFAULT-VALUE}).")
    int limit;

    @Option(names = "--color", defaultValue = "auto", description = "Highlight matched words: ${COMPLETION-CANDIDATES}.")
    Color color;

    private final Map<String, String> environment;
    private final PrintWriter out;
    private final PrintWriter err;

    private Pounce(Map<String, String> environment, PrintWriter out, PrintWriter err) {
        this.environment = environment;
        this.out = out;
        this.err = err;
    }

    public static void main(String[] args) {
        var out = new PrintWriter(System.out, true);
        var err = new PrintWriter(System.err, true);
        System.exit(commandLine(System.getenv(), out, err).execute(args));
    }

    /** The command wired to an environment (POUNCE_URL, POUNCE_HOME) and output streams; tests use this too. */
    static CommandLine commandLine(Map<String, String> environment, PrintWriter out, PrintWriter err) {
        var command = new CommandLine(new Pounce(environment, out, err));
        command.setOut(out);
        command.setErr(err);
        return command;
    }

    @Override
    public Integer call() throws InterruptedException {
        String query = String.join(" ", words);
        var client = new EngineClient(engineUrl(), pounceHome().resolve("bootstrap-secret"));
        try {
            List<EngineClient.Result> results = client.search(query, limit);
            if (results.isEmpty()) {
                err.println("No results for \"" + query + "\".");
                return 1;
            }
            new ResultPrinter(useColor()).print(results, out);
            return 0;
        } catch (Problem problem) {
            err.println(problem.getMessage());
            return 2;
        } finally {
            out.flush();
            err.flush();
        }
    }

    private URI engineUrl() {
        return URI.create(environment.getOrDefault("POUNCE_URL", "http://127.0.0.1:7431"));
    }

    private Path pounceHome() {
        String home = environment.get("POUNCE_HOME");
        return home != null ? Path.of(home)
                : Path.of(System.getProperty("user.home"), "Library", "Application Support", "Pounce");
    }

    private boolean useColor() {
        return switch (color) {
            case always -> true;
            case never -> false;
            // On Java 21 the console is null when output is piped or redirected; NO_COLOR is the usual opt-out.
            case auto -> System.console() != null && !environment.containsKey("NO_COLOR");
        };
    }
}
