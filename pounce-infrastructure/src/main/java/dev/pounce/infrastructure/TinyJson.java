package dev.pounce.infrastructure;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * A minimal JSON reader, just enough for a Hugging Face {@code tokenizer.json}: objects become {@link Map}s,
 * arrays {@link List}s, numbers {@link Long} or {@link Double}, and {@code null} becomes Java {@code null}.
 * The Engine has no JSON library of its own below the API module, and this saves adding one for a single file.
 */
final class TinyJson {

    private final String text;
    private int position;

    private TinyJson(String text) {
        this.text = text;
    }

    /** Parses one JSON document. */
    static Object parse(String text) {
        var reader = new TinyJson(text);
        reader.skipWhitespace();
        Object value = reader.value();
        reader.skipWhitespace();
        if (reader.position != text.length()) {
            throw reader.error("unexpected trailing content");
        }
        return value;
    }

    private Object value() {
        if (position >= text.length()) {
            throw error("unexpected end of input");
        }
        char c = text.charAt(position);
        return switch (c) {
            case '{' -> object();
            case '[' -> array();
            case '"' -> string();
            case 't' -> literal("true", Boolean.TRUE);
            case 'f' -> literal("false", Boolean.FALSE);
            case 'n' -> literal("null", null);
            default -> {
                if (c == '-' || (c >= '0' && c <= '9')) {
                    yield number();
                }
                throw error("unexpected character '" + c + "'");
            }
        };
    }

    private Map<String, Object> object() {
        position++; // {
        Map<String, Object> members = new LinkedHashMap<>();
        skipWhitespace();
        if (peek() == '}') {
            position++;
            return members;
        }
        while (true) {
            skipWhitespace();
            if (peek() != '"') {
                throw error("expected a member name");
            }
            String name = string();
            skipWhitespace();
            expect(':');
            skipWhitespace();
            members.put(name, value());
            skipWhitespace();
            char c = next();
            if (c == '}') {
                return members;
            }
            if (c != ',') {
                throw error("expected ',' or '}'");
            }
        }
    }

    private List<Object> array() {
        position++; // [
        List<Object> elements = new ArrayList<>();
        skipWhitespace();
        if (peek() == ']') {
            position++;
            return elements;
        }
        while (true) {
            skipWhitespace();
            elements.add(value());
            skipWhitespace();
            char c = next();
            if (c == ']') {
                return elements;
            }
            if (c != ',') {
                throw error("expected ',' or ']'");
            }
        }
    }

    private String string() {
        position++; // opening quote
        int start = position;
        // Fast path: no escapes.
        while (position < text.length()) {
            char c = text.charAt(position);
            if (c == '"') {
                return text.substring(start, position++);
            }
            if (c == '\\') {
                break;
            }
            position++;
        }
        var out = new StringBuilder(text.substring(start, position));
        while (true) {
            char c = next();
            if (c == '"') {
                return out.toString();
            }
            if (c != '\\') {
                out.append(c);
                continue;
            }
            char escape = next();
            switch (escape) {
                case '"', '\\', '/' -> out.append(escape);
                case 'b' -> out.append('\b');
                case 'f' -> out.append('\f');
                case 'n' -> out.append('\n');
                case 'r' -> out.append('\r');
                case 't' -> out.append('\t');
                case 'u' -> {
                    if (position + 4 > text.length()) {
                        throw error("truncated \\u escape");
                    }
                    try {
                        out.append((char) Integer.parseInt(text, position, position + 4, 16));
                    } catch (NumberFormatException e) {
                        throw error("invalid \\u escape");
                    }
                    position += 4;
                }
                default -> throw error("invalid escape '\\" + escape + "'");
            }
        }
    }

    private Number number() {
        int start = position;
        boolean integral = true;
        while (position < text.length()) {
            char c = text.charAt(position);
            if (c == '.' || c == 'e' || c == 'E') {
                integral = false;
            } else if (!(c == '-' || c == '+' || (c >= '0' && c <= '9'))) {
                break;
            }
            position++;
        }
        String literal = text.substring(start, position);
        try {
            return integral ? (Number) Long.parseLong(literal) : (Number) Double.parseDouble(literal);
        } catch (NumberFormatException e) {
            throw error("invalid number '" + literal + "'");
        }
    }

    private Object literal(String word, Object value) {
        if (!text.startsWith(word, position)) {
            throw error("expected '" + word + "'");
        }
        position += word.length();
        return value;
    }

    private void skipWhitespace() {
        while (position < text.length()) {
            char c = text.charAt(position);
            if (c != ' ' && c != '\t' && c != '\n' && c != '\r') {
                return;
            }
            position++;
        }
    }

    private char peek() {
        if (position >= text.length()) {
            throw error("unexpected end of input");
        }
        return text.charAt(position);
    }

    private char next() {
        char c = peek();
        position++;
        return c;
    }

    private void expect(char c) {
        if (next() != c) {
            position--;
            throw error("expected '" + c + "'");
        }
    }

    private IllegalArgumentException error(String message) {
        return new IllegalArgumentException("invalid JSON at offset " + position + ": " + message);
    }
}
