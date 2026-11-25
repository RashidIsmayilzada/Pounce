package dev.pounce.api;

import dev.pounce.platform.DefaultWatchedFolders;
import dev.pounce.platform.PounceHome;
import org.springframework.boot.EnvironmentPostProcessor;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.context.config.ConfigDataEnvironmentPostProcessor;
import org.springframework.core.Ordered;
import org.springframework.core.env.ConfigurableEnvironment;
import org.springframework.core.env.MapPropertySource;

import java.nio.file.Path;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Fills in {@code pounce.home} and {@code pounce.watched-folders} with the usual ones for this system
 * ({@link PounceHome}, {@link DefaultWatchedFolders}), as the weakest settings of all: {@code --pounce.home},
 * {@code -Dpounce.home}, {@code POUNCE_HOME} and the user's {@code pounce.yml} all win over them. Registered in
 * {@code META-INF/spring.factories}.
 */
class PlatformDefaults implements EnvironmentPostProcessor, Ordered {

    static final String SOURCE_NAME = "pouncePlatformDefaults";

    @Override
    public void postProcessEnvironment(ConfigurableEnvironment environment, SpringApplication application) {
        String osName = environment.getProperty("os.name", "");
        @SuppressWarnings({"unchecked", "rawtypes"}) // the environment variables: only ever strings
        Map<String, String> variables = (Map) environment.getSystemEnvironment();
        Path userHome = Path.of(environment.getProperty("user.home", ""));

        var defaults = new HashMap<String, Object>();
        defaults.put("pounce.home", PounceHome.resolve(osName, variables, userHome).folder().toString());
        // One entry per folder rather than a comma-separated list, so a folder with a comma in its name still works.
        List<Path> folders = DefaultWatchedFolders.find(osName, variables, userHome);
        for (int i = 0; i < folders.size(); i++) {
            defaults.put("pounce.watched-folders[" + i + "]", folders.get(i).toString());
        }
        environment.getPropertySources().addLast(new MapPropertySource(SOURCE_NAME, defaults));
    }

    /** After the config files are loaded, so that adding last puts these behind them. */
    @Override
    public int getOrder() {
        return ConfigDataEnvironmentPostProcessor.ORDER + 1;
    }
}
