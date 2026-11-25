package dev.pounce.api;

import dev.pounce.application.Embedder;
import dev.pounce.application.Indexer;
import dev.pounce.application.PassageIndex;
import dev.pounce.application.Searcher;
import dev.pounce.domain.Exclusions;
import dev.pounce.domain.PassageSplitter;
import dev.pounce.domain.WatchedFolder;
import dev.pounce.infrastructure.FolderWatcher;
import dev.pounce.infrastructure.LucenePassageIndex;
import dev.pounce.infrastructure.OnnxEmbedder;
import dev.pounce.infrastructure.FileTypeTextExtractor;
import dev.pounce.platform.PounceHome;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.event.EventListener;

import java.nio.file.Path;

/** Wires the Engine's modules together. The only place that knows every adapter. */
@Configuration(proxyBeanMethods = false)
class EngineConfiguration {

    private static final Logger log = LoggerFactory.getLogger(EngineConfiguration.class);

    @Bean
    Credentials credentials(PounceProperties properties) {
        return Credentials.in(new PounceHome(properties.home()));
    }

    @Bean(destroyMethod = "close")
    LucenePassageIndex passageIndex(PounceProperties properties) {
        return LucenePassageIndex.open(new PounceHome(properties.home()).index());
    }

    @Bean(destroyMethod = "close")
    OnnxEmbedder embedder(PounceProperties properties) {
        return OnnxEmbedder.load(properties.modelDir());
    }

    @Bean
    Exclusions exclusions(PounceProperties properties) {
        return Exclusions.defaults(Path.of(System.getProperty("user.home")), properties.home());
    }

    @Bean
    Indexer indexer(PounceProperties properties, Exclusions exclusions, Embedder embedder, PassageIndex index) {
        return new Indexer(
                properties.watchedFolders().stream().map(WatchedFolder::new).toList(),
                exclusions,
                new FileTypeTextExtractor(), new PassageSplitter(), embedder, index);
    }

    @Bean
    Searcher searcher(Embedder embedder, PassageIndex index) {
        return new Searcher(embedder, index);
    }

    /** Live changes: created before the startup reconcile runs, so nothing saved during it is missed. */
    @Bean(destroyMethod = "close")
    FolderWatcher folderWatcher(PounceProperties properties, Exclusions exclusions, Indexer indexer) {
        return FolderWatcher.start(properties.watchedFolders(), exclusions, indexer::refresh, indexer::reconcile);
    }

    /** Catch up with changes made while the Engine was not running, without delaying startup. */
    @EventListener(ApplicationReadyEvent.class)
    void reconcileInBackground(ApplicationReadyEvent event) {
        Indexer indexer = event.getApplicationContext().getBean(Indexer.class);
        Thread.ofVirtual().name("reconcile").start(() -> {
            long started = System.nanoTime();
            try {
                indexer.reconcile();
                log.info("Index reconciled in {} ms", (System.nanoTime() - started) / 1_000_000);
            } catch (RuntimeException e) {
                log.error("Reconciling the Index failed", e);
            }
        });
    }
}
