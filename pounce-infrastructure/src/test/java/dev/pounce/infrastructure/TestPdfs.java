package dev.pounce.infrastructure;

import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.pdmodel.PDPage;
import org.apache.pdfbox.pdmodel.PDPageContentStream;
import org.apache.pdfbox.pdmodel.encryption.AccessPermission;
import org.apache.pdfbox.pdmodel.encryption.StandardProtectionPolicy;
import org.apache.pdfbox.pdmodel.font.PDType1Font;
import org.apache.pdfbox.pdmodel.font.Standard14Fonts;

import java.io.IOException;
import java.nio.file.Path;

/** Writes small PDFs for tests, so no binary fixtures live in git. */
final class TestPdfs {

    private TestPdfs() {
    }

    /** One page per string, each written as a line of Helvetica text. */
    static Path write(Path file, String... pages) throws IOException {
        try (var pdf = new PDDocument()) {
            addPages(pdf, pages);
            pdf.save(file.toFile());
        }
        return file;
    }

    /** A PDF that needs a password to open. */
    static Path writeEncrypted(Path file, String text) throws IOException {
        try (var pdf = new PDDocument()) {
            addPages(pdf, text);
            var policy = new StandardProtectionPolicy("owner-secret", "user-secret", new AccessPermission());
            policy.setEncryptionKeyLength(128);
            pdf.protect(policy);
            pdf.save(file.toFile());
        }
        return file;
    }

    private static void addPages(PDDocument pdf, String... pages) throws IOException {
        var font = new PDType1Font(Standard14Fonts.FontName.HELVETICA);
        for (String text : pages) {
            var page = new PDPage();
            pdf.addPage(page);
            if (text.isEmpty()) {
                continue;
            }
            try (var content = new PDPageContentStream(pdf, page)) {
                content.beginText();
                content.setFont(font, 12);
                content.newLineAtOffset(72, 700);
                content.showText(text);
                content.endText();
            }
        }
    }
}
