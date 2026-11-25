package dev.pounce.api;

import dev.pounce.platform.PounceHome;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledOnOs;
import org.junit.jupiter.api.condition.OS;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.FileSystems;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.AclEntry;
import java.nio.file.attribute.AclEntryFlag;
import java.nio.file.attribute.AclEntryPermission;
import java.nio.file.attribute.AclEntryType;
import java.nio.file.attribute.AclFileAttributeView;
import java.nio.file.attribute.PosixFileAttributeView;
import java.nio.file.attribute.PosixFilePermissions;
import java.nio.file.attribute.UserPrincipal;
import java.nio.file.attribute.UserPrincipalNotFoundException;
import java.util.ArrayList;
import java.util.EnumSet;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/** The bootstrap secret and signing key on a real disk: created once, kept for the user alone, reused after. */
class CredentialsTest {

    @TempDir
    Path folder;

    @Test
    void creates_the_secret_and_the_key_on_first_start() throws IOException {
        var home = new PounceHome(folder.resolve("home"));

        Credentials credentials = Credentials.in(home);

        String secret = Files.readString(home.bootstrapSecret()).strip();
        assertThat(secret).matches("[\\w-]{43}");
        assertThat(credentials.isBootstrapSecret(secret)).isTrue();
        assertThat(credentials.signingKey().getEncoded()).hasSize(32);
    }

    @Test
    void only_the_user_can_read_the_home_the_secret_and_the_key() throws IOException {
        var home = new PounceHome(folder.resolve("home"));

        Credentials.in(home);

        assertOwnerOnly(home.folder());
        assertOwnerOnly(home.bootstrapSecret());
        assertOwnerOnly(home.signingKey());
    }

    @Test
    void tightens_a_home_that_already_exists() throws IOException {
        var home = new PounceHome(Files.createDirectories(folder.resolve("home")));

        Credentials.in(home);

        assertOwnerOnly(home.folder());
    }

    @Test
    void a_second_start_reuses_the_same_secret_and_key() throws IOException {
        var home = new PounceHome(folder.resolve("home"));
        Credentials first = Credentials.in(home);
        String secret = Files.readString(home.bootstrapSecret()).strip();

        Credentials second = Credentials.in(home);

        assertThat(second.isBootstrapSecret(secret)).isTrue();
        assertThat(second.signingKey()).isEqualTo(first.signingKey());
    }

    @Test
    void an_empty_secret_or_key_left_by_a_crash_is_replaced() throws IOException {
        var home = new PounceHome(Files.createDirectories(folder.resolve("home")));
        Files.writeString(home.bootstrapSecret(), "");
        Files.writeString(home.signingKey(), " \n");

        Credentials credentials = Credentials.in(home);

        String secret = Files.readString(home.bootstrapSecret()).strip();
        assertThat(secret).matches("[\\w-]{43}");
        assertThat(credentials.isBootstrapSecret(secret)).isTrue();
        assertThat(credentials.signingKey().getEncoded()).hasSize(32);
        assertOwnerOnly(home.bootstrapSecret());
    }

    @Test
    void leaves_no_temporary_files_behind() throws IOException {
        var home = new PounceHome(folder.resolve("home"));

        Credentials.in(home);

        try (var files = Files.list(home.folder())) {
            assertThat(files.map(file -> file.getFileName().toString()))
                    .containsExactlyInAnyOrder("bootstrap-secret", "signing-key");
        }
    }

    @Test
    @EnabledOnOs(OS.WINDOWS)
    void on_windows_a_secret_under_a_folder_shared_with_all_users_is_not_shared() throws IOException {
        var lookup = FileSystems.getDefault().getUserPrincipalLookupService();
        UserPrincipal users;
        try {
            users = lookup.lookupPrincipalByGroupName("BUILTIN\\Users");
        } catch (UserPrincipalNotFoundException translated) {
            // Windows translates the name on other languages (VORDEFINIERT\Benutzer…) and Java cannot look up a SID.
            assumeTrue(false, "the Users group has another name on this system: " + translated.getName());
            return;
        }
        var view = Files.getFileAttributeView(folder, AclFileAttributeView.class);
        assumeNarrowable(view.getOwner());
        var acl = new ArrayList<>(view.getAcl());
        acl.add(AclEntry.newBuilder().setType(AclEntryType.ALLOW).setPrincipal(users)
                .setPermissions(AclEntryPermission.READ_DATA, AclEntryPermission.READ_ATTRIBUTES,
                        AclEntryPermission.READ_ACL, AclEntryPermission.SYNCHRONIZE)
                .setFlags(AclEntryFlag.FILE_INHERIT, AclEntryFlag.DIRECTORY_INHERIT).build());
        view.setAcl(acl);
        var home = new PounceHome(folder.resolve("home"));

        Credentials.in(home);

        for (Path path : List.of(home.folder(), home.bootstrapSecret(), home.signingKey())) {
            assertThat(Files.getFileAttributeView(path, AclFileAttributeView.class).getAcl())
                    .as("ACL of %s", path).noneMatch(entry -> entry.principal().equals(users));
        }
    }

    /**
     * Files made by an elevated process (as on a CI runner, where the Engine runs as an administrator) are owned by
     * the Administrators group; their permissions are left as they are on purpose, so there is nothing to check.
     */
    private static void assumeNarrowable(UserPrincipal owner) {
        assumeTrue(OwnerOnly.mayNarrowTo(owner),
                "files here are owned by " + owner.getName() + ", so their permissions are left as they are");
    }

    /** POSIX permissions where the disk has them, else an ACL that allows the owner and nobody else. */
    static void assertOwnerOnly(Path path) throws IOException {
        if (Files.getFileStore(path).supportsFileAttributeView(PosixFileAttributeView.class)) {
            assertThat(PosixFilePermissions.toString(Files.getPosixFilePermissions(path)))
                    .as("permissions of %s", path)
                    .isEqualTo(Files.isDirectory(path) ? "rwx------" : "rw-------");
        } else {
            UserPrincipal owner = Files.getOwner(path);
            assumeNarrowable(owner);
            List<AclEntry> acl = Files.getFileAttributeView(path, AclFileAttributeView.class).getAcl();
            assertThat(acl).as("ACL of %s", path).isNotEmpty().allSatisfy(entry -> {
                assertThat(entry.type()).isEqualTo(AclEntryType.ALLOW);
                assertThat(entry.principal()).isEqualTo(owner);
            });
            assertThat(acl.getFirst().permissions()).containsAll(EnumSet.of(
                    AclEntryPermission.READ_DATA, AclEntryPermission.WRITE_DATA));
        }
    }
}
