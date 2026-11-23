// PROTOTYPE — throwaway speed/RAM spike for Pounce. Not production code; no tests, minimal error handling.
// Question: can bge-small-en-v1.5 (ONNX, Java) + Lucene 10.6 parent/child hybrid search + RRF
// hit Early < 10 ms, Final < 40 ms, and keep the whole process well under ~500 MB on an 8 GB M2?
//
//   java Spike index <indexDir> <model.onnx> <tokenizer.json> <scaleDocs> <root>...
//   java Spike search <indexDir> <model.onnx> <tokenizer.json>

import ai.djl.huggingface.tokenizers.Encoding;
import ai.djl.huggingface.tokenizers.HuggingFaceTokenizer;
import ai.onnxruntime.*;
import org.apache.lucene.analysis.Analyzer;
import org.apache.lucene.analysis.TokenStream;
import org.apache.lucene.analysis.standard.StandardAnalyzer;
import org.apache.lucene.analysis.tokenattributes.CharTermAttribute;
import org.apache.lucene.codecs.KnnVectorsFormat;
import org.apache.lucene.codecs.lucene104.Lucene104Codec;
import org.apache.lucene.codecs.lucene104.Lucene104HnswScalarQuantizedVectorsFormat;
import org.apache.lucene.document.*;
import org.apache.lucene.index.*;
import org.apache.lucene.search.*;
import org.apache.lucene.search.join.*;
import org.apache.lucene.store.FSDirectory;
import org.apache.lucene.util.BitSet;

import java.io.IOException;
import java.nio.LongBuffer;
import java.nio.charset.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.*;

public class Spike {
    static final int DIM = 384;
    static final String QUERY_PREFIX = "Represent this sentence for searching relevant passages: ";
    static final Set<String> EXT = Set.of("md", "txt", "java", "py", "ts", "js", "kt", "swift", "yaml", "yml", "sql", "sh");
    static final int MAX_REAL_FILES = Integer.getInteger("realFiles", 1200);
    static final int MAX_PASSAGES_PER_FILE = 12;

    public static void main(String[] a) throws Exception {
        footprint("start");
        if (a[0].equals("index")) index(Path.of(a[1]), Path.of(a[2]), Path.of(a[3]), Integer.parseInt(a[4]),
                Arrays.stream(a, 5, a.length).map(Path::of).toList());
        else search(Path.of(a[1]), Path.of(a[2]), Path.of(a[3]));
    }

    // ---------------------------------------------------------------- embedding
    static OrtEnvironment env;
    static OrtSession session;
    static HuggingFaceTokenizer tok;

    static void loadModel(Path model, Path tokenizer) throws Exception {
        long t = System.nanoTime();
        tok = HuggingFaceTokenizer.builder().optTokenizerPath(tokenizer).optMaxLength(512).optTruncation(true).build();
        env = OrtEnvironment.getEnvironment();
        var so = new OrtSession.SessionOptions();
        so.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT);
        so.setIntraOpNumThreads(4);
        so.setCPUArenaAllocator(false);
        session = env.createSession(model.toString(), so);
        System.out.printf("model loaded in %d ms, inputs=%s%n", (System.nanoTime() - t) / 1_000_000, session.getInputNames());
        footprint("model loaded");
    }

    static float[] embed(String text) throws OrtException {
        Encoding e = tok.encode(text);
        long[] ids = e.getIds(), mask = e.getAttentionMask(), types = e.getTypeIds();
        long[] shape = {1, ids.length};
        Map<String, OnnxTensor> in = new HashMap<>();
        in.put("input_ids", OnnxTensor.createTensor(env, LongBuffer.wrap(ids), shape));
        in.put("attention_mask", OnnxTensor.createTensor(env, LongBuffer.wrap(mask), shape));
        if (session.getInputNames().contains("token_type_ids"))
            in.put("token_type_ids", OnnxTensor.createTensor(env, LongBuffer.wrap(types), shape));
        try (var out = session.run(in)) {
            float[][][] h = (float[][][]) out.get(0).getValue();
            float[] cls = h[0][0].clone(); // bge uses the CLS token, then L2-normalise
            double n = 0; for (float f : cls) n += f * f; n = Math.sqrt(n);
            for (int i = 0; i < cls.length; i++) cls[i] /= (float) n;
            return cls;
        } finally { in.values().forEach(OnnxTensor::close); }
    }

    // ---------------------------------------------------------------- indexing
    static IndexWriterConfig iwc() {
        var c = new IndexWriterConfig(new StandardAnalyzer());
        c.setCodec(new Lucene104Codec() {
            final KnnVectorsFormat q = new Lucene104HnswScalarQuantizedVectorsFormat();
            @Override public KnnVectorsFormat getKnnVectorsFormatForField(String f) { return q; }
        });
        c.setRAMBufferSizeMB(32);
        return c;
    }

    record Passage(String text, float[] vec) {}
    record Doc(String path, String name, List<Passage> passages) {}

    static void index(Path dir, Path model, Path tokenizer, int scaleDocs, List<Path> roots) throws Exception {
        loadModel(model, tokenizer);
        List<Path> files = new ArrayList<>();
        for (Path r : roots) try (Stream<Path> s = Files.walk(r)) {
            s.filter(p -> !p.toString().matches(".*/(\\.[^/]*|node_modules|target|build|Library)/.*"))
             .filter(Files::isRegularFile)
             .filter(p -> EXT.contains(ext(p)))
             .filter(p -> size(p) > 0 && size(p) < 200_000)
             .forEach(files::add);
        }
        Collections.shuffle(files, new Random(42));
        files = files.subList(0, Math.min(MAX_REAL_FILES, files.size()));

        List<Doc> real = new ArrayList<>();
        long embedNanos = 0; int passages = 0;
        for (Path f : files) {
            String text = read(f); if (text == null) continue;
            List<Passage> ps = new ArrayList<>();
            for (String chunk : split(text, ext(f).equals("md"))) {
                if (ps.size() >= MAX_PASSAGES_PER_FILE) break;
                long t = System.nanoTime(); float[] v = embed(chunk); embedNanos += System.nanoTime() - t;
                ps.add(new Passage(chunk, v)); passages++;
            }
            real.add(new Doc(f.toString(), f.getFileName().toString(), ps));
        }
        System.out.printf("real corpus: %d files, %d passages, embedding avg %.1f ms/passage (%.0f passages/s)%n",
                real.size(), passages, embedNanos / 1e6 / passages, passages / (embedNanos / 1e9));
        footprint("after embedding real corpus");

        try (var d = FSDirectory.open(dir); var w = new IndexWriter(d, iwc())) {
            w.deleteAll();
            long t = System.nanoTime();
            for (Doc doc : real) w.addDocuments(block(doc));
            var rnd = new Random(7); long total = real.size(); long totalPassages = passages;
            for (int i = real.size(); i < scaleDocs; i++) { // synthetic copies with noised vectors, for scale only
                Doc src = real.get(rnd.nextInt(real.size()));
                List<Passage> ps = new ArrayList<>();
                for (Passage p : src.passages()) ps.add(new Passage(p.text(), noise(p.vec(), rnd)));
                w.addDocuments(block(new Doc(src.path() + "#synthetic-" + i, "synthetic-" + i + "-" + src.name(), ps)));
                total++; totalPassages += ps.size();
            }
            w.commit();
            System.out.printf("indexed %d documents / %d passages in %d s%n", total, totalPassages, (System.nanoTime() - t) / 1_000_000_000);
        }
        footprint("after indexing");
        System.out.println("index size on disk: " + dirSize(dir) / (1024 * 1024) + " MB");
    }

    static List<Document> block(Doc doc) {
        List<Document> out = new ArrayList<>();
        for (Passage p : doc.passages()) {
            var c = new Document();
            c.add(new TextField("text", p.text(), Field.Store.YES));
            c.add(new KnnFloatVectorField("vec", p.vec(), VectorSimilarityFunction.DOT_PRODUCT));
            out.add(c);
        }
        var parent = new Document(); // parent last in the block
        parent.add(new StringField("type", "doc", Field.Store.NO));
        parent.add(new StringField("path", doc.path(), Field.Store.YES));
        parent.add(new TextField("name", doc.name().replaceAll("[-_.]", " "), Field.Store.NO));
        out.add(parent);
        return out;
    }

    static List<String> split(String text, boolean markdown) {
        List<String> out = new ArrayList<>();
        List<String> sections = markdown ? Arrays.asList(text.split("(?m)(?=^#{1,6} )")) : List.of(text);
        for (String s : sections) {
            for (int i = 0; i < s.length(); i += 1400) { // ~400 tokens, ~200-char overlap
                String c = s.substring(i, Math.min(s.length(), i + 1600)).strip();
                if (c.length() > 20) out.add(c);
            }
        }
        return out;
    }

    // ---------------------------------------------------------------- searching
    static final String[] QUERIES = {
            "canary deployment rollout", "how to renew a tls certificate", "kubernetes helm values",
            "trading bot strategy", "attention is all you need", "spring security jwt",
            "error connection refused", "vector database embeddings", "meeting notes action items",
            "python script to index files", "docker compose", "what is a transformer",
            "canary deplo", "renew cert", "jwt", "obsidian template", "mixture of experts",
            "rate limit api", "unit test mock", "markdown frontmatter"};

    static void search(Path dir, Path model, Path tokenizer) throws Exception {
        loadModel(model, tokenizer);
        var reader = DirectoryReader.open(FSDirectory.open(dir));
        var searcher = new IndexSearcher(reader);
        var parents = new QueryBitSetProducer(new TermQuery(new Term("type", "doc")));
        System.out.printf("opened index: %d leaves, %d docs (parents+passages)%n", reader.leaves().size(), reader.maxDoc());
        footprint("index opened");

        List<Long> early = new ArrayList<>(), emb = new ArrayList<>(), knn = new ArrayList<>(), fin = new ArrayList<>();
        for (int round = 0; round < 2; round++) { // round 0 = warm-up
            for (int rep = 0; rep < 10; rep++) for (String q : QUERIES) {
                long t0 = System.nanoTime();
                TopDocs kw = searcher.search(keywordQuery(q, parents), 20);
                long t1 = System.nanoTime();
                float[] v = embed(QUERY_PREFIX + q);
                long t2 = System.nanoTime();
                TopDocs vec = toParents(searcher.search(
                        new DiversifyingChildrenFloatKnnVectorQuery("vec", v, null, 20, parents), 20), reader, parents);
                TopDocs fused = TopDocs.rrf(8, 60, new TopDocs[]{kw, vec});
                for (ScoreDoc sd : fused.scoreDocs) searcher.storedFields().document(sd.doc).get("path");
                long t3 = System.nanoTime();
                if (round == 1) { early.add(t1 - t0); emb.add(t2 - t1); knn.add(t3 - t2); fin.add(t3 - t0); }
            }
        }
        report("EARLY  (keyword + name, prefix on last word)", early);
        report("  query embedding", emb);
        report("  vector search + RRF + load 8 paths", knn);
        report("FINAL  (early + embedding + vector + RRF)", fin);

        String q = "how to renew a tls certificate";
        TopDocs kw = searcher.search(keywordQuery(q, parents), 20);
        TopDocs vec = toParents(searcher.search(new DiversifyingChildrenFloatKnnVectorQuery("vec",
                embed(QUERY_PREFIX + q), null, 20, parents), 20), reader, parents);
        System.out.println("\nsample FINAL results for \"" + q + "\":");
        for (ScoreDoc sd : TopDocs.rrf(8, 60, new TopDocs[]{kw, vec}).scoreDocs)
            System.out.println("  " + searcher.storedFields().document(sd.doc).get("path"));

        System.gc(); Thread.sleep(500);
        footprint("steady state after searches");
    }

    static Query keywordQuery(String q, BitSetProducer parents) throws IOException {
        List<String> terms = analyze(q);
        var child = new BooleanQuery.Builder(); var name = new BooleanQuery.Builder();
        for (int i = 0; i < terms.size(); i++) {
            boolean last = i == terms.size() - 1;
            Query ct = last ? new PrefixQuery(new Term("text", terms.get(i))) : new TermQuery(new Term("text", terms.get(i)));
            Query nt = last ? new PrefixQuery(new Term("name", terms.get(i))) : new TermQuery(new Term("name", terms.get(i)));
            child.add(ct, BooleanClause.Occur.SHOULD); name.add(nt, BooleanClause.Occur.SHOULD);
        }
        return new BooleanQuery.Builder()
                .add(new ToParentBlockJoinQuery(child.build(), parents, org.apache.lucene.search.join.ScoreMode.Max), BooleanClause.Occur.SHOULD)
                .add(new BoostQuery(name.build(), 2f), BooleanClause.Occur.SHOULD).build();
    }

    // DiversifyingChildren... returns the best *child* per parent; RRF fuses by doc id, so map children to parents.
    static TopDocs toParents(TopDocs td, IndexReader reader, BitSetProducer parents) throws IOException {
        var leaves = reader.leaves(); var out = new ArrayList<ScoreDoc>();
        for (ScoreDoc sd : td.scoreDocs) {
            var leaf = leaves.get(ReaderUtil.subIndex(sd.doc, leaves));
            BitSet bits = parents.getBitSet(leaf);
            out.add(new ScoreDoc(leaf.docBase + bits.nextSetBit(sd.doc - leaf.docBase), sd.score));
        }
        return new TopDocs(td.totalHits, out.toArray(ScoreDoc[]::new));
    }

    // ---------------------------------------------------------------- helpers
    static List<String> analyze(String s) throws IOException {
        List<String> out = new ArrayList<>();
        try (Analyzer an = new StandardAnalyzer(); TokenStream ts = an.tokenStream("text", s)) {
            var term = ts.addAttribute(CharTermAttribute.class); ts.reset();
            while (ts.incrementToken()) out.add(term.toString());
            ts.end();
        }
        return out;
    }

    static float[] noise(float[] v, Random r) {
        float[] o = new float[v.length]; double n = 0;
        for (int i = 0; i < v.length; i++) { o[i] = v[i] + (float) (r.nextGaussian() * 0.03); n += o[i] * o[i]; }
        n = Math.sqrt(n); for (int i = 0; i < o.length; i++) o[i] /= (float) n;
        return o;
    }

    static void report(String label, List<Long> ns) {
        long[] s = ns.stream().mapToLong(Long::longValue).sorted().toArray();
        System.out.printf("%-48s p50 %6.2f ms   p95 %6.2f ms   p99 %6.2f ms   (n=%d)%n", label,
                s[s.length / 2] / 1e6, s[(int) (s.length * 0.95)] / 1e6, s[(int) (s.length * 0.99)] / 1e6, s.length);
    }

    static void footprint(String label) {
        try {
            var p = new ProcessBuilder("vmmap", "--summary", Long.toString(ProcessHandle.current().pid()))
                    .redirectErrorStream(true).start();
            String out = new String(p.getInputStream().readAllBytes());
            String now = grab(out, "Physical footprint:"), peak = grab(out, "Physical footprint (peak):");
            var rt = Runtime.getRuntime();
            System.out.printf("[memory] %-30s footprint %-8s peak %-8s java heap used %d MB%n", label, now, peak,
                    (rt.totalMemory() - rt.freeMemory()) / (1024 * 1024));
        } catch (Exception e) { System.out.println("[memory] " + label + ": " + e); }
    }

    static String grab(String s, String key) {
        int i = s.indexOf(key); if (i < 0) return "?";
        return s.substring(i + key.length()).strip().split("\\s+")[0];
    }

    static String ext(Path p) { String n = p.getFileName().toString(); int i = n.lastIndexOf('.'); return i < 0 ? "" : n.substring(i + 1).toLowerCase(); }
    static long size(Path p) { try { return Files.size(p); } catch (IOException e) { return -1; } }
    static String read(Path p) {
        try { return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(java.nio.ByteBuffer.wrap(Files.readAllBytes(p))).toString(); }
        catch (Exception e) { return null; }
    }
    static long dirSize(Path d) throws IOException { try (var s = Files.list(d)) { return s.mapToLong(Spike::size).sum(); } }
}
