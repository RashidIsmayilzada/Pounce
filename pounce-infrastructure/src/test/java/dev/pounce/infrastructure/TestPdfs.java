package dev.pounce.infrastructure;

import org.apache.pdfbox.cos.COSArray;
import org.apache.pdfbox.cos.COSDictionary;
import org.apache.pdfbox.cos.COSName;
import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.pdmodel.PDPage;
import org.apache.pdfbox.pdmodel.PDPageContentStream;
import org.apache.pdfbox.pdmodel.encryption.AccessPermission;
import org.apache.pdfbox.pdmodel.encryption.StandardProtectionPolicy;
import org.apache.pdfbox.pdmodel.font.PDFont;
import org.apache.pdfbox.pdmodel.font.PDTrueTypeFont;
import org.apache.pdfbox.pdmodel.font.PDType0Font;
import org.apache.pdfbox.pdmodel.font.PDType1Font;
import org.apache.pdfbox.pdmodel.font.Standard14Fonts.FontName;
import org.apache.pdfbox.pdmodel.font.encoding.WinAnsiEncoding;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Path;
import java.util.List;

/**
 * Writes small PDFs for tests, so no binary fixtures live in git. Unless a method says otherwise, the text is in one of
 * the Standard 14 fonts, which a PDF names but does not carry.
 */
final class TestPdfs {

    static {
        // A Standard 14 font asks PDFBox's font mapper for a substitute as soon as it is created; the Engine's mapper
        // must be in place first, or PDFBox reads every font installed on the computer (minutes on Windows).
        BundledFontMapper.install();
    }

    /** How a PDF holds the font its text is in, when that font is not one of the Standard 14. */
    enum FontKind {
        /** A TrueType font embedded as a whole. */
        TRUETYPE_EMBEDDED,
        /** A TrueType font named, with its widths, but not embedded. */
        TRUETYPE_NOT_EMBEDDED,
        /** A CID-keyed font (Type 0, Unicode text) named but not embedded. */
        CID_KEYED_NOT_EMBEDDED
    }

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

    /** One page with one line of text per string, in the given font. */
    static Path writeLines(Path file, FontName font, String... lines) throws IOException {
        try (var pdf = new PDDocument()) {
            addLines(pdf, new PDType1Font(font), lines);
            pdf.save(file.toFile());
        }
        return file;
    }

    /** Two columns of Helvetica side by side, each line of a column placed on its own. */
    static Path writeColumns(Path file, List<String> left, List<String> right) throws IOException {
        var font = new PDType1Font(FontName.HELVETICA);
        try (var pdf = new PDDocument()) {
            var page = new PDPage();
            pdf.addPage(page);
            try (var content = new PDPageContentStream(pdf, page)) {
                for (int line = 0; line < Math.max(left.size(), right.size()); line++) {
                    float y = 700 - 14 * line;
                    if (line < left.size()) {
                        showAt(content, font, 72, y, left.get(line));
                    }
                    if (line < right.size()) {
                        showAt(content, font, 320, y, right.get(line));
                    }
                }
            }
            pdf.save(file.toFile());
        }
        return file;
    }

    /**
     * Each word shown on its own, a quarter of the font size after the end of the previous one, with no space
     * characters at all: the reader has to tell the words apart by the widths of the letters.
     */
    static Path writeWordsApart(Path file, String... lines) throws IOException {
        var font = new PDType1Font(FontName.HELVETICA);
        float size = 12;
        try (var pdf = new PDDocument()) {
            var page = new PDPage();
            pdf.addPage(page);
            try (var content = new PDPageContentStream(pdf, page)) {
                float y = 700;
                for (String line : lines) {
                    float x = 72;
                    for (String word : line.split(" ")) {
                        showAt(content, font, x, y, word);
                        x += font.getStringWidth(word) / 1000 * size + size / 4;
                    }
                    y -= 16;
                }
            }
            pdf.save(file.toFile());
        }
        return file;
    }

    /**
     * One run of Times text whose words are set apart only by position adjustments (in thousandths of the font size,
     * negative moves right), as TeX writes them: {@code "Kubern", 15f, "etes", -333f, "upgrade"}.
     */
    static Path writeKerned(Path file, Object... textAndAdjustments) throws IOException {
        var font = new PDType1Font(FontName.TIMES_ROMAN);
        try (var pdf = new PDDocument()) {
            var page = new PDPage();
            pdf.addPage(page);
            try (var content = new PDPageContentStream(pdf, page)) {
                content.beginText();
                content.setFont(font, 12);
                content.newLineAtOffset(72, 700);
                content.showTextWithPositioning(textAndAdjustments);
                content.endText();
            }
            pdf.save(file.toFile());
        }
        return file;
    }

    /** One page with one line of text per string, in the TrueType font bundled with PDFBox, held as {@code kind} says. */
    static Path writeInFont(Path file, FontKind kind, String... lines) throws IOException {
        try (var pdf = new PDDocument();
             InputStream ttf = PDFont.class.getResourceAsStream(BundledFontMapper.BUNDLED_FONT)) {
            PDFont font = switch (kind) {
                case TRUETYPE_EMBEDDED, TRUETYPE_NOT_EMBEDDED -> PDTrueTypeFont.load(pdf, ttf, WinAnsiEncoding.INSTANCE);
                case CID_KEYED_NOT_EMBEDDED -> PDType0Font.load(pdf, ttf, false);
            };
            addLines(pdf, font, lines);
            switch (kind) {
                case TRUETYPE_EMBEDDED -> { }
                case TRUETYPE_NOT_EMBEDDED -> leaveOut(font.getCOSObject(), "Verdana");
                case CID_KEYED_NOT_EMBEDDED -> {
                    font.getCOSObject().setName(COSName.BASE_FONT, "ArialUnicodeMS");
                    var descendants = (COSArray) font.getCOSObject().getDictionaryObject(COSName.DESCENDANT_FONTS);
                    leaveOut((COSDictionary) descendants.getObject(0), "ArialUnicodeMS");
                }
            }
            pdf.save(file.toFile());
        }
        return file;
    }

    /** Drops the embedded font program and renames the font, so a reader has to find a substitute for it. */
    private static void leaveOut(COSDictionary font, String name) {
        COSDictionary descriptor = font.getCOSDictionary(COSName.FONT_DESC);
        descriptor.removeItem(COSName.FONT_FILE2);
        descriptor.setName(COSName.FONT_NAME, name);
        font.setName(COSName.BASE_FONT, name);
    }

    private static void addLines(PDDocument pdf, PDFont font, String... lines) throws IOException {
        var page = new PDPage();
        pdf.addPage(page);
        try (var content = new PDPageContentStream(pdf, page)) {
            content.beginText();
            content.setFont(font, 12);
            content.setLeading(16);
            content.newLineAtOffset(72, 700);
            for (String line : lines) {
                content.showText(line);
                content.newLine();
            }
            content.endText();
        }
    }

    private static void showAt(PDPageContentStream content, PDFont font, float x, float y, String text)
            throws IOException {
        content.beginText();
        content.setFont(font, 12);
        content.newLineAtOffset(x, y);
        content.showText(text);
        content.endText();
    }

    private static void addPages(PDDocument pdf, String... pages) throws IOException {
        var font = new PDType1Font(FontName.HELVETICA);
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
