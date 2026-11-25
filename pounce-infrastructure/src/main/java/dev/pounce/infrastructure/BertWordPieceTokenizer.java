package dev.pounce.infrastructure;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.text.Normalizer;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * A pure-Java port of the Hugging Face {@code tokenizers} pipeline that BERT-style models declare in their
 * {@code tokenizer.json}: special tokens split out of the raw text, then {@code BertNormalizer},
 * {@code BertPreTokenizer}, {@code WordPiece}, and a {@code TemplateProcessing} or {@code BertProcessing}
 * post-processor. Any other tokenizer type in the file is refused when it is loaded, rather than tokenized wrongly.
 *
 * <p>The file's own {@code truncation} and {@code padding} settings are not used: the caller gives the maximum
 * length, and nothing is padded, since the Engine embeds one text at a time.
 *
 * <p>It replaces DJL's JNI wrapper around the Rust library, which has no Windows native library and would try
 * to download one. {@code BertWordPieceTokenizerGoldenTest} checks its output token for token against that library.
 */
final class BertWordPieceTokenizer {

    /** One tokenized text, ready for the model: token ids, their token type ids, and the attention mask. */
    record Encoding(long[] ids, long[] typeIds, long[] attentionMask) {
    }

    /** A special token found literally in the raw text, before normalisation. */
    private record AddedToken(String content, int id) {
    }

    // Special tokens, longest first so the first match at a position is the longest one.
    private final AddedToken[] addedTokens;
    private final String addedTokenFirstChars;

    // BertNormalizer
    private final boolean cleanText;
    private final boolean handleChineseChars;
    private final boolean stripAccents;
    private final boolean lowercase;

    // WordPiece
    private final Map<String, Integer> vocab;
    private final int unknownId;
    private final String continuingSubwordPrefix;
    private final int maxInputCharsPerWord;
    private final int longestVocabEntry; // in UTF-16 chars, without the continuing-subword prefix

    // Post-processor: the template around the single sequence.
    private final int[] prefixIds;
    private final int[] prefixTypeIds;
    private final int sequenceTypeId;
    private final int[] suffixIds;
    private final int[] suffixTypeIds;

    private BertWordPieceTokenizer(Map<String, Object> json) {
        Map<String, Object> model = object(json, "model");
        require("WordPiece".equals(model.get("type")), "model type " + model.get("type"));
        vocab = new HashMap<>();
        int longest = 0;
        continuingSubwordPrefix = string(model, "continuing_subword_prefix");
        for (var entry : object(model, "vocab").entrySet()) {
            String piece = entry.getKey();
            vocab.put(piece, ((Number) entry.getValue()).intValue());
            int length = piece.startsWith(continuingSubwordPrefix)
                    ? piece.length() - continuingSubwordPrefix.length()
                    : piece.length();
            longest = Math.max(longest, length);
        }
        longestVocabEntry = longest;
        String unknown = string(model, "unk_token");
        require(vocab.containsKey(unknown), "unknown token " + unknown + " missing from the vocabulary");
        unknownId = vocab.get(unknown);
        maxInputCharsPerWord = ((Number) model.get("max_input_chars_per_word")).intValue();

        Map<String, Object> normalizer = object(json, "normalizer");
        require("BertNormalizer".equals(normalizer.get("type")), "normalizer type " + normalizer.get("type"));
        cleanText = flag(normalizer, "clean_text");
        handleChineseChars = flag(normalizer, "handle_chinese_chars");
        lowercase = flag(normalizer, "lowercase");
        // As in the reference: strip_accents left unset follows lowercase.
        stripAccents = normalizer.get("strip_accents") == null ? lowercase : flag(normalizer, "strip_accents");

        Map<String, Object> preTokenizer = object(json, "pre_tokenizer");
        require("BertPreTokenizer".equals(preTokenizer.get("type")), "pre-tokenizer type " + preTokenizer.get("type"));

        addedTokens = addedTokens(json);
        var firstChars = new StringBuilder();
        for (AddedToken token : addedTokens) {
            firstChars.append(token.content().charAt(0));
        }
        addedTokenFirstChars = firstChars.toString();

        List<int[]> prefix = new ArrayList<>();
        List<int[]> suffix = new ArrayList<>();
        sequenceTypeId = template(object(json, "post_processor"), prefix, suffix);
        prefixIds = prefix.stream().mapToInt(p -> p[0]).toArray();
        prefixTypeIds = prefix.stream().mapToInt(p -> p[1]).toArray();
        suffixIds = suffix.stream().mapToInt(p -> p[0]).toArray();
        suffixTypeIds = suffix.stream().mapToInt(p -> p[1]).toArray();
    }

    /** Loads the tokenizer declared by a Hugging Face {@code tokenizer.json}. */
    static BertWordPieceTokenizer load(Path tokenizerJson) throws IOException {
        Object json = TinyJson.parse(Files.readString(tokenizerJson, StandardCharsets.UTF_8));
        if (!(json instanceof Map<?, ?> map)) {
            throw new IllegalArgumentException(tokenizerJson + " is not a JSON object");
        }
        @SuppressWarnings("unchecked")
        var root = (Map<String, Object>) map;
        return new BertWordPieceTokenizer(root);
    }

    /**
     * Tokenizes one text with its special tokens added, keeping at most {@code maxLength} tokens in total:
     * a longer text loses tokens from its end, never the special tokens. Nothing is padded.
     */
    Encoding encode(String text, int maxLength) {
        int added = prefixIds.length + suffixIds.length;
        if (maxLength <= added) {
            throw new IllegalArgumentException("maxLength " + maxLength + " leaves no room for the text");
        }
        text = withoutUnpairedSurrogates(text);
        // Tokens never depend on the text after them, so tokenizing can stop once the kept ones are known.
        int limit = maxLength - added;
        var ids = new IntBuffer(Math.min(limit, text.length()));
        int segmentStart = 0;
        for (int i = 0; i < text.length() && ids.size() < limit; i++) {
            AddedToken token = addedTokenAt(text, i);
            if (token != null) {
                tokenizeSegment(text.substring(segmentStart, i), ids, limit);
                ids.add(token.id());
                i += token.content().length() - 1;
                segmentStart = i + 1;
            }
        }
        if (ids.size() < limit) {
            tokenizeSegment(text.substring(segmentStart), ids, limit);
        }

        int sequenceLength = Math.min(ids.size(), limit);
        int length = sequenceLength + added;
        long[] tokenIds = new long[length];
        long[] typeIds = new long[length];
        long[] mask = new long[length];
        Arrays.fill(mask, 1);
        int at = 0;
        for (int i = 0; i < prefixIds.length; i++, at++) {
            tokenIds[at] = prefixIds[i];
            typeIds[at] = prefixTypeIds[i];
        }
        for (int i = 0; i < sequenceLength; i++, at++) {
            tokenIds[at] = ids.get(i);
            typeIds[at] = sequenceTypeId;
        }
        for (int i = 0; i < suffixIds.length; i++, at++) {
            tokenIds[at] = suffixIds[i];
            typeIds[at] = suffixTypeIds[i];
        }
        return new Encoding(tokenIds, typeIds, mask);
    }

    /**
     * A Java string can hold an unpaired surrogate, which is not a character, and Rust strings cannot. The reference
     * received its text through JNI as modified UTF-8, which is not valid UTF-8 when a surrogate is unpaired, and
     * then decoded the whole text lossily: every surrogate, paired or not, became U+FFFD. We do the same, so such
     * a text (rare: decoders put U+FFFD in place of bad input) gets the same tokens. With {@code clean_text}, as
     * in bge-small, the U+FFFD are then dropped.
     */
    private static String withoutUnpairedSurrogates(String text) {
        boolean unpaired = false;
        for (int i = 0; i < text.length() && !unpaired; i++) {
            char c = text.charAt(i);
            if (Character.isHighSurrogate(c)) {
                if (i + 1 < text.length() && Character.isLowSurrogate(text.charAt(i + 1))) {
                    i++;
                } else {
                    unpaired = true;
                }
            } else if (Character.isLowSurrogate(c)) {
                unpaired = true;
            }
        }
        if (!unpaired) {
            return text;
        }
        var replaced = new StringBuilder(text.length());
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            replaced.append(Character.isSurrogate(c) ? '\uFFFD' : c);
        }
        return replaced.toString();
    }

    private AddedToken addedTokenAt(String text, int index) {
        if (addedTokenFirstChars.indexOf(text.charAt(index)) < 0) {
            return null;
        }
        for (AddedToken token : addedTokens) {
            if (text.startsWith(token.content(), index)) {
                return token;
            }
        }
        return null;
    }

    /**
     * Normalises, pre-tokenizes and WordPiece-encodes a stretch of text that holds no special token,
     * stopping at the first word boundary once {@code out} holds {@code limit} ids.
     */
    private void tokenizeSegment(String segment, IntBuffer out, int limit) {
        if (segment.isEmpty()) {
            return;
        }
        String normalized = normalize(segment);
        int wordStart = -1;
        int i = 0;
        while (i < normalized.length() && out.size() < limit) {
            int c = normalized.codePointAt(i);
            int next = i + Character.charCount(c);
            if (isWhitespace(c) || isPunctuation(c)) {
                if (wordStart >= 0) {
                    wordPiece(normalized, wordStart, i, out);
                    wordStart = -1;
                }
                if (!isWhitespace(c)) {
                    wordPiece(normalized, i, next, out); // punctuation is a word of its own
                }
            } else if (wordStart < 0) {
                wordStart = i;
            }
            i = next;
        }
        if (wordStart >= 0 && out.size() < limit) {
            wordPiece(normalized, wordStart, normalized.length(), out);
        }
    }

    /** BertNormalizer: clean, space out CJK, strip accents, lowercase, in that order. */
    private String normalize(String text) {
        var cleaned = new StringBuilder(text.length() + 8);
        boolean ascii = true;
        int i = 0;
        while (i < text.length()) {
            int c = text.codePointAt(i);
            i += Character.charCount(c);
            if (cleanText) {
                if (c == 0 || c == 0xFFFD || isControl(c)) {
                    continue;
                }
                if (isWhitespace(c)) {
                    c = ' ';
                }
            }
            if (handleChineseChars && isChinese(c)) {
                cleaned.append(' ').appendCodePoint(c).append(' ');
            } else {
                cleaned.appendCodePoint(c);
            }
            ascii &= c < 0x80;
        }
        if (ascii) {
            if (lowercase) {
                for (int j = 0; j < cleaned.length(); j++) {
                    char c = cleaned.charAt(j);
                    if (c >= 'A' && c <= 'Z') {
                        cleaned.setCharAt(j, (char) (c + ('a' - 'A')));
                    }
                }
            }
            return cleaned.toString();
        }
        String result = cleaned.toString();
        if (stripAccents) {
            result = Normalizer.normalize(result, Normalizer.Form.NFD);
        }
        if (!stripAccents && !lowercase) {
            return result;
        }
        var out = new StringBuilder(result.length());
        int j = 0;
        while (j < result.length()) {
            int c = result.codePointAt(j);
            j += Character.charCount(c);
            if (stripAccents && UnicodeCategories8.isNonspacingMark(c)) {
                continue;
            }
            if (lowercase) {
                appendLowercase(c, out);
            } else {
                out.appendCodePoint(c);
            }
        }
        return out.toString();
    }

    /**
     * Lowercases one code point on its own, as Rust's {@code char::to_lowercase} does: no context (so no final
     * sigma), but the one unconditional multi-character mapping, U+0130 to "i" and U+0307.
     */
    private static void appendLowercase(int c, StringBuilder out) {
        if (c == 0x130) {
            out.append("i\u0307");
        } else {
            out.appendCodePoint(Character.toLowerCase(c));
        }
    }

    /** WordPiece: greedy longest-match-first; a word that cannot be fully matched becomes the unknown token. */
    private void wordPiece(String text, int from, int to, IntBuffer out) {
        if (text.codePointCount(from, to) > maxInputCharsPerWord) {
            out.add(unknownId);
            return;
        }
        int mark = out.size();
        int start = from;
        while (start < to) {
            int end = Math.min(to, start + longestVocabEntry);
            if (end < to && Character.isLowSurrogate(text.charAt(end)) && end > start + 1) {
                end--; // never split a surrogate pair
            }
            Integer id = null;
            while (start < end) {
                String piece = text.substring(start, end);
                id = vocab.get(start == from ? piece : continuingSubwordPrefix + piece);
                if (id != null) {
                    break;
                }
                end = text.offsetByCodePoints(end, -1);
            }
            if (id == null) {
                out.truncate(mark);
                out.add(unknownId);
                return;
            }
            out.add(id);
            start = end;
        }
    }

    /** {@code char::is_whitespace} in Rust: the Unicode White_Space property. */
    static boolean isWhitespace(int c) {
        return switch (c) {
            case '\t', '\n', 0x0B, 0x0C, '\r', ' ', 0x85, 0xA0, 0x1680, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000 -> true;
            default -> c >= 0x2000 && c <= 0x200A;
        };
    }

    /** Characters the BertNormalizer drops: Unicode 8.0 Cc, Cf and Co, except tab, newline and carriage return. */
    static boolean isControl(int c) {
        return c != '\t' && c != '\n' && c != '\r' && UnicodeCategories8.isOther(c);
    }

    /** ASCII punctuation (including symbols such as {@code $+<=>^`|~}) and the Unicode 8.0 punctuation categories. */
    static boolean isPunctuation(int c) {
        if ((c >= 33 && c <= 47) || (c >= 58 && c <= 64) || (c >= 91 && c <= 96) || (c >= 123 && c <= 126)) {
            return true;
        }
        return c >= 0x80 && UnicodeCategories8.isPunctuation(c);
    }

    /** The CJK ideograph blocks the BertNormalizer surrounds with spaces, exactly as the reference lists them. */
    static boolean isChinese(int c) {
        return (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0x20000 && c <= 0x2A6DF)
                || (c >= 0x2A700 && c <= 0x2B73F)
                || (c >= 0x2B740 && c <= 0x2B81F)
                || (c >= 0x2B920 && c <= 0x2CEAF)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0x2F800 && c <= 0x2FA1F);
    }

    // --- tokenizer.json ---

    private AddedToken[] addedTokens(Map<String, Object> json) {
        List<AddedToken> tokens = new ArrayList<>();
        Object list = json.get("added_tokens");
        if (list != null) {
            for (Object item : (List<?>) list) {
                @SuppressWarnings("unchecked")
                var token = (Map<String, Object>) item;
                String content = string(token, "content");
                require(!flag(token, "single_word") && !flag(token, "lstrip") && !flag(token, "rstrip")
                        && !flag(token, "normalized") && !content.isEmpty(),
                        "added token " + content + " with single_word, lstrip, rstrip or normalized set");
                tokens.add(new AddedToken(content, ((Number) token.get("id")).intValue()));
            }
        }
        tokens.sort(Comparator.comparingInt((AddedToken t) -> t.content().length()).reversed());
        return tokens.toArray(AddedToken[]::new);
    }

    /**
     * Reads the single-sequence template into the special tokens before and after the text, each as
     * {id, type id}, and returns the text's type id.
     */
    private int template(Map<String, Object> processor, List<int[]> prefix, List<int[]> suffix) {
        Object type = processor.get("type");
        if ("BertProcessing".equals(type)) {
            prefix.add(new int[] {((Number) ((List<?>) processor.get("cls")).get(1)).intValue(), 0});
            suffix.add(new int[] {((Number) ((List<?>) processor.get("sep")).get(1)).intValue(), 0});
            return 0;
        }
        require("TemplateProcessing".equals(type), "post-processor type " + type);
        Map<String, Object> specialTokens = object(processor, "special_tokens");
        Integer sequenceType = null;
        for (Object item : (List<?>) processor.get("single")) {
            @SuppressWarnings("unchecked")
            var piece = (Map<String, Object>) item;
            if (piece.containsKey("Sequence")) {
                Map<String, Object> sequence = object(piece, "Sequence");
                require(sequenceType == null && "A".equals(sequence.get("id")), "template " + processor.get("single"));
                sequenceType = ((Number) sequence.get("type_id")).intValue();
            } else {
                Map<String, Object> special = object(piece, "SpecialToken");
                int typeId = ((Number) special.get("type_id")).intValue();
                Map<String, Object> definition = object(specialTokens, string(special, "id"));
                for (Object id : (List<?>) definition.get("ids")) {
                    (sequenceType == null ? prefix : suffix).add(new int[] {((Number) id).intValue(), typeId});
                }
            }
        }
        require(sequenceType != null, "template without the sequence");
        return sequenceType;
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> object(Map<String, Object> json, String key) {
        Object value = json.get(key);
        require(value instanceof Map, "missing \"" + key + "\"");
        return (Map<String, Object>) value;
    }

    private static String string(Map<String, Object> json, String key) {
        Object value = json.get(key);
        require(value instanceof String, "missing \"" + key + "\"");
        return (String) value;
    }

    private static boolean flag(Map<String, Object> json, String key) {
        return Boolean.TRUE.equals(json.get(key));
    }

    private static void require(boolean condition, String what) {
        if (!condition) {
            throw new IllegalArgumentException("unsupported tokenizer.json: " + what);
        }
    }

    /** A growable list of ints without boxing. */
    private static final class IntBuffer {
        private int[] values;
        private int size;

        IntBuffer(int capacity) {
            values = new int[Math.max(16, capacity)];
        }

        void add(int value) {
            if (size == values.length) {
                values = Arrays.copyOf(values, size * 2);
            }
            values[size++] = value;
        }

        int get(int index) {
            return values[index];
        }

        int size() {
            return size;
        }

        void truncate(int newSize) {
            size = newSize;
        }
    }
}
