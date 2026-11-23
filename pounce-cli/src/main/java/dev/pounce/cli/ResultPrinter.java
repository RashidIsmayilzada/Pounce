package dev.pounce.cli;

import java.io.PrintWriter;
import java.util.List;

/** Prints Results as "name  path" with the Snippet indented below; matched words in bold on a colour terminal. */
final class ResultPrinter {

    private static final String BOLD = "\u001B[1m";
    private static final String NORMAL = "\u001B[22m";

    private final boolean color;

    ResultPrinter(boolean color) {
        this.color = color;
    }

    void print(List<EngineClient.Result> results, PrintWriter out) {
        for (EngineClient.Result result : results) {
            out.println(result.name() + "  " + result.path());
            if (result.snippet() != null && !result.snippet().text().isBlank()) {
                out.println("  " + snippet(result.snippet()));
            }
        }
    }

    private String snippet(EngineClient.Snippet snippet) {
        String text = snippet.text();
        if (!color) {
            return oneLine(text);
        }
        var out = new StringBuilder();
        int position = 0;
        for (EngineClient.Highlight h : snippet.highlights()) {
            out.append(text, position, h.start()).append(BOLD).append(text, h.start(), h.end()).append(NORMAL);
            position = h.end();
        }
        return oneLine(out.append(text.substring(position)).toString());
    }

    private static String oneLine(String text) {
        return text.replaceAll("\\s+", " ").strip();
    }
}
