package dev.pounce.domain;

import java.util.ArrayList;
import java.util.List;

/**
 * Cuts a Document's text into Passages: Markdown at each heading, then every section into
 * windows of at most {@value #WINDOW} characters (~400 tokens) that overlap by {@value #OVERLAP}.
 */
public final class PassageSplitter {

    static final int WINDOW = 1_600;
    static final int OVERLAP = 200;

    public List<Passage> split(String text, TextFormat format) {
        List<String> sections = format == TextFormat.MARKDOWN
                ? List.of(text.split("(?m)(?=^#{1,6} )"))
                : List.of(text);
        List<Passage> passages = new ArrayList<>();
        for (String section : sections) {
            windows(section.strip(), passages);
        }
        return passages;
    }

    private static void windows(String section, List<Passage> into) {
        if (section.isBlank()) {
            return;
        }
        for (int start = 0; ; start += WINDOW - OVERLAP) {
            int end = Math.min(section.length(), start + WINDOW);
            into.add(new Passage(section.substring(start, end).strip()));
            if (end == section.length()) {
                return;
            }
        }
    }
}
