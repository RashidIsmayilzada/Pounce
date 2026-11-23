package dev.pounce.infrastructure;

import ai.djl.huggingface.tokenizers.Encoding;
import ai.djl.huggingface.tokenizers.HuggingFaceTokenizer;
import ai.onnxruntime.OnnxTensor;
import ai.onnxruntime.OrtEnvironment;
import ai.onnxruntime.OrtException;
import ai.onnxruntime.OrtSession;
import dev.pounce.application.Embedder;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.LongBuffer;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Embeds text with bge-small-en-v1.5 (int8 ONNX) on the CPU: the CLS token's hidden state, L2-normalised.
 * Queries get the model's retrieval instruction prefix; Passages are embedded as they are.
 * Input longer than the model's 512 tokens is truncated.
 */
public final class OnnxEmbedder implements Embedder, AutoCloseable {

    private static final String QUERY_PREFIX = "Represent this sentence for searching relevant passages: ";
    private static final int MAX_TOKENS = 512;

    private final OrtEnvironment environment;
    private final OrtSession session;
    private final HuggingFaceTokenizer tokenizer;

    private OnnxEmbedder(OrtEnvironment environment, OrtSession session, HuggingFaceTokenizer tokenizer) {
        this.environment = environment;
        this.session = session;
        this.tokenizer = tokenizer;
    }

    /** Loads {@code model.onnx} and {@code tokenizer.json} from the given folder. */
    public static OnnxEmbedder load(Path modelDir) {
        try {
            var tokenizer = HuggingFaceTokenizer.builder()
                    .optTokenizerPath(modelDir.resolve("tokenizer.json"))
                    .optMaxLength(MAX_TOKENS)
                    .optTruncation(true)
                    .build();
            var environment = OrtEnvironment.getEnvironment();
            var options = new OrtSession.SessionOptions();
            options.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT);
            options.setIntraOpNumThreads(4);
            options.setCPUArenaAllocator(false); // lower steady-state memory, see docs/research
            var session = environment.createSession(modelDir.resolve("model.onnx").toString(), options);
            return new OnnxEmbedder(environment, session, tokenizer);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        } catch (OrtException e) {
            throw new IllegalStateException("could not load embedding model from " + modelDir, e);
        }
    }

    @Override
    public float[] embedQuery(String query) {
        return embed(QUERY_PREFIX + query);
    }

    @Override
    public List<float[]> embedPassages(List<String> passages) {
        List<float[]> vectors = new ArrayList<>(passages.size());
        for (String passage : passages) {
            vectors.add(embed(passage));
        }
        return vectors;
    }

    private float[] embed(String text) {
        Encoding encoding = tokenizer.encode(text);
        long[] shape = {1, encoding.getIds().length};
        Map<String, OnnxTensor> inputs = new HashMap<>();
        try {
            inputs.put("input_ids", tensor(encoding.getIds(), shape));
            inputs.put("attention_mask", tensor(encoding.getAttentionMask(), shape));
            inputs.put("token_type_ids", tensor(encoding.getTypeIds(), shape));
            try (OrtSession.Result output = session.run(inputs)) {
                float[][][] hidden = (float[][][]) output.get(0).getValue();
                return normalised(hidden[0][0]);
            }
        } catch (OrtException e) {
            throw new IllegalStateException("embedding failed", e);
        } finally {
            inputs.values().forEach(OnnxTensor::close);
        }
    }

    private OnnxTensor tensor(long[] values, long[] shape) throws OrtException {
        return OnnxTensor.createTensor(environment, LongBuffer.wrap(values), shape);
    }

    private static float[] normalised(float[] vector) {
        double sum = 0;
        for (float v : vector) {
            sum += v * v;
        }
        float length = (float) Math.sqrt(sum);
        float[] unit = new float[vector.length];
        for (int i = 0; i < vector.length; i++) {
            unit[i] = vector[i] / length;
        }
        return unit;
    }

    @Override
    public void close() {
        try {
            session.close();
        } catch (OrtException e) {
            throw new IllegalStateException(e);
        } finally {
            tokenizer.close();
        }
    }
}
