package dev.pounce.cli;

import com.sun.net.httpserver.HttpServer;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.io.PrintWriter;
import java.io.StringWriter;
import java.net.InetSocketAddress;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/** The `pounce` command against a stand-in Engine that speaks the same HTTP: token exchange, then an SSE search. */
class PounceTest {

    static final String SECRET = "the-bootstrap-secret";
    static final String TOKEN = "header.payload.signature";

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
            boolean ok = body.contains("\"" + SECRET + "\"");
            byte[] reply = ok ? ("{\"token\":\"" + TOKEN + "\",\"expiresInSeconds\":900}").getBytes() : new byte[0];
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
            byte[] stream = ("event:early\ndata:" + earlyJson + "\n\nevent:final\ndata:" + finalJson + "\n\n").getBytes();
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
        earlyJson = "[" + result("/notes/old-guess.md", "An early guess.", List.of()) + "]";
        finalJson = "[" + result("/notes/rollout.md", "The canary rollout went fine.", List.of(4, 10)) + ","
                + result("/notes/runbook.md", "Shift traffic slowly.", List.of()) + "]";

        Run run = pounce("--color=never", "canary");

        assertThat(run.exitCode).isZero();
        assertThat(run.out).isEqualTo("""
                rollout.md  /notes/rollout.md
                  The canary rollout went fine.
                runbook.md  /notes/runbook.md
                  Shift traffic slowly.
                """);
    }

    @Test
    void highlights_matched_words_in_bold_on_a_colour_terminal() {
        finalJson = "[" + result("/notes/rollout.md", "The canary rollout went fine.", List.of(4, 10)) + "]";

        Run run = pounce("--color=always", "canary");

        assertThat(run.out).contains("The \u001B[1mcanary\u001B[22m rollout went fine.");
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
        assertThat(run.err).isEqualTo("No results for \"zebra\".\n");
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
        assertThat(run.err).isEqualTo("Can't read " + home.resolve("bootstrap-secret")
                + ". Start Pounce once to create it.\n");
    }

    @Test
    void explains_it_when_the_engine_refuses_the_secret() throws IOException {
        Files.writeString(home.resolve("bootstrap-secret"), "a-stale-secret\n");

        Run run = pounce("canary");

        assertThat(run.exitCode).isEqualTo(2);
        assertThat(run.err).isEqualTo("Pounce refused the secret in " + home.resolve("bootstrap-secret")
                + ". Is it from another Pounce install?\n");
    }

    // ------------------------------------------------------------------ helpers

    record Run(int exitCode, String out, String err) {
    }

    Run pounce(String... args) {
        var out = new StringWriter();
        var err = new StringWriter();
        var environment = Map.of(
                "POUNCE_URL", "http://127.0.0.1:" + engine.getAddress().getPort(),
                "POUNCE_HOME", home.toString());
        int exitCode = Pounce.commandLine(environment, new PrintWriter(out), new PrintWriter(err)).execute(args);
        return new Run(exitCode, out.toString(), err.toString());
    }

    /** One Result as the Engine sends it; highlights are start/end pairs. */
    static String result(String path, String snippet, List<Integer> highlights) {
        var ranges = new ArrayList<String>();
        for (int i = 0; i < highlights.size(); i += 2) {
            ranges.add("{\"start\":" + highlights.get(i) + ",\"end\":" + highlights.get(i + 1) + "}");
        }
        String name = Path.of(path).getFileName().toString();
        return "{\"path\":\"" + path + "\",\"name\":\"" + name + "\",\"snippet\":{\"text\":\"" + snippet
                + "\",\"highlights\":[" + String.join(",", ranges) + "]}}";
    }
}
