package dev.pounce.api;

import dev.pounce.application.Phase;
import dev.pounce.application.Searcher;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import dev.pounce.domain.Snippet;
import org.springframework.http.MediaType;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.servlet.mvc.method.annotation.SseEmitter;

import java.io.IOException;
import java.util.List;

/**
 * One streamed request per Query: an {@code early} event with Early Results, then a {@code final} event with
 * Final Results. A client that types another key just closes the connection.
 */
@RestController
class SearchController {

    record ResultView(String path, String name, Snippet snippet) {
        static ResultView of(Result result) {
            return new ResultView(result.path().toString(), result.path().getFileName().toString(), result.snippet());
        }
    }

    private static final long TIMEOUT_MILLIS = 30_000;

    private final Searcher searcher;

    SearchController(Searcher searcher) {
        this.searcher = searcher;
    }

    @GetMapping(path = "/search", produces = MediaType.TEXT_EVENT_STREAM_VALUE)
    SseEmitter search(@RequestParam("q") String text,
                      @RequestParam(name = "limit", defaultValue = "8") int limit) {
        var emitter = new SseEmitter(TIMEOUT_MILLIS);
        var query = new Query(text, Math.clamp(limit, 1, 50));
        Thread.ofVirtual().name("search").start(() -> {
            try {
                send(emitter, "early", searcher.search(query, Phase.EARLY));
                send(emitter, "final", searcher.search(query, Phase.FINAL));
                emitter.complete();
            } catch (IOException | IllegalStateException clientGone) {
                // The client moved on to a newer Query; nothing to clean up.
            } catch (RuntimeException e) {
                emitter.completeWithError(e);
            }
        });
        return emitter;
    }

    private static void send(SseEmitter emitter, String phase, List<Result> results) throws IOException {
        emitter.send(SseEmitter.event().name(phase)
                .data(results.stream().map(ResultView::of).toList(), MediaType.APPLICATION_JSON));
    }
}
