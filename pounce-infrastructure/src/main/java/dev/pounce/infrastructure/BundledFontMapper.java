package dev.pounce.infrastructure;

import org.apache.fontbox.FontBoxFont;
import org.apache.fontbox.ttf.TTFParser;
import org.apache.fontbox.ttf.TrueTypeFont;
import org.apache.pdfbox.io.RandomAccessReadBuffer;
import org.apache.pdfbox.pdmodel.font.CIDFontMapping;
import org.apache.pdfbox.pdmodel.font.FontMapper;
import org.apache.pdfbox.pdmodel.font.FontMapping;
import org.apache.pdfbox.pdmodel.font.FontMappers;
import org.apache.pdfbox.pdmodel.font.PDCIDSystemInfo;
import org.apache.pdfbox.pdmodel.font.PDFontDescriptor;

import java.io.IOException;
import java.io.UncheckedIOException;

/**
 * Stands in for every font a PDF does not embed with the font that ships inside the PDFBox jar, so PDFBox never looks
 * at the fonts installed on the computer.
 * <p>
 * PDFBox's own mapper reads every installed font file the first time a PDF uses a font it does not carry (the
 * Standard 14 Helvetica, Times and Courier, or Arial in many office exports) and writes a {@code .pdfbox.cache} file
 * into the user's home folder. With the hundreds of fonts Windows has, that stalls the first PDF of an index for
 * seconds, or minutes on a slow disk, and the home folder is not where Pounce keeps its data. Reading the text needs
 * only the widths of the glyphs, and those come from the PDF itself ({@code /Widths}) or, for the Standard 14 fonts,
 * from the metrics bundled with PDFBox. The substitute's widths count only in a PDF that neither embeds a font nor
 * gives its widths, which the PDF standard does not allow; there, as before on a computer without a matching font,
 * words set closer than the substitute's letters can run together.
 * <p>
 * Every answer is PDFBox's own last resort, the LiberationSans TrueType font it bundles, returned the way its default
 * mapper returns it when nothing installed matches, so every font class (Type 1, TrueType, CID-keyed) gets a font it can
 * use. The answers are not marked as fallbacks: PDFBox reads that mark only to log a warning per font, and here a
 * substitute is the plan, not a fault.
 */
final class BundledFontMapper implements FontMapper {

    /** PDFBox's own last resort, inside its jar. */
    static final String BUNDLED_FONT = "/org/apache/pdfbox/resources/ttf/LiberationSans-Regular.ttf";

    static {
        // Class initialisation runs once, and every thread that calls install() waits for it and sees its result.
        FontMappers.set(new BundledFontMapper());
    }

    private final TrueTypeFont font;

    private BundledFontMapper() {
        try (var bundled = FontMapper.class.getResourceAsStream(BUNDLED_FONT)) {
            if (bundled == null) {
                throw new IOException(BUNDLED_FONT + " is missing from the PDFBox jar");
            }
            font = new TTFParser().parse(RandomAccessReadBuffer.createBufferFromStream(bundled));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /** Makes PDFBox use this mapper. Call it before anything that may touch a PDF font; calling it again does nothing. */
    static void install() {
        // The work happens in the static initialiser above.
    }

    @Override
    public FontMapping<TrueTypeFont> getTrueTypeFont(String baseFont, PDFontDescriptor fontDescriptor) {
        return new FontMapping<>(font, false);
    }

    @Override
    public FontMapping<FontBoxFont> getFontBoxFont(String baseFont, PDFontDescriptor fontDescriptor) {
        return new FontMapping<>(font, false);
    }

    @Override
    public CIDFontMapping getCIDFont(String baseFont, PDFontDescriptor fontDescriptor, PDCIDSystemInfo cidSystemInfo) {
        // No OpenType CFF font, so PDFBox uses the TrueType one, as with its own last resort.
        return new CIDFontMapping(null, font, false);
    }
}
