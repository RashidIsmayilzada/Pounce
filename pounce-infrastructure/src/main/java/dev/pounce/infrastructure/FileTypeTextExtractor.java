package dev.pounce.infrastructure;

import dev.pounce.application.TextExtractor;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Locale;
import java.util.Optional;

/** Picks the reader by file type: PDFs through PDFBox, everything else as text ({@link PlainTextExtractor}). */
public final class FileTypeTextExtractor implements TextExtractor {

    private final TextExtractor pdf = new PdfTextExtractor();
    private final TextExtractor plain = new PlainTextExtractor();

    @Override
    public Optional<String> extract(Path file) throws IOException {
        boolean isPdf = file.getFileName().toString().toLowerCase(Locale.ROOT).endsWith(".pdf");
        return (isPdf ? pdf : plain).extract(file);
    }
}
