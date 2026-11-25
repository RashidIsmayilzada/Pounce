package dev.pounce.domain;

import java.io.IOException;
import java.nio.file.Path;
import java.util.HashSet;
import java.util.Set;

/**
 * Keeps a walk of a folder tree out of loops through folders that lead somewhere else. On Windows a junction (or a
 * mount point) is a folder to a walk, not a link, so it is entered; one that leads to a folder above it would be
 * walked again and again until the paths grow too long. OneDrive folders are reparse points too and must be entered,
 * so such a folder is judged by where it leads: never into a folder the walk is inside, and never twice to the same
 * place. Ordinary folders need no check. One instance per walk.
 */
public final class LinkedFolders {

    private final Set<Path> entered = new HashSet<>();

    /**
     * Whether the walk may enter this folder, which may lead elsewhere. False when where it leads cannot be found
     * out (it is broken, or cannot be read).
     */
    public boolean mayEnter(Path folder) {
        Path holder = folder.getParent();
        if (holder == null) {
            return true;
        }
        try {
            return mayEnter(holder.toRealPath(), folder.toRealPath());
        } catch (IOException broken) {
            return false;
        }
    }

    /**
     * Whether the walk may enter a folder that sits in the folder whose real path is {@code holder} and leads to the
     * real path {@code target}: not when the walk is already inside the target (a loop), nor when it already entered
     * the target through another such folder.
     */
    public boolean mayEnter(Path holder, Path target) {
        return !holder.startsWith(target) && entered.add(target);
    }
}
