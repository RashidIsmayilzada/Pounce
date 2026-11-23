package dev.pounce.infrastructure;

import dev.pounce.application.TextExtractor;
import org.apache.pdfbox.Loader;
import org.apache.pdfbox.io.IOUtils;
import org.apache.pdfbox.text.PDFTextStripper;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Optional;

/**
 * Reads the text layer of a PDF with PDFBox. Password-protected, broken or image-only PDFs have no text, so they
 * stay findable by file name only. Pages are buffered in a temp file rather than the heap, to keep memory flat.
 */
public final class PdfTextExtractor implements TextExtractor {

    @Override
    public Optional<String> extract(Path file) {
        try (var pdf = Loader.loadPDF(file.toFile(), IOUtils.createTempFileOnlyStreamCache())) {
            String text = new PDFTextStripper().getText(pdf);
            return text.isBlank() ? Optional.empty() : Optional.of(text);
        } catch (IOException | RuntimeException unreadable) {
            return Optional.empty();
        }
    }
}
