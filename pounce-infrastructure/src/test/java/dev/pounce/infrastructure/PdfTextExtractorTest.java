package dev.pounce.infrastructure;

import dev.pounce.infrastructure.TestPdfs.FontKind;
import org.apache.pdfbox.pdmodel.font.PDFont;
import org.apache.pdfbox.pdmodel.font.Standard14Fonts.FontName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * Contract for reading PDFs: text from every page; nothing when there is none to read (a broken, locked-with-a-password
 * or image-only PDF); an exception only when the file itself cannot be read right now. Fonts a PDF does not embed are
 * never looked up among the fonts installed on the computer, and the words come out the same.
 */
class PdfTextExtractorTest {

    static final String[] LINES = {
            "Kubernetes upgrade checklist for the production cluster",
            "Drain each node, then cordon it before the kubelet restarts",
            "Rollback with helm if the error rate rises above two percent",
    };

    @TempDir
    Path folder;
    final PdfTextExtractor extractor = new PdfTextExtractor();

    @Test
    void reads_the_text_of_every_page() throws IOException {
        Path pdf = TestPdfs.write(folder.resolve("plan.pdf"), "Canary rollout plan for Friday", "Rollback with helm if errors rise");

        assertThat(extractor.extract(pdf)).hasValueSatisfying(text -> assertThat(text)
                .contains("Canary rollout plan for Friday")
                .contains("Rollback with helm if errors rise"));
    }

    @Test
    void a_password_protected_pdf_has_no_text() throws IOException {
        Path pdf = TestPdfs.writeEncrypted(folder.resolve("payslip.pdf"), "Salary for November");

        assertThat(extractor.extract(pdf)).isEmpty();
    }

    @Test
    void a_broken_pdf_has_no_text() throws IOException {
        Path pdf = Files.write(folder.resolve("broken.pdf"), "%PDF-1.7 this is not really a pdf".getBytes());

        assertThat(extractor.extract(pdf)).isEmpty();
    }

    @Test
    void a_pdf_that_cannot_be_opened_right_now_is_an_error_not_a_pdf_without_text() throws IOException {
        Path pdf = TestPdfs.write(folder.resolve("plan.pdf"), "Canary rollout plan for Friday");

        try (var _ = Unreadable.make(pdf)) {
            assertThatThrownBy(() -> extractor.extract(pdf)).isInstanceOf(IOException.class);
        }
    }

    @Test
    void a_pdf_of_blank_pages_has_no_text() throws IOException {
        Path pdf = TestPdfs.write(folder.resolve("scan.pdf"), "", "");

        assertThat(extractor.extract(pdf)).isEmpty();
    }

    @ParameterizedTest
    @EnumSource(value = FontName.class, names = {"HELVETICA", "HELVETICA_BOLD", "TIMES_ROMAN", "TIMES_ITALIC", "COURIER"})
    void text_in_a_font_the_pdf_does_not_embed_reads_word_for_word(FontName font) throws IOException {
        Path pdf = TestPdfs.writeLines(folder.resolve("checklist.pdf"), font, LINES);

        assertThat(linesOf(pdf)).containsExactly(LINES);
    }

    @ParameterizedTest
    @EnumSource(FontKind.class)
    void text_in_a_font_that_is_not_one_of_the_standard_14_reads_word_for_word(FontKind kind) throws IOException {
        String accents = "Café, naïve, Zürich — 50 € per seat";
        Path pdf = TestPdfs.writeInFont(folder.resolve("checklist.pdf"), kind, LINES[0], LINES[1], accents);

        assertThat(linesOf(pdf)).containsExactly(LINES[0], LINES[1], accents);
    }

    @Test
    void two_columns_side_by_side_keep_their_words_apart() throws IOException {
        Path pdf = TestPdfs.writeColumns(folder.resolve("newsletter.pdf"),
                List.of("The canary rollout", "starts on Monday", "in Amsterdam."),
                List.of("Billing exports run", "every night at two", "for auditors."));

        assertThat(linesOf(pdf)).containsExactly(
                "The canary rollout Billing exports run",
                "starts on Monday every night at two",
                "in Amsterdam. for auditors.");
    }

    @Test
    void words_placed_apart_without_spaces_between_them_are_still_separate_words() throws IOException {
        Path pdf = TestPdfs.writeWordsApart(folder.resolve("checklist.pdf"), LINES);

        assertThat(linesOf(pdf)).containsExactly(LINES);
    }

    @Test
    void words_set_apart_only_by_kerning_are_still_separate_words() throws IOException {
        Path pdf = TestPdfs.writeKerned(folder.resolve("paper.pdf"), "K", 15f, "ubernetes", -333f, "upgrade", -333f,
                "c", 10f, "hec", -20f, "klist", -333f, "for", -333f, "the", -333f, "pro", 25f, "duction", -333f, "cluster");

        assertThat(linesOf(pdf)).containsExactly("Kubernetes upgrade checklist for the production cluster");
    }

    /**
     * PDFBox's own font mapper reads every installed font the first time a PDF uses a font it does not embed (minutes
     * with the hundreds that Windows has) and leaves {@code .pdfbox.cache} in the folder that the {@code pdfbox.fontcache}
     * property names, or else in the home folder. The font mapper is one per process, so this runs in a JVM of its own,
     * where PDFBox has not been used yet, with both folders pointed into a temporary one, and a font in that home folder,
     * so that there is something to find also on a Linux or macOS without fonts (Windows always has some).
     */
    @Test
    void reading_a_pdf_never_looks_through_the_fonts_installed_on_the_computer() throws IOException, InterruptedException {
        Path home = Files.createDirectories(folder.resolve("home"));
        Path fontCache = Files.createDirectories(folder.resolve("font-cache"));
        for (String userFonts : List.of(".fonts", "Library/Fonts")) {
            try (InputStream font = PDFont.class.getResourceAsStream(BundledFontMapper.BUNDLED_FONT)) {
                Files.copy(font, Files.createDirectories(home.resolve(userFonts)).resolve("Sans.ttf"));
            }
        }
        Path output = folder.resolve("output.txt");
        String javaCommand = ProcessHandle.current().info().command()
                .orElse(Path.of(System.getProperty("java.home"), "bin", "java").toString());

        Process java = new ProcessBuilder(javaCommand, "-Duser.home=" + home, "-Dpdfbox.fontcache=" + fontCache,
                "-cp", System.getProperty("java.class.path"), FirstPdfInItsOwnJvm.class.getName(), folder.toString())
                .redirectErrorStream(true).redirectOutput(output.toFile()).start();
        boolean finished = java.waitFor(2, TimeUnit.MINUTES);
        java.destroyForcibly();

        List<String> printed = Files.readAllLines(output);
        assertThat(finished).as("finished in time; printed %s", printed).isTrue();
        assertThat(java.exitValue()).as("read the PDF back; printed %s", printed).isZero();
        assertThat(fontCache.resolve(".pdfbox.cache")).doesNotExist();
        assertThat(home.resolve(".pdfbox.cache")).doesNotExist();
        assertThat(Long.parseLong(printed.getLast().strip())).as("milliseconds to write and read the first PDF")
                .isLessThan(10_000);
    }

    private List<String> linesOf(Path pdf) throws IOException {
        return extractor.extract(pdf).orElseThrow().lines().toList();
    }
}
