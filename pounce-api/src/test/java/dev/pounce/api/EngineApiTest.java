package dev.pounce.api;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIf;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.regex.Pattern;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.boot.test.context.SpringBootTest.WebEnvironment.RANDOM_PORT;

/**
 * The Engine over real HTTP, the way the CLI and Launcher use it: real model, real Lucene index on disk,
 * a Watched Folder that already holds a file when the Engine starts.
 */
@SpringBootTest(webEnvironment = RANDOM_PORT)
@EnabledIf("modelIsPresent")
class EngineApiTest {

    static final Path MODEL = Path.of(System.getProperty("pounce.model.dir", "../models/bge-small-en-v1.5"));
    static final Path HOME = tempDirectory("pounce-home");
    static final Path WATCHED = tempDirectory("pounce-watched");

    final HttpClient http = HttpClient.newHttpClient();

    @Value("${local.server.port}")
    int port;

    static boolean modelIsPresent() {
        return Files.exists(MODEL.resolve("model.onnx"));
    }

    @DynamicPropertySource
    static void engine(DynamicPropertyRegistry properties) throws IOException {
        Files.writeString(WATCHED.resolve("rollout-notes.md"), "# Deploys\nThe canary rollout went fine.");
        properties.add("pounce.home", HOME::toString);
        properties.add("pounce.watched-folders", WATCHED::toString);
        properties.add("pounce.model-dir", MODEL::toString);
    }

    @Test
    void searching_without_a_token_is_refused() throws Exception {
        assertThat(search("canary", null).statusCode()).isEqualTo(401);
    }

    @Test
    void a_forged_token_is_refused() throws Exception {
        assertThat(search("canary", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJtZSJ9.c2lnbmF0dXJl").statusCode()).isEqualTo(401);
    }

    @Test
    void a_wrong_bootstrap_secret_gets_no_token() throws Exception {
        assertThat(requestToken("not-the-secret").statusCode()).isEqualTo(401);
    }

    @Test
    void the_bootstrap_secret_is_exchanged_for_a_token() throws Exception {
        HttpResponse<String> response = requestToken(Files.readString(HOME.resolve("bootstrap-secret")).strip());

        assertThat(response.statusCode()).isEqualTo(200);
        assertThat(response.body()).containsPattern("\"token\"\\s*:\\s*\"[\\w-]+\\.[\\w-]+\\.[\\w-]+\"");
    }

    @Test
    void a_file_on_disk_when_the_engine_starts_becomes_searchable() throws Exception {
        String token = token();
        Instant deadline = Instant.now().plusSeconds(20);
        String body = "";
        while (Instant.now().isBefore(deadline) && !body.contains("rollout-notes.md")) {
            body = search("canary", token).body();
            Thread.sleep(200);
        }
        assertThat(body).contains("rollout-notes.md");
    }

    @Test
    void a_file_saved_while_the_engine_runs_becomes_searchable() throws Exception {
        a_file_on_disk_when_the_engine_starts_becomes_searchable();
        String token = token();

        Files.writeString(WATCHED.resolve("falconry.md"), "# Falconry\nThe falcon returned to the glove.");

        Instant deadline = Instant.now().plusSeconds(10);
        String body = "";
        while (Instant.now().isBefore(deadline) && !body.contains("falconry.md")) {
            body = search("falcon glove", token).body();
            Thread.sleep(200);
        }
        assertThat(body).contains("falconry.md");
    }

    @Test
    void a_search_streams_early_results_then_final_results() throws Exception {
        a_file_on_disk_when_the_engine_starts_becomes_searchable();

        HttpResponse<String> response = search("canary", token());

        assertThat(response.statusCode()).isEqualTo(200);
        assertThat(response.headers().firstValue("Content-Type")).hasValueSatisfying(
                type -> assertThat(type).startsWith("text/event-stream"));
        String body = response.body();
        int early = body.indexOf("event:early");
        int last = body.indexOf("event:final");
        assertThat(early).isNotNegative();
        assertThat(last).isGreaterThan(early);
        assertThat(body.substring(early, last)).contains("rollout-notes.md").contains("canary");
        assertThat(body.substring(last)).contains("rollout-notes.md");
    }

    private String token() throws Exception {
        String body = requestToken(Files.readString(HOME.resolve("bootstrap-secret")).strip()).body();
        var matcher = Pattern.compile("\"token\"\\s*:\\s*\"([^\"]+)\"").matcher(body);
        assertThat(matcher.find()).as("token in %s", body).isTrue();
        return matcher.group(1);
    }

    private HttpResponse<String> requestToken(String secret) throws Exception {
        return http.send(HttpRequest.newBuilder(uri("/auth/token"))
                        .header("Content-Type", "application/json")
                        .POST(HttpRequest.BodyPublishers.ofString("{\"secret\":\"" + secret + "\"}"))
                        .build(),
                HttpResponse.BodyHandlers.ofString());
    }

    private HttpResponse<String> search(String query, String token) throws Exception {
        var request = HttpRequest.newBuilder(uri("/search?q=" + URLEncoder.encode(query, StandardCharsets.UTF_8)))
                .header("Accept", "text/event-stream")
                .timeout(Duration.ofSeconds(10));
        if (token != null) {
            request.header("Authorization", "Bearer " + token);
        }
        return http.send(request.GET().build(), HttpResponse.BodyHandlers.ofString());
    }

    private URI uri(String path) {
        return URI.create("http://127.0.0.1:" + port + path);
    }

    private static Path tempDirectory(String prefix) {
        try {
            return Files.createTempDirectory(prefix).toRealPath();
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
    }
}
