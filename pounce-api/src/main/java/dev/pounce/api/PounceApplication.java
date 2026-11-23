package dev.pounce.api;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;

/** The Engine: keeps the Index current and answers Queries over a localhost REST API. */
@SpringBootApplication
@ConfigurationPropertiesScan
public class PounceApplication {

    public static void main(String[] args) {
        SpringApplication.run(PounceApplication.class, args);
    }
}
