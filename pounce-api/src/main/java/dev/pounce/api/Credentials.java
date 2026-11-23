package dev.pounce.api;

import javax.crypto.SecretKey;
import javax.crypto.spec.SecretKeySpec;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.PosixFilePermissions;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.Base64;

/**
 * The bootstrap secret clients exchange for a JWT, and the key JWTs are signed with (ADR-0003).
 * Both are created on first start as files only the user can read, and reused afterwards.
 */
final class Credentials {

    private static final SecureRandom RANDOM = new SecureRandom();

    private final byte[] bootstrapSecret;
    private final SecretKey signingKey;

    private Credentials(byte[] bootstrapSecret, SecretKey signingKey) {
        this.bootstrapSecret = bootstrapSecret;
        this.signingKey = signingKey;
    }

    static Credentials in(Path home) {
        try {
            Files.createDirectories(home);
            String secret = readOrCreate(home.resolve("bootstrap-secret"));
            byte[] key = Base64.getUrlDecoder().decode(readOrCreate(home.resolve("signing-key")));
            return new Credentials(secret.getBytes(StandardCharsets.UTF_8), new SecretKeySpec(key, "HmacSHA256"));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    boolean isBootstrapSecret(String candidate) {
        return candidate != null
                && MessageDigest.isEqual(bootstrapSecret, candidate.strip().getBytes(StandardCharsets.UTF_8));
    }

    SecretKey signingKey() {
        return signingKey;
    }

    /** 256 random bits, base64url, in a file with permission 600. */
    private static String readOrCreate(Path file) throws IOException {
        if (Files.exists(file)) {
            return Files.readString(file).strip();
        }
        byte[] bytes = new byte[32];
        RANDOM.nextBytes(bytes);
        String value = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
        Files.createFile(file, PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rw-------")));
        Files.writeString(file, value + "\n");
        return value;
    }
}
