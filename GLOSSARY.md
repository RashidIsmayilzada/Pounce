# Pounce

Pounce is a private, on-device search bar for macOS that finds the user's files by what they say, not only by their name.

## Language

### What gets searched

**Watched Folder**:
A folder the user has chosen for Pounce to keep searchable, including everything beneath it.
_Avoid_: Root, source, location, scope

**Exclusion**:
A rule that keeps matching paths inside a Watched Folder out of the Index.
_Avoid_: Ignore pattern, blacklist, filter

**Document**:
A single file from a Watched Folder that Pounce has taken into the Index, counted once however many paths lead to it. It may have no Passages (e.g. a scanned PDF) and then only its name can match; a renamed or moved file becomes a new Document.
_Avoid_: File (when meaning an indexed one), item, entry

**Passage**:
A contiguous stretch of a Document's text; the smallest unit Pounce matches a Query against.
_Avoid_: Chunk, segment, block

**Index**:
Everything Pounce currently knows about the user's Documents and can search.
_Avoid_: Database, store, cache

### Searching

**Query**:
The text the user has typed into the Launcher or CLI at a given moment.
_Avoid_: Search term, input, prompt

**Keyword Match**:
A Document that matches because it contains the Query's words or its name does.
_Avoid_: Text match, exact match, lexical hit

**Semantic Match**:
A Document that matches because its meaning is close to the Query's, even without shared words.
_Avoid_: Vector match, AI match, fuzzy match

**Result**:
One Document returned for a Query, shown with its single best Passage.
_Avoid_: Hit, match, row

**Top Result**:
The highest-ranked Result for a Query, the one ⏎ opens.
_Avoid_: Top hit, best match

**Early Results**:
The first Results shown for a Query, built from Keyword Matches only so they appear instantly.
_Avoid_: Fast results, preliminary results

**Final Results**:
The Results for a Query after Keyword and Semantic Matches are merged; they replace the Early Results.
_Avoid_: Full results, merged results, refined results

**Snippet**:
The excerpt of a Result's best Passage shown to the user, with the Query's words highlighted.
_Avoid_: Preview, excerpt, summary

### Parts of Pounce

**Engine**:
The background part of Pounce that keeps the Index current and answers Queries.
_Avoid_: Backend, daemon, server, service

**Launcher**:
The ⌥Space search window where the user types a Query and acts on Results.
_Avoid_: Search bar, popup, Spotlight

**CLI**:
The terminal command `pounce` that sends Queries to the Engine.
_Avoid_: Command-line client, shell tool
