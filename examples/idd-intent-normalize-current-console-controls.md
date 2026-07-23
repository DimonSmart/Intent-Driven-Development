# Example: Normalizing Console Control Mouse Behavior

This example shows how `idd-intent-normalize-current` moves existing intent to a
better location without changing product meaning.

## Request

```text
Use idd-intent-normalize-current to collect all current intent about mouse support in
console controls and move it into a dedicated specification.
```

## Before

```text
.idd/intent/
  IDD-0001.spec-main-menu.md
  IDD-0002.spec-table-view.md
  IDD-0003.spec-dialogs.md
```

### IDD-0001.spec-main-menu.md

```md
## Behavior

The main menu supports keyboard navigation.

The main menu supports mouse clicks on menu items.
```

### IDD-0002.spec-table-view.md

```md
## Behavior

The table supports keyboard navigation.

The table supports mouse row selection.

The table supports mouse wheel scrolling.
```

### IDD-0003.spec-dialogs.md

```md
## Behavior

Dialog buttons can be activated with keyboard shortcuts.

Dialog buttons can be activated with mouse clicks.
```

## Reorganization

`idd-intent-normalize-current` identifies:

- common behavior: console controls support mouse interaction;
- control-specific behavior: menu clicks, table row selection, wheel scrolling,
  dialog button clicks;
- no product intent conflict.

It creates a dedicated shared specification:

```text
IDD-0004.spec-console-control-mouse-behavior.md
```

## After

```text
.idd/intent/
  IDD-0001.spec-main-menu.md
  IDD-0002.spec-table-view.md
  IDD-0003.spec-dialogs.md
  IDD-0004.spec-console-control-mouse-behavior.md
```

### IDD-0004.spec-console-control-mouse-behavior.md

```md
## Intent

Console controls support mouse interaction consistently where the terminal
environment provides mouse events.

## Behavior

Clickable controls can be activated with mouse clicks.

Selectable controls can change selection using mouse input.

Scrollable controls can react to mouse wheel events.
```

### IDD-0001.spec-main-menu.md

```md
## Related Specifications

- IDD-0004.spec-console-control-mouse-behavior.md

## Behavior

The main menu supports keyboard navigation.

Main menu mouse behavior follows
IDD-0004.spec-console-control-mouse-behavior.md.

Menu items can be activated with mouse clicks.
```

### IDD-0002.spec-table-view.md

```md
## Related Specifications

- IDD-0004.spec-console-control-mouse-behavior.md

## Behavior

The table supports keyboard navigation.

Table mouse behavior follows
IDD-0004.spec-console-control-mouse-behavior.md.

Rows can be selected with mouse input.

The table supports mouse wheel scrolling.
```

### IDD-0003.spec-dialogs.md

```md
## Related Specifications

- IDD-0004.spec-console-control-mouse-behavior.md

## Behavior

Dialog buttons can be activated with keyboard shortcuts.

Dialog mouse behavior follows
IDD-0004.spec-console-control-mouse-behavior.md.

Dialog buttons can be activated with mouse clicks.
```

## What Changed

The location of intent changed.

The product meaning did not change.

Duplicated mouse behavior moved into a shared specification. Source specs now
reference the shared behavior and keep their local control-specific rules.
