package dev.pounce.infrastructure;

import dev.pounce.application.TextExtractor;
import org.apache.pdfbox.Loader;
import org.apache.pdfbox.io.IOUtils;
import org.apache.pdfbox.io.RandomAccessRead;
import org.apache.pdfbox.io.RandomAccessReadBufferedFile;
import org.apache.pdfbox.text.PDFTextStripper;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Optional;

/**
 * Reads the text layer of a PDF with PDFBox. Password-protected, broken or image-only PDFs have no text, so they
 * stay findable by file name only, and are not parsed again until they change: a broken file stays broken.
 * Only a PDF that cannot be opened at all (another program holds it, permissions) throws, so it is retried.
 * Pages are buffered in a temp file rather than the heap, to keep memory flat. Fonts a PDF does not embed are never
 * looked up among the installed ones ({@link BundledFontMapper}).
 */
public final class PdfTextExtractor implements TextExtractor {

    @Override
    public Optional<String> extract(Path file) throws IOException {
        BundledFontMapper.install();
        try (var input = new RandomAccessReadBufferedFile(file)) {
            return textOf(input);
        }
    }

    /** Once the file is open, any failure is a PDF that PDFBox cannot make sense of. */
    private static Optional<String> textOf(RandomAccessRead input) {
        try (var pdf = Loader.loadPDF(input, IOUtils.createTempFileOnlyStreamCache())) {
            String text = new PDFTextStripper().getText(pdf);
            return text.isBlank() ? Optional.empty() : Optional.of(text);
        } catch (IOException | RuntimeException unparseable) {
            return Optional.empty();
        }
    }
}
