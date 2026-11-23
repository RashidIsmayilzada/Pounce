package dev.pounce.application;

/** A file's size and last-modified time when it was indexed; if both are unchanged, the file is not read again. */
public record FileStamp(long size, long modifiedMillis) {

    /** Stamp of a Document indexed before stamps were stored. */
    public static final FileStamp UNKNOWN = new FileStamp(-1, -1);
}
