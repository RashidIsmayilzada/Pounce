package dev.pounce.api;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.SpringApplication;
import org.springframework.context.ApplicationContext;
import org.springframework.stereotype.Component;

/**
 * When the Launcher starts the Engine it passes its process id; the Engine shuts down when that process ends,
 * so a crashed or force-quit Launcher never leaves an Engine running on its own.
 */
@Component
class ParentProcessWatch {

    private static final Logger log = LoggerFactory.getLogger(ParentProcessWatch.class);

    ParentProcessWatch(@Value("${pounce.parent-pid:0}") long parentPid, ApplicationContext context) {
        if (parentPid <= 0) {
            return;
        }
        ProcessHandle.of(parentPid).ifPresentOrElse(
                parent -> parent.onExit().thenRun(() -> shutDown(context, "the Launcher (pid " + parentPid + ") exited")),
                () -> shutDown(context, "the Launcher (pid " + parentPid + ") is not running"));
    }

    private static void shutDown(ApplicationContext context, String reason) {
        log.info("Shutting down: {}", reason);
        Thread.ofVirtual().start(() -> System.exit(SpringApplication.exit(context, () -> 0)));
    }
}
