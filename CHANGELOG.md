# Changelog

## Versioning scheme

This project uses a 4-number version format: `ver-A.B.C.D`. The `ver-` prefix is always present.

- **A (1st number):** a complete redesign/rewrite of the whole program or layout.
- **B (2nd number):** changes to core features, short of a full redesign.
- **C (3rd number):** large bug fixes.
- **D (4th number):** very small bug fixes. A doc/spec-only addition (no feature, no bugfix) counts as a 4th-number change too, same treatment as a minor bugfix.

Any number can climb arbitrarily high. When a higher-order number increments, every number to its right resets to 0.

**Pre-1.0 phase:** development starts at `ver-0.1.0.0-dev`. Until this project reaches `ver-1.0.0.0`, anything that would normally increment the 1st number instead increments the 2nd number — the 1st number stays locked at 0 for the entire pre-1.0 phase. The 3rd and 4th numbers behave normally throughout. Moving to `ver-1.0.0.0` only happens when the user explicitly says so.

`dev` and `main` carry the exact same version number in lockstep — the only difference is `dev`'s version string has `-dev` appended.

(Full detail, including the "why," lives in `CLAUDE.md`. This section is not edited when entries below are added — only when the scheme itself changes.)

## ver-1.0.1.1
