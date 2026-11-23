package dev.pounce.api;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.nio.file.Path;
import java.time.Duration;
import java.util.List;

/**
 * Engine settings ({@code pounce.*}).
 *
 * @param home           where the Index, the signing key and the bootstrap secret live
 * @param watchedFolders the Watched Folders
 * @param modelDir       folder holding {@code model.onnx} and {@code tokenizer.json}
 * @param tokenLifetime  how long a JWT stays valid
 */
@ConfigurationProperties("pounce")
public record PounceProperties(Path home, List<Path> watchedFolders, Path modelDir, Duration tokenLifetime) {
}
