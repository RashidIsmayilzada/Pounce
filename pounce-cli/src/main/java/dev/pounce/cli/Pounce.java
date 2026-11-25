package dev.pounce.cli;

import dev.pounce.platform.PounceHome;
import picocli.CommandLine;
import picocli.CommandLine.Command;
import picocli.CommandLine.Option;
import picocli.CommandLine.Parameters;

import java.io.Console;
import java.io.PrintWriter;
import java.net.URI;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.concurrent.Callable;

/** {@code pounce <words…>}: search your files through the running Pounce Engine. */
@Command(name = "pounce", mixinStandardHelpOptions = true, version = "pounce 0.1.0",
        description = "Search your files by what they say, through the Pounce Engine running on this computer.")
public final class Pounce implements Callable<Integer> {

    enum Color { auto, always, never }

    @Parameters(arity = "1..*", paramLabel = "WORDS", description = "What to search for.")
    List<String> words;

    @Option(names = {"-n", "--limit"}, defaultValue = "8", description = "Most Results to show (default: ${DEFAULT-VALUE}).")
    int limit;

    @Option(names = "--color", defaultValue = "auto", description = "Highlight matched words: ${COMPLETION-CANDIDATES}.")
    Color color;

    private final Map<String, String> environment;
    private final Path userHome;
    private final PrintWriter out;
    private final PrintWriter err;

    private Pounce(Map<String, String> environment, Path userHome, PrintWriter out, PrintWriter err) {
        this.environment = environment;
        this.userHome = userHome;
        this.out = out;
        this.err = err;
    }

    public static void main(String[] args) {
        var out = new PrintWriter(System.out, true);
        var err = new PrintWriter(System.err, true);
        System.exit(commandLine(System.getenv(), Path.of(System.getProperty("user.home")), out, err).execute(args));
    }

    /**
     * The command wired to an environment (POUNCE_URL, POUNCE_HOME, NO_COLOR, and the variables {@link PounceHome}
     * reads), a user home and output streams; tests use this too.
     */
    static CommandLine commandLine(Map<String, String> environment, Path userHome, PrintWriter out, PrintWriter err) {
        var command = new CommandLine(new Pounce(environment, userHome, out, err));
        command.setOut(out);
        command.setErr(err);
        return command;
    }

    @Override
    public Integer call() throws InterruptedException {
        String query = String.join(" ", words);
        var client = new EngineClient(engineUrl(),
                PounceHome.resolve(System.getProperty("os.name"), environment, userHome).bootstrapSecret());
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

    private boolean useColor() {
        return switch (color) {
            case always -> true;
            case never -> false;
            // No colour when output is piped or redirected; NO_COLOR is the usual opt-out.
            case auto -> isTerminal(System.console()) && !environment.containsKey("NO_COLOR");
        };
    }

    /**
     * On Java 21 there is a console only on a terminal. Later versions can also give one when output is redirected,
     * and have {@code Console.isTerminal()} to tell them apart; called by name because this command is built for 21.
     */
    private static boolean isTerminal(Console console) {
        if (console == null) {
            return false;
        }
        try {
            return (Boolean) Console.class.getMethod("isTerminal").invoke(console);
        } catch (NoSuchMethodException java21) {
            return true;
        } catch (ReflectiveOperationException | RuntimeException e) {
            return false;
        }
    }
}
