package dev.pounce.api;

import dev.pounce.platform.PounceHome;

import javax.crypto.SecretKey;
import javax.crypto.spec.SecretKeySpec;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;
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

    static Credentials in(PounceHome home) {
        try {
            Files.createDirectories(home.folder());
            OwnerOnly.restrict(home.folder());
            String secret = readOrCreate(home.bootstrapSecret());
            byte[] key = Base64.getUrlDecoder().decode(readOrCreate(home.signingKey()));
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

    /**
     * 256 random bits, base64url, in a file only the user can read. The value is written to a temporary file next
     * to it first and then renamed into place, so a crash never leaves a half-written file; an empty one (from a
     * version before that) is replaced. When another Engine creates the file at the same moment, its value is used.
     */
    private static String readOrCreate(Path file) throws IOException {
        String existing = read(file);
        if (existing != null && !existing.isEmpty()) {
            return existing;
        }
        byte[] bytes = new byte[32];
        RANDOM.nextBytes(bytes);
        String value = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
        Path temporary = Files.createTempFile(file.getParent(), "." + file.getFileName(), ".tmp");
        try {
            // Restricted while still empty, so the value is never readable by anyone else.
            OwnerOnly.restrict(temporary);
            Files.writeString(temporary, value + "\n", StandardOpenOption.WRITE, StandardOpenOption.SYNC);
            if (existing == null) {
                Files.move(temporary, file);
            } else {
                Files.move(temporary, file, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
            }
            return value;
        } catch (FileAlreadyExistsException anotherEngineWasFaster) {
            String theirs = read(file);
            if (theirs == null || theirs.isEmpty()) {
                throw anotherEngineWasFaster;
            }
            return theirs;
        } finally {
            Files.deleteIfExists(temporary);
        }
    }

    /** The file's value without surrounding whitespace, or null when there is no such file. */
    private static String read(Path file) throws IOException {
        try {
            return Files.readString(file).strip();
        } catch (NoSuchFileException e) {
            return null;
        }
    }
}
