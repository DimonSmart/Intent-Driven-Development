# Mini Catalog

## Product codes

A product code has one canonical representation:

- surrounding whitespace is removed;
- letters are converted using invariant uppercase;
- an empty canonical value is rejected.

Catalog operations use the canonical representation. Adding two values that
normalize to the same product code is rejected.

## Summary

The catalog returns a textual summary containing one canonical product code per
line, ordered using ordinal comparison.
