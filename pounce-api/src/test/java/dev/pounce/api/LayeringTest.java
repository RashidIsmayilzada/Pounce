package dev.pounce.api;

import com.tngtech.archunit.core.importer.ImportOption;
import com.tngtech.archunit.junit.AnalyzeClasses;
import com.tngtech.archunit.junit.ArchTest;
import com.tngtech.archunit.lang.ArchRule;

import static com.tngtech.archunit.lang.syntax.ArchRuleDefinition.noClasses;

/** The onion rule from ADR-0005: dependencies point inward, and frameworks stay in their outer layer. */
@AnalyzeClasses(packages = "dev.pounce", importOptions = ImportOption.DoNotIncludeTests.class)
class LayeringTest {

    @ArchTest
    static final ArchRule domain_depends_on_nothing_but_the_jdk = noClasses()
            .that().resideInAPackage("dev.pounce.domain..")
            .should().dependOnClassesThat().resideOutsideOfPackages("dev.pounce.domain..", "java..");

    @ArchTest
    static final ArchRule application_depends_only_on_the_domain = noClasses()
            .that().resideInAPackage("dev.pounce.application..")
            .should().dependOnClassesThat()
            .resideOutsideOfPackages("dev.pounce.application..", "dev.pounce.domain..", "java..");

    @ArchTest
    static final ArchRule infrastructure_never_uses_spring_or_the_api = noClasses()
            .that().resideInAPackage("dev.pounce.infrastructure..")
            .should().dependOnClassesThat().resideInAnyPackage("org.springframework..", "dev.pounce.api..");

    /** The platform module is shared with the CLI and is not an Engine layer: JDK only. */
    @ArchTest
    static final ArchRule platform_depends_on_nothing_but_the_jdk = noClasses()
            .that().resideInAPackage("dev.pounce.platform..")
            .should().dependOnClassesThat().resideOutsideOfPackages("dev.pounce.platform..", "java..");

    /** Where files live on each system is a concern of the outer layers; the core never asks. */
    @ArchTest
    static final ArchRule domain_and_application_never_use_the_platform_module = noClasses()
            .that().resideInAnyPackage("dev.pounce.domain..", "dev.pounce.application..")
            .should().dependOnClassesThat().resideInAPackage("dev.pounce.platform..");
}
