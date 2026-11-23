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
}
