package dev.pounce.infrastructure;

import dev.pounce.application.Phase;
import dev.pounce.domain.Query;
import dev.pounce.domain.Result;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Disabled;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * Ranking quality with the real model: for each labelled Query, the expected Document must be the Top Result.
 * The corpus includes a note stuffed with filler words, the way real notes are.
 */
class RankingTest {

    /** Everyday notes that share only filler words with the Queries, like most of a real disk. */
    static final Map<String, String> FILLER = Map.of(
            "standup.md", "How do we unblock the other team? My plan: do the review, then how do I sync the files?",
            "reading-list.md", "Books I am reading: how other people do deep work. My files of notes on how to stop procrastinating.",
            "onboarding.md", "How do I get access? My laptop: do the setup, copy the other files, stop the old VPN.",
            "retro.md", "What do we stop doing? How do my other projects compete? Do we read the files before meetings?",
            "home-admin.md", "How do I file my taxes? Other files: my insurance, how do we stop the old subscription.",
            "apps-to-try.md", "Other apps I want to try: how do they sync my files? Stop paying for the ones I do not use.");

    static final Map<String, String> CORPUS = Map.of(
            "adr-local-api-tokens.md", """
                    # Self-issued JWTs protect the localhost API
                    Any process on the Mac could call the API and read the user's files through it,
                    so every request must carry a short-lived signed token.""",
            "adr-java-engine.md", """
                    # Java for the Engine, not Rust
                    The Engine is written in Java rather than Rust; the speed difference is a few milliseconds.""",
            "canary-runbook.md", """
                    # Canary rollout
                    Shift ten percent of traffic to the new version, watch the error rate, then promote it.""",
            "certificate-renewal.md", """
                    # Renewing the TLS certificate
                    Generate a CSR, order the certificate from DigiCert and import it into the load balancer
                    before the old one expires.""",
            "banana-bread.md", """
                    # Banana bread
                    Mash three ripe bananas, mix with flour, sugar and butter, and bake for an hour.""",
            "sprint-notes.md", """
                    # Sprint planning
                    How do we plan this sprint? My notes: do the grooming first, then how do we split the
                    stories, and what do we do about my holiday? How do I read my calendar?""",
            "postgres-errors.txt", """
                    ECONNREFUSED 127.0.0.1:5432 when the database container is down.""",
            "budget-review.md", """
                    # Budget review
                    The quarterly budget review moved to Thursday afternoon.""");

    @TempDir
    static Path folder;
    static OnnxEmbedder embedder;
    static TestEngine engine;

    @BeforeAll
    static void indexCorpus() throws IOException {
        Path modelDir = Path.of(System.getProperty("pounce.model.dir", "models/bge-small-en-v1.5"));
        assumeTrue(Files.exists(modelDir.resolve("model.onnx")), "model missing: run scripts/fetch-model.sh");
        folder = folder.toRealPath();
        for (var corpus : java.util.List.of(CORPUS, FILLER)) {
            for (var file : corpus.entrySet()) {
                Files.writeString(folder.resolve(file.getKey()), file.getValue());
            }
        }
        embedder = OnnxEmbedder.load(modelDir);
        engine = TestEngine.over(folder, embedder);
        engine.indexer().reconcile();
    }

    @AfterAll
    static void close() {
        if (embedder != null) {
            embedder.close();
        }
    }

    @ParameterizedTest(name = "\"{0}\" → nothing")
    @CsvSource(textBlock = """
            xyzzyplugh
            qwfp zxcv
            purple elephant violin
            medieval falconry techniques
            """)
    void a_query_unrelated_to_every_document_has_no_results(String query) {
        assertThat(engine.searcher().search(Query.of(query), Phase.FINAL)).isEmpty();
    }

    @Test
    @Disabled("Known limit of bge-small: on meaning alone it rates apps-to-try.md 0.79 and the ADR below 0.64, "
            + "so no blend with Keyword Matches can rank the ADR first. Needs a stronger model or a reranker.")
    void a_question_that_needs_reasoning_finds_the_decision_that_answers_it() {
        the_expected_document_is_the_top_result("how do we stop other apps reading my files", "adr-local-api-tokens.md");
    }

    @ParameterizedTest(name = "\"{0}\" → {1}")
    @CsvSource(delimiter = '|', textBlock = """
            why java and not rust                      | adr-java-engine.md
            ECONNREFUSED                               | postgres-errors.txt
            renew ssl cert                             | certificate-renewal.md
            gradual release to a small share of users  | canary-runbook.md
            dessert recipe                             | banana-bread.md
            canary                                     | canary-runbook.md
            budget                                     | budget-review.md
            what do I do when the database is down     | postgres-errors.txt
            """)
    void the_expected_document_is_the_top_result(String query, String expected) {
        var results = engine.searcher().search(Query.of(query), Phase.FINAL);

        assertThat(results).extracting(Result::path).first().isEqualTo(folder.resolve(expected));
    }
}
