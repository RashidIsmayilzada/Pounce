package dev.pounce.cli;

import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.DeserializationFeature;
import tools.jackson.databind.json.JsonMapper;

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
import java.util.List;
import java.util.Map;

/** Talks to the Engine: exchanges the bootstrap secret for a token, then reads one streamed search. */
final class EngineClient {

    record Highlight(int start, int end) {
    }

    record Snippet(String text, List<Highlight> highlights) {
    }

    record Result(String path, String name, Snippet snippet) {
    }

    private static final JsonMapper JSON = JsonMapper.builder()
            .disable(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES)
            .build();

    private final HttpClient http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(2)).build();
    private final URI engine;
    private final Path secretFile;

    EngineClient(URI engine, Path secretFile) {
        this.engine = engine;
        this.secretFile = secretFile;
    }

    /** The Final Results for this Query. */
    List<Result> search(String query, int limit) throws Problem, InterruptedException {
        String uri = engine + "/search?q=" + URLEncoder.encode(query, StandardCharsets.UTF_8) + "&limit=" + limit;
        HttpResponse<java.util.stream.Stream<String>> response = send(HttpRequest.newBuilder(URI.create(uri))
                        .header("Accept", "text/event-stream")
                        .header("Authorization", "Bearer " + token())
                        .timeout(Duration.ofSeconds(15))
                        .GET().build(),
                HttpResponse.BodyHandlers.ofLines());
        if (response.statusCode() != 200) {
            throw new Problem("Pounce answered the search with HTTP " + response.statusCode() + ".");
        }
        String event = "";
        for (String line : (Iterable<String>) response.body()::iterator) {
            if (line.startsWith("event:")) {
                event = line.substring("event:".length()).strip();
            } else if (line.startsWith("data:") && event.equals("final")) {
                return JSON.readValue(line.substring("data:".length()), new TypeReference<List<Result>>() { });
            }
        }
        return List.of();
    }

    private String token() throws Problem, InterruptedException {
        String secret;
        try {
            secret = Files.readString(secretFile).strip();
        } catch (IOException e) {
            throw new Problem("Can't read " + secretFile + ". Start Pounce once to create it.");
        }
        HttpResponse<String> response = send(HttpRequest.newBuilder(engine.resolve("/auth/token"))
                        .header("Content-Type", "application/json")
                        .POST(HttpRequest.BodyPublishers.ofString(JSON.writeValueAsString(Map.of("secret", secret))))
                        .build(),
                HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() == 401) {
            throw new Problem("Pounce refused the secret in " + secretFile + ". Is it from another Pounce install?");
        }
        if (response.statusCode() != 200) {
            throw new Problem("Pounce answered the login with HTTP " + response.statusCode() + ".");
        }
        return JSON.readTree(response.body()).get("token").asString();
    }

    private <T> HttpResponse<T> send(HttpRequest request, HttpResponse.BodyHandler<T> body)
            throws Problem, InterruptedException {
        try {
            return http.send(request, body);
        } catch (IOException e) {
            throw new Problem("Pounce isn't running at " + engine + ". Start Pounce and try again.");
        }
    }
}
