package dev.pounce.infrastructure;

import dev.pounce.application.TextExtractor;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.Charset;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Optional;

/**
 * Reads files that are text (notes, code, config): UTF-8, or UTF-16 when a byte order mark says so, as Windows tools
 * often write it (PowerShell's {@code >}, Notepad's "Unicode"). A byte order mark is not part of the text. Anything
 * that does not decode has no text; a file that cannot be read at all throws.
 */
public final class PlainTextExtractor implements TextExtractor {

    @Override
    public Optional<String> extract(Path file) throws IOException {
        byte[] bytes = Files.readAllBytes(file);
        try {
            return Optional.of(decode(bytes));
        } catch (CharacterCodingException notText) {
            return Optional.empty();
        }
    }

    private static String decode(byte[] bytes) throws CharacterCodingException {
        if (startsWith(bytes, 0xEF, 0xBB, 0xBF)) {
            return decode(bytes, 3, StandardCharsets.UTF_8);
        }
        if (startsWith(bytes, 0xFF, 0xFE)) {
            return decode(bytes, 2, StandardCharsets.UTF_16LE);
        }
        if (startsWith(bytes, 0xFE, 0xFF)) {
            return decode(bytes, 2, StandardCharsets.UTF_16BE);
        }
        return decode(bytes, 0, StandardCharsets.UTF_8);
    }

    private static String decode(byte[] bytes, int skip, Charset charset) throws CharacterCodingException {
        return charset.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes, skip, bytes.length - skip))
                .toString();
    }

    private static boolean startsWith(byte[] bytes, int... prefix) {
        if (bytes.length < prefix.length) {
            return false;
        }
        for (int i = 0; i < prefix.length; i++) {
            if ((bytes[i] & 0xFF) != prefix[i]) {
                return false;
            }
        }
        return true;
    }
}
