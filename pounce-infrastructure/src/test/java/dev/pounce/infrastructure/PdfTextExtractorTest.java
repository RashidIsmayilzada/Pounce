package dev.pounce.infrastructure;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * Contract for reading PDFs: text from every page; nothing when there is none to read (a broken, locked-with-a-password
 * or image-only PDF); an exception only when the file itself cannot be read right now.
 */
class PdfTextExtractorTest {

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
}
