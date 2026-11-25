package dev.pounce.api;

import org.junit.jupiter.api.Test;

import java.nio.file.attribute.GroupPrincipal;
import java.nio.file.attribute.UserPrincipal;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Whom an access list may be narrowed to. On Windows a file's owner is not always the user: files created by an
 * elevated process ("Run as administrator") are owned by the Administrators group, and an access list allowing
 * only that group would lock the user out of their own Pounce home.
 */
class OwnerOnlyTest {

    @Test
    void a_file_owned_by_the_user_is_narrowed_to_that_user() {
        UserPrincipal user = () -> "LAPTOP\\rashid";

        assertThat(OwnerOnly.mayNarrowTo(user)).isTrue();
    }

    @Test
    void a_file_owned_by_a_group_is_left_as_it_is() {
        GroupPrincipal administrators = () -> "BUILTIN\\Administrators";

        assertThat(OwnerOnly.mayNarrowTo(administrators)).isFalse();
    }

    @Test
    void a_file_owned_by_an_account_windows_cannot_name_is_left_as_it_is() {
        // Java names an owner by its SID when the account no longer exists (or cannot be looked up).
        UserPrincipal unknown = () -> "S-1-5-21-1004336348-1177238915-682003330-1001";

        assertThat(OwnerOnly.mayNarrowTo(unknown)).isFalse();
    }
}
