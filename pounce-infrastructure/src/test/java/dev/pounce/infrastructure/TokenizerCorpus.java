package dev.pounce.infrastructure;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;

/** Texts to tokenize in the tokenizer tests, chosen for the rules a BERT tokenizer port usually gets wrong. */
final class TokenizerCorpus {

    /** What {@code OnnxEmbedder} puts in front of every Query. */
    static final String QUERY_PREFIX = "Represent this sentence for searching relevant passages: ";

    private TokenizerCorpus() {
    }

    /** Hand-written cases, one group per tricky rule. */
    static List<String> handwritten() {
        List<String> cases = new ArrayList<>(List.of(
                // empty and whitespace-only
                "", " ", "   ", "\t", "\n", "\r\n", " \t\n\r ", "\u00A0", "\u3000", "\u2009\u200A", "\u200B",
                // plain text and case
                "Hello, World!", "HELLO world", "canary deployment", "how do I renew my TLS certificate",
                "The Quick Brown Fox Jumps Over The Lazy Dog.", "iPhone macOS JavaScript McDonald's O'Neil",
                // accents: NFD and stripping of non-spacing marks
                "Ünïcödé àccénts: façade naïve résumé coöperate", "Crème brûlée à la carte", "Ångström Øre Æsir œuvre",
                "e\u0301 versus \u00E9", "a\u0308\u0301\u0323 stacked marks", "\u0344 \u0F73 \u1E9B\u0323",
                "Straße STRASSE \u1E9E", "İstanbul ıi IİIı", "ΣΊΣΥΦΟΣ Σίσυφος ὅτι ᾷ", "Ǆ ǅ ǆ ǈ", "ﬁ ﬀ ﬃ ﬆ ligatures",
                "Ｆｕｌｌｗｉｄｔｈ　ＡＢＣ　１２３", "½ ² ³ ⅓ ⅞", "ℌ ℍ ℕ ℙ", "Ω Ω Å Å K K", "Ⓐⓑ①②③ Ⅻ ⅻ",
                "Vietnamese: Tiếng Việt có dấu", "Polish: Zażółć gęślą jaźń", "Czech: Příliš žluťoučký kůň",
                // mixed scripts
                "Москва — столица России", "Ελληνικά γράμματα", "مرحبا بالعالم العربية", "שלום עולם עברית",
                "नमस्ते दुनिया हिन्दी", "สวัสดีชาวโลก ภาษาไทย", "안녕하세요 세계 한국어", "ქართული ენა", "Հայերեն",
                "Deutsch und English mixed with русский and 日本語", "ascii then 中文 then ascii", "ʻokina ʼapostrophe",
                // CJK, each ideograph its own token, including the extension blocks and the reference's gap
                "中文测试，你好世界！", "日本語のテキスト、カタカナ、ひらがな。", "漢字かな交じり文", "我爱北京天安门",
                "\uD840\uDC00\uD840\uDC01 \uD869\uDF00 \uD86D\uDF40 \uD86E\uDC1F \uD86E\uDC20 \uD86E\uDD1F \uD86E\uDD20",
                "\uF900\uF901\uFA0E compatibility ideographs \uD87E\uDC00", "\u3400\u4DBF\u4E00\u9FFF\u9FD5",
                // emoji and other supplementary characters
                "I ❤️ Pounce 😀👍🏽", "👨‍👩‍👧‍👦 family 🇳🇱 flag 🏳️‍🌈", "🥰 🫠 🪿 newer emoji", "😀😀😀",
                "𝔘𝔫𝔦𝔠𝔬𝔡𝔢 𝐁𝐨𝐥𝐝 𝕯𝖔𝖚𝖇𝖑𝖊", "𓀀𓀁 hieroglyphs", "𐐀𐐨 Deseret", "musical 𝄞 symbol", "a😀b",
                // zero-width, control and other invisible characters
                "zero\u200Bwidth", "join\u200Ders\u200C", "soft\u00ADhyphen", "\uFEFFbyte order mark", "bell\u0007char",
                "nul\u0000char", "esc\u001B[31mred\u001B[0m", "del\u007Fchar", "next\u0085line", "v\u000Btab f\u000Cfeed",
                "line\u2028para\u2029sep", "replacement\uFFFDchar", "nonchar\uFFFE\uFFFF", "private\uE000\uF8FFuse",
                "plane 15 \uDB80\uDC00 private", "lone \uD83D high", "lone \uDC00 low", "a\uDC00b\uD800c",
                "rtl\u200Fmark\u202Eoverride\u202C", "word\u2060joiner", "tag\uDB40\uDC41\uDB40\uDC7F", "\u0600\u06DD arabic",
                // tabs, newlines and other whitespace
                "tabs\there\tand\nnewlines\r\nand CRLF", "nbsp\u00A0here", "thin\u2009space", "ideographic\u3000space",
                "\u1680ogham", "narrow\u202Fnbsp", "medium\u205Fmath", "en\u2002em\u2003spaces", "\u001C\u001D\u001E\u001F",
                // very long words around max_input_chars_per_word (100)
                "a".repeat(99), "a".repeat(100), "a".repeat(101), "ab".repeat(50), "ab".repeat(51) + " short",
                "x".repeat(250), "😀".repeat(100), "😀".repeat(101), "é".repeat(100), "é".repeat(101),
                "e\u0301".repeat(60), "\uD840\uDC00".repeat(120), "supercalifragilisticexpialidocious",
                "pneumonoultramicroscopicsilicovolcanoconiosis", "antidisestablishmentarianism",
                "Donaudampfschifffahrtsgesellschaftskapitän", "averyveryveryverylongcompoundwordwithoutspaces",
                // punctuation runs and ASCII symbols that are not Unicode punctuation
                "!!!???...", "--->", "<==>", "«quoted» „German“ “curly” ‘single’", "…—–", "¿Qué? ¡Hola!",
                "a.b,c;d:e", "$100 + 5% = <tag> ^caret `tick` |pipe| ~tilde", "#hashtag @mention", "§ ¶ † ‡ • ·",
                "email@example.com", "https://example.com/path?q=1&r=2#frag", "C++ C# F# .NET", "™ © ® ° ± × ÷",
                "(((nested)))", "[[[brackets]]]", "{{{braces}}}", "____", "~~~", "''\"\"``", "。、！？「」『』【】",
                // code snippets
                "public static void main(String[] args) { System.out.println(\"hi\"); }",
                "for (int i = 0; i < n; i++) { sum += a[i]; }", "def f(x):\n    return x**2  # square\n",
                "SELECT * FROM users WHERE id = 42 AND name LIKE '%bob%';", "<div class=\"x\">&amp;&lt;</div>",
                "{\"key\": [1, 2.5e-3, null, true]}", "git commit -m 'fix: bug' && git push --force-with-lease",
                "ECONNREFUSED 127.0.0.1:5432", "snake_case camelCase PascalCase kebab-case SCREAMING_SNAKE_CASE",
                "0x1F600 1e10 3.14159 -42 1,000,000.00", "2026-11-25T13:45:28+01:00", "v1.2.3-SNAPSHOT",
                "#include <stdio.h>\nint main(void) { return 0; }", "x => x * 2; a ?? b; c?.d", "$HOME/.config/*.toml",
                // file paths
                "C:\\Users\\me\\file.md", "C:\\Program Files (x86)\\Pounce\\pounce.exe", "\\\\server\\share\\folder",
                "/home/me/notes/todo.md", "~/Documents/Résumé 2024.pdf", "D:/mixed\\slashes/file.TXT",
                "%APPDATA%\\Pounce\\index", "file:///C:/Users/me/My%20Notes.md",
                // special tokens written literally in the text
                "[SEP]", "[CLS]", "[MASK]", "[PAD]", "[UNK]", "hello [SEP] world", "hello[SEP]world",
                "[CLS][SEP][MASK][PAD][UNK]", "[sep] [Sep] [cls] lowercase is not special", "[SEP", "SEP]", "[[SEP]]",
                "[MASK]ed", "x[UNK]y", "[ SEP ]", "[SEP]".repeat(600), "[CLS] " + "a ".repeat(600),
                "Crème [SEP] brûlée", "\u00A0[SEP]\u00A0", "[SEP]\u0301 accent after", "[unused0] [unused1]"));
        // texts far longer than the limit, and the boundary around it
        cases.add("the canary rollout shifted traffic to the new version ".repeat(200));
        cases.add("a ".repeat(509));
        cases.add("a ".repeat(510));
        cases.add("a ".repeat(511));
        cases.add("a ".repeat(509) + "supercalifragilisticexpialidocious");
        cases.add("word ".repeat(2000));
        cases.add("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ".repeat(100));
        cases.add("中".repeat(600));
        cases.add("!".repeat(600));
        cases.add("x".repeat(100_000));
        // characters whose Unicode category changed or that were added after Unicode 8.0
        for (int c : new int[] {0x0378, 0x061D, 0x07FD, 0x08D4, 0x0890, 0x09FD, 0x166D, 0x1734, 0x2E43, 0x2E55,
                0x2E5D, 0xA8FF, 0x110CD, 0x11C3F, 0x13430, 0x1171E, 0x1F923, 0x1FAE0, 0x10D6E, 0xE01F0}) {
            String s = Character.toString(c);
            cases.add(s);
            cases.add("a" + s + "a");
            cases.add("Ab" + s + "x" + s + "y z");
        }
        return cases;
    }

    /** The other texts the repository's tests feed the Engine: their string literals and text blocks. */
    static List<String> repositoryTestFixtures(Path repository) {
        Set<String> texts = new LinkedHashSet<>();
        for (Path source : javaFiles(repository, "src/test/java")) {
            texts.addAll(literals(read(source)));
        }
        return withQueryPrefix(texts);
    }

    /** Real prose and real code: the repository's Markdown files, Java sources, and each of their lines. */
    static List<String> repositoryDocumentsAndCode(Path repository) {
        Set<String> texts = new LinkedHashSet<>();
        List<Path> files = new ArrayList<>(javaFiles(repository, "src/main/java"));
        try (Stream<Path> markdown = Files.walk(repository, 3)) {
            markdown.filter(p -> p.toString().endsWith(".md"))
                    .filter(p -> !p.toString().contains("/target/") && !p.toString().contains("/.git/"))
                    .sorted()
                    .forEach(files::add);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
        for (Path file : files) {
            String text = read(file);
            texts.add(text);
            text.lines().map(String::strip).filter(line -> !line.isEmpty()).forEach(texts::add);
        }
        return List.copyOf(texts);
    }

    /** Pseudo-random strings drawn from a mix of Unicode ranges, with a fixed seed so failures reproduce. */
    static List<String> random(long seed, int count) {
        var random = new Random(seed);
        List<String> texts = new ArrayList<>(count);
        for (int n = 0; n < count; n++) {
            int length = random.nextInt(10) == 0 ? 500 + random.nextInt(3000) : random.nextInt(120);
            var text = new StringBuilder();
            while (text.codePointCount(0, text.length()) < length) {
                appendRandom(random, text);
            }
            texts.add(text.toString());
        }
        return texts;
    }

    private static final int[][] RANGES = {
            {0x20, 0x7E}, {0x20, 0x7E}, {0x20, 0x7E}, {0x41, 0x5A}, // ASCII, weighted up
            {0x00, 0x1F}, {0x7F, 0x9F}, // controls
            {0xA0, 0xFF}, {0x100, 0x24F}, {0x250, 0x2FF}, // Latin-1, Latin Extended, IPA
            {0x300, 0x36F}, {0x1AB0, 0x1AFF}, {0x1DC0, 0x1DFF}, {0x20D0, 0x20FF}, {0xFE20, 0xFE2F}, // combining marks
            {0x370, 0x3FF}, {0x400, 0x4FF}, {0x530, 0x58F}, {0x590, 0x5FF}, {0x600, 0x6FF}, {0x700, 0x8FF},
            {0x900, 0x97F}, {0x980, 0xDFF}, {0xE00, 0xE7F}, {0xF00, 0xFFF}, {0x1000, 0x109F}, {0x10A0, 0x10FF},
            {0x1100, 0x11FF}, {0x1680, 0x18AF}, {0x1E00, 0x1FFF}, // Greek ... Greek Extended
            {0x2000, 0x206F}, {0x2070, 0x214F}, {0x2150, 0x23FF}, {0x2460, 0x27BF}, {0x2E00, 0x2E7F}, // punctuation, symbols
            {0x3000, 0x30FF}, {0x3100, 0x33FF}, {0x3400, 0x4DBF}, {0x4E00, 0x9FFF}, {0xA000, 0xABFF},
            {0xAC00, 0xD7AF}, {0xE000, 0xF8FF}, {0xF900, 0xFAFF}, {0xFB00, 0xFDFF}, {0xFE00, 0xFE6F},
            {0xFF00, 0xFFEF}, {0xFFF0, 0xFFFF}, // CJK ... specials
            {0x10000, 0x1FFFF}, {0x1D400, 0x1D7FF}, {0x1F300, 0x1FAFF}, {0x20000, 0x2FFFF}, // supplementary
            {0xE0000, 0xE01FF}, {0xF0000, 0x10FFFF}, {0x0, 0x10FFFF}, // tags, private use, anything at all
    };

    private static final String[] PIECES = {
            " ", "  ", "\t", "\n", "\r\n", "\u00A0", "\u3000", "\u200B", "\u200D", "\uFE0F", "[SEP]", "[CLS]",
            "[MASK]", "[PAD]", "[UNK]", "[sep]", "##", "\\", "C:\\Users\\", "e\u0301", "İ", "Σ", "ς",
    };

    private static void appendRandom(Random random, StringBuilder text) {
        int pick = random.nextInt(100);
        if (pick < 12) {
            text.append(PIECES[random.nextInt(PIECES.length)]);
        } else if (pick < 14) {
            text.append((char) (0xD800 + random.nextInt(0x800))); // an unpaired surrogate
        } else if (pick < 30) {
            String[] words = {"the", "canary", "rollout", "Pounce", "search", "tokenizer", "unaffable",
                    "embedding", "résumé", "naïve", "Москва", "東京", "😀", "don't", "e-mail", "X-Ray"};
            text.append(words[random.nextInt(words.length)]);
        } else {
            int[] range = RANGES[random.nextInt(RANGES.length)];
            int c = range[0] + random.nextInt(range[1] - range[0] + 1);
            if (c >= Character.MIN_SURROGATE && c <= Character.MAX_SURROGATE) {
                c = 'x';
            }
            text.appendCodePoint(c);
        }
    }

    /**
     * Every code point from {@code from} up to (not including) {@code to}, {@code perText} to a text, each alone
     * and inside a word: "c ac b ...". Unpaired surrogates included.
     */
    static List<String> codePointSweep(int from, int to, int perText) {
        List<String> texts = new ArrayList<>();
        var text = new StringBuilder();
        int inText = 0;
        for (int c = from; c < to; c++) {
            text.appendCodePoint(c).append(' ').append('A').appendCodePoint(c).append("b ");
            if (++inText == perText) {
                texts.add(text.toString());
                text.setLength(0);
                inText = 0;
            }
        }
        if (!text.isEmpty()) {
            texts.add(text.toString());
        }
        return texts;
    }

    private static List<String> withQueryPrefix(Set<String> texts) {
        List<String> all = new ArrayList<>(texts);
        texts.forEach(text -> all.add(QUERY_PREFIX + text));
        return all;
    }

    private static final Pattern TEXT_BLOCK = Pattern.compile("\"\"\"[ \\t]*\\R(.*?)\"\"\"", Pattern.DOTALL);
    private static final Pattern STRING = Pattern.compile("\"((?:[^\"\\\\\\n]|\\\\.)*)\"");

    /** String literals and text blocks of one Java file, with each text block's lines and "|" cells too. */
    static List<String> literals(String source) {
        List<String> texts = new ArrayList<>();
        Matcher blocks = TEXT_BLOCK.matcher(source);
        var rest = new StringBuilder();
        int last = 0;
        while (blocks.find()) {
            rest.append(source, last, blocks.start());
            last = blocks.end();
            String block = unescape(blocks.group(1).stripIndent());
            if (block == null) {
                continue;
            }
            texts.add(block);
            block.lines().map(String::strip).filter(line -> !line.isEmpty()).forEach(line -> {
                texts.add(line);
                for (String cell : line.split("\\|")) {
                    texts.add(cell.strip());
                }
            });
        }
        rest.append(source.substring(last));
        Matcher strings = STRING.matcher(rest);
        while (strings.find()) {
            String literal = unescape(strings.group(1));
            if (literal != null) {
                texts.add(literal);
            }
        }
        return texts;
    }

    private static String unescape(String literal) {
        try {
            return literal.translateEscapes();
        } catch (IllegalArgumentException e) {
            return null;
        }
    }

    private static List<Path> javaFiles(Path repository, String sourceFolder) {
        try (Stream<Path> modules = Files.list(repository)) {
            List<Path> files = new ArrayList<>();
            for (Path module : modules.filter(m -> m.getFileName().toString().startsWith("pounce-")).sorted().toList()) {
                Path folder = module.resolve(sourceFolder);
                if (Files.isDirectory(folder)) {
                    try (Stream<Path> java = Files.walk(folder)) {
                        java.filter(p -> p.toString().endsWith(".java")).sorted().forEach(files::add);
                    }
                }
            }
            return files;
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private static String read(Path file) {
        try {
            return Files.readString(file, StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }
}
