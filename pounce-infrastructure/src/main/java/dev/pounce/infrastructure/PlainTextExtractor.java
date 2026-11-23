package dev.pounce.infrastructure;

import dev.pounce.application.TextExtractor;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Optional;

/** Reads files that are UTF-8 text (notes, code, config). Anything that does not decode as UTF-8 has no text. */
public final class PlainTextExtractor implements TextExtractor {

    @Override
    public Optional<String> extract(Path file) {
        try {
            byte[] bytes = Files.readAllBytes(file);
            return Optional.of(StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(bytes))
                    .toString());
        } catch (CharacterCodingException e) {
            return Optional.empty();
        } catch (IOException e) {
            return Optional.empty();
        }
    }
}
