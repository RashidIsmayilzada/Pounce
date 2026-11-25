package dev.pounce.infrastructure;

import org.junit.jupiter.api.Test;

import java.util.Arrays;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class TinyJsonTest {

    @Test
    void reads_objects_arrays_and_scalars() {
        Object json = TinyJson.parse("""
                { "a": [1, -2, 3.5, 1e3, true, false, null], "b": {}, "c": [], "d": "x" }
                """);

        assertThat(json).isEqualTo(Map.of(
                "a", Arrays.asList(1L, -2L, 3.5, 1000.0, true, false, null),
                "b", Map.of(),
                "c", List.of(),
                "d", "x"));
    }

    @Test
    void reads_every_string_escape_including_surrogate_pairs() {
        assertThat(TinyJson.parse("\"q\\\" b\\\\ s\\/ \\b\\f\\n\\r\\t \\u00e9 \\uD83D\\uDE00 \\u4E2D ##\""))
                .isEqualTo("q\" b\\ s/ \b\f\n\r\t é 😀 中 ##");
    }

    @Test
    void keeps_raw_non_ascii_text() {
        assertThat(TinyJson.parse("{\"ü\": \"日本😀\"}")).isEqualTo(Map.of("ü", "日本😀"));
    }

    @Test
    void refuses_broken_json() {
        for (String broken : List.of("", "{", "[1,]", "{\"a\" 1}", "\"open", "tru", "\"\\x\"", "1 2", "{\"a\":1,}")) {
            assertThatThrownBy(() -> TinyJson.parse(broken)).as(broken).isInstanceOf(IllegalArgumentException.class);
        }
    }
}
