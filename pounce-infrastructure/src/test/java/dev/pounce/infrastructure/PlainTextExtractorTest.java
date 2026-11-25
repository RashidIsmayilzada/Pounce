package dev.pounce.infrastructure;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * Contract for reading text files: UTF-8, with or without a byte order mark, and UTF-16 when a byte order mark says
 * so (as Windows tools write it, for example PowerShell's {@code >}), and otherwise the Windows code page
 * windows-1252. A file with NUL bytes and no UTF-16 byte order mark is binary and has no text.
 */
class PlainTextExtractorTest {

    static final String NOTE = "# Deploys\nThe canary rollout went fine. Ça va.";

    @TempDir
    Path folder;
    final PlainTextExtractor extractor = new PlainTextExtractor();

    @Test
    void reads_utf8() throws IOException {
        assertThat(extractor.extract(write(new byte[0], NOTE, StandardCharsets.UTF_8))).hasValue(NOTE);
    }

    @Test
    void a_utf8_byte_order_mark_is_not_part_of_the_text() throws IOException {
        byte[] bom = {(byte) 0xEF, (byte) 0xBB, (byte) 0xBF};

        assertThat(extractor.extract(write(bom, NOTE, StandardCharsets.UTF_8))).hasValue(NOTE);
    }

    @Test
    void reads_utf16_little_endian_after_its_byte_order_mark() throws IOException {
        byte[] bom = {(byte) 0xFF, (byte) 0xFE};

        assertThat(extractor.extract(write(bom, NOTE, StandardCharsets.UTF_16LE))).hasValue(NOTE);
    }

    @Test
    void reads_utf16_big_endian_after_its_byte_order_mark() throws IOException {
        byte[] bom = {(byte) 0xFE, (byte) 0xFF};

        assertThat(extractor.extract(write(bom, NOTE, StandardCharsets.UTF_16BE))).hasValue(NOTE);
    }

    @Test
    void reads_text_in_the_windows_ansi_code_page_when_it_is_not_utf8() throws IOException {
        // Notepad's "ANSI", and many older Windows tools: one accented letter makes it invalid UTF-8.
        assertThat(extractor.extract(write(new byte[0], NOTE, Charset.forName("windows-1252")))).hasValue(NOTE);
    }

    @Test
    void a_binary_file_has_no_text() throws IOException {
        Path file = Files.write(folder.resolve("photo.jpg"), new byte[]{(byte) 0xFF, (byte) 0xD8, (byte) 0xFF, 0, 1, 2});

        assertThat(extractor.extract(file)).isEmpty();
    }

    @Test
    void a_file_that_cannot_be_read_right_now_is_an_error_not_a_file_without_text() throws IOException {
        Path file = write(new byte[0], NOTE, StandardCharsets.UTF_8);

        try (var _ = Unreadable.make(file)) {
            assertThatThrownBy(() -> extractor.extract(file)).isInstanceOf(IOException.class);
        }
    }

    private Path write(byte[] start, String text, Charset charset) throws IOException {
        var bytes = new ByteArrayOutputStream();
        bytes.write(start);
        bytes.write(text.getBytes(charset));
        return Files.write(folder.resolve("notes.md"), bytes.toByteArray());
    }
}
