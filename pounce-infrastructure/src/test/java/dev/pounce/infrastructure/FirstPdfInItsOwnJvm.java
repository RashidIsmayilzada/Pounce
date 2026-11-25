package dev.pounce.infrastructure;

import java.io.IOException;
import java.nio.file.Path;

/**
 * Run by {@link PdfTextExtractorTest} as a JVM of its own, where PDFBox has not been used yet: writes a PDF in a font it
 * does not embed, reads it back, and prints how many milliseconds both took. Exits with 2 if the text did not come back.
 */
final class FirstPdfInItsOwnJvm {

    static final String TEXT = "Canary rollout plan for Friday";

    private FirstPdfInItsOwnJvm() {
    }

    public static void main(String[] args) throws IOException {
        long start = System.nanoTime();
        Path pdf = TestPdfs.write(Path.of(args[0]).resolve("plan.pdf"), TEXT);
        boolean read = new PdfTextExtractor().extract(pdf).filter(text -> text.contains(TEXT)).isPresent();
        long millis = (System.nanoTime() - start) / 1_000_000;
        System.out.println();
        System.out.println(millis);
        System.exit(read ? 0 : 2);
    }
}
