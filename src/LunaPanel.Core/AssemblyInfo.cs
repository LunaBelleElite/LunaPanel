using System.Runtime.CompilerServices;

// Lets LunaPanel.Tests reach the internal test-only hooks documented next to
// their declarations (e.g. StatusFileWatcher's simulate-* methods), which
// exist to drive real-OS-timing-dependent code paths deterministically
// instead of waiting on FileSystemWatcher's real event-delivery timing.
// Production code in LunaPanel.Core has no other reason to declare anything
// internal, and this does not relax the "Core never discovers a filesystem
// path" rule - constructors still take every path as a parameter.
[assembly: InternalsVisibleTo("LunaPanel.Tests")]
