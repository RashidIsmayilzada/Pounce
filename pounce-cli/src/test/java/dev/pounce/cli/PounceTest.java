package dev.pounce.cli;

import com.sun.net.httpserver.HttpServer;
import dev.pounce.platform.PounceHome;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import tools.jackson.databind.json.JsonMapper;

import java.io.IOException;
import java.io.PrintWriter;
import java.io.StringWriter;
import java.net.InetSocketAddress;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/** The `pounce` command against a stand-in Engine that speaks the same HTTP: token exchange, then an SSE search. */
class PounceTest {

    static final String SECRET = "the-bootstrap-secret";
    static final String TOKEN = "header.payload.signature";
    static final JsonMapper JSON = JsonMapper.builder().build();

    @TempDir
    Path home;
    HttpServer engine;
    final List<String> searches = new ArrayList<>();
    String earlyJson = "[]";
    String finalJson = "[]";

    @BeforeEach
    void startStandInEngine() throws IOException {
        Files.writeString(home.resolve("bootstrap-secret"), SECRET + "\n");
        engine = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        engine.createContext("/auth/token", exchange -> {
            String body = new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
            boolean ok = SECRET.equals(JSON.readTree(body).path("secret").asString());
            byte[] reply = ok ? JSON.writeValueAsBytes(Map.of("token", TOKEN, "expiresInSeconds", 900)) : new byte[0];
            exchange.sendResponseHeaders(ok ? 200 : 401, ok ? reply.length : -1);
            exchange.getResponseBody().write(reply);
            exchange.close();
        });
        engine.createContext("/search", exchange -> {
            if (!("Bearer " + TOKEN).equals(exchange.getRequestHeaders().getFirst("Authorization"))) {
                exchange.sendResponseHeaders(401, -1);
                exchange.close();
                return;
            }
            searches.add(URLDecoder.decode(exchange.getRequestURI().getRawQuery(), StandardCharsets.UTF_8));
            byte[] stream = ("event:early\ndata:" + earlyJson + "\n\nevent:final\ndata:" + finalJson + "\n\n")
                    .getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().add("Content-Type", "text/event-stream");
            exchange.sendResponseHeaders(200, stream.length);
            exchange.getResponseBody().write(stream);
            exchange.close();
        });
        engine.start();
    }

    @AfterEach
    void stop() {
        engine.stop(0);
    }

    @Test
    void prints_the_final_results_with_their_snippets() {
        earlyJson = json(result("/notes/old-guess.md", "An early guess."));
        finalJson = json(result("/notes/rollout.md", "The canary rollout went fine.", 4, 10),
                result("/notes/runbook.md", "Shift traffic slowly."));

        Run run = pounce("--color=never", "canary");

        assertThat(run.exitCode).isZero();
        assertThat(run.out).isEqualToNormalizingNewlines("""
                rollout.md  /notes/rollout.md
                  The canary rollout went fine.
                runbook.md  /notes/runbook.md
                  Shift traffic slowly.
                """);
    }

    @Test
    void prints_a_windows_path_unchanged() {
        finalJson = json(result("C:\\Users\\me\\notes.md", "Notes on \"quoting\"."));

        Run run = pounce("--color=never", "notes");

        assertThat(run.out).isEqualToNormalizingNewlines("""
                notes.md  C:\\Users\\me\\notes.md
                  Notes on "quoting".
                """);
    }

    @Test
    void highlights_matched_words_in_bold_on_a_colour_terminal() {
        finalJson = json(result("/notes/rollout.md", "The canary rollout went fine.", 4, 10));

        Run run = pounce("--color=always", "canary");

        assertThat(run.out).contains("The \u001B[1mcanary\u001B[22m rollout went fine.");
    }

    @Test
    void does_not_colour_output_that_is_not_a_terminal() {
        finalJson = json(result("/notes/rollout.md", "The canary rollout went fine.", 4, 10));

        Run run = pounce("canary");

        assertThat(run.out).contains("The canary rollout went fine.").doesNotContain("\u001B");
    }

    @Test
    void sends_all_the_words_and_the_limit() {
        pounce("-n", "3", "canary", "rollout");

        assertThat(searches).containsExactly("q=canary rollout&limit=3");
    }

    @Test
    void says_so_and_exits_with_1_when_nothing_matches() {
        Run run = pounce("zebra");

        assertThat(run.exitCode).isEqualTo(1);
        assertThat(run.err).isEqualToNormalizingNewlines("No results for \"zebra\".\n");
    }

    @Test
    void reads_the_bootstrap_secret_from_the_usual_home_for_this_system() throws IOException {
        Path userHome = home.resolve("user");
        var environment = Map.of("POUNCE_URL", "http://127.0.0.1:" + engine.getAddress().getPort());
        Path secret = PounceHome.resolve(System.getProperty("os.name"), environment, userHome).bootstrapSecret();
        Files.createDirectories(secret.getParent());
        Files.writeString(secret, SECRET + "\n");
        finalJson = json(result("/notes/rollout.md", "The canary rollout went fine."));

        Run run = pounce(environment, userHome, "--color=never", "canary");

        assertThat(run.err).isEmpty();
        assertThat(run.out).startsWith("rollout.md  /notes/rollout.md");
    }

    @Test
    void explains_how_to_fix_it_when_the_engine_is_not_running() {
        engine.stop(0);

        Run run = pounce("canary");

        assertThat(run.exitCode).isEqualTo(2);
        assertThat(run.err).startsWith("Pounce isn't running at http://127.0.0.1:").contains("Start Pounce");
    }

    @Test
    void explains_how_to_fix_it_when_the_bootstrap_secret_is_missing() throws IOException {
        Files.delete(home.resolve("bootstrap-secret"));

        Run run = pounce("canary");

        assertThat(run.exitCode).isEqualTo(2);
        assertThat(run.err).isEqualToNormalizingNewlines("Can't read " + home.resolve("bootstrap-secret")
                + ". Start Pounce once to create it.\n");
    }

    @Test
    void explains_it_when_the_engine_refuses_the_secret() throws IOException {
        Files.writeString(home.resolve("bootstrap-secret"), "a-stale-secret\n");

        Run run = pounce("canary");

        assertThat(run.exitCode).isEqualTo(2);
        assertThat(run.err).isEqualToNormalizingNewlines("Pounce refused the secret in "
                + home.resolve("bootstrap-secret") + ". Is it from another Pounce install?\n");
    }

    // ------------------------------------------------------------------ helpers

    record Run(int exitCode, String out, String err) {
    }

    /** Runs the command with POUNCE_HOME set to this test's temporary folder. */
    Run pounce(String... args) {
        var environment = Map.of(
                "POUNCE_URL", "http://127.0.0.1:" + engine.getAddress().getPort(),
                "POUNCE_HOME", home.toString());
        return pounce(environment, home.resolve("user"), args);
    }

    Run pounce(Map<String, String> environment, Path userHome, String... args) {
        var out = new StringWriter();
        var err = new StringWriter();
        int exitCode = Pounce.commandLine(environment, userHome, new PrintWriter(out), new PrintWriter(err))
                .execute(args);
        return new Run(exitCode, out.toString(), err.toString());
    }

    /** One Result as the Engine sends it; highlights are start/end pairs. */
    static Map<String, Object> result(String path, String snippet, int... highlights) {
        var ranges = new ArrayList<Map<String, Integer>>();
        for (int i = 0; i < highlights.length; i += 2) {
            ranges.add(Map.of("start", highlights[i], "end", highlights[i + 1]));
        }
        var result = new HashMap<String, Object>();
        result.put("path", path);
        result.put("name", path.substring(Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\')) + 1));
        result.put("snippet", Map.of("text", snippet, "highlights", ranges));
        return result;
    }

    @SafeVarargs
    static String json(Map<String, Object>... results) {
        return JSON.writeValueAsString(List.of(results));
    }
}
