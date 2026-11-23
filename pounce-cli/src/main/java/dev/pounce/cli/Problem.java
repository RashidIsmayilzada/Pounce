package dev.pounce.cli;

/** Something the user can fix, with a message that says how. Printed as-is, without a stack trace. */
final class Problem extends Exception {
    Problem(String message) {
        super(message);
    }
}
