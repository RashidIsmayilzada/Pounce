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
 * often write it (PowerShell's {@code >}, Notepad's "Unicode"). A byte order mark is not part of the text. Text that
 * is not UTF-8 and has no NUL bytes is read as windows-1252, the code page many Windows tools still write ("ANSI"),
 * so one accented letter does not make a note unsearchable. Anything else, binary files among it, has no text; a
 * file that cannot be read at all throws.
 */
public final class PlainTextExtractor implements TextExtractor {

    private static final Charset WINDOWS_1252 = Charset.forName("windows-1252");

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
        try {
            return decode(bytes, 0, StandardCharsets.UTF_8);
        } catch (CharacterCodingException notUtf8) {
            if (containsNul(bytes)) {
                throw notUtf8;
            }
            return decode(bytes, 0, WINDOWS_1252);
        }
    }

    private static boolean containsNul(byte[] bytes) {
        for (byte b : bytes) {
            if (b == 0) {
                return true;
            }
        }
        return false;
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
