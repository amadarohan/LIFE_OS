# Life OS — UI/UX Guidelines

## 1. Concept

**Gothic Minimalism + Editorial Command Center.**

A private command center, not a corporate CRM. Dense where it matters, quiet everywhere else.
Information hierarchy does the work; decoration does not.

## 2. Tokens

```css
--bg-void:      #08090b;   /* page background */
--bg-surface:   #0e1013;   /* cards, panels */
--bg-raised:    #14171b;   /* hover, inputs */
--bg-inset:     #0a0c0e;   /* wells, code */
--line:         #1e2227;   /* hairline borders */
--line-strong:  #2a2f36;
--text:         #d7dade;   /* primary */
--text-muted:   #8b9199;   /* secondary */
--text-faint:   #5c626a;   /* tertiary, meta */
--accent:       #8c1f2b;   /* restrained crimson */
--accent-soft:  #b8323f;
--accent-wash:  rgba(140,31,43,.12);
--ok:           #3f7d5a;
--warn:         #9a7b32;
--danger:       #a33a3a;
--info:         #3d5f7a;
```

Rules: no gradients on surfaces, no glassmorphism, no neon, no drop shadows beyond a 1px
hairline. Accent is used for one primary action per view and for active navigation.

## 3. Typography

- Persian: **Vazirmatn** (bundled locally under `wwwroot/fonts`, `@font-face`, no CDN).
- Numerals: Persian digits in prose and dates; Latin digits inside technical identifiers,
  currency codes, and code.
- Scale: 11 / 12 / 13 / 14 / 16 / 20 / 26 / 34 px. Body is 14px, line-height 1.75 for Persian.
- Headings use tighter tracking and a lighter weight than body — editorial, not bold-heavy.

## 4. Layout

```
┌────────────┬──────────────────────────────────────────────┐
│  sidebar   │  topbar: breadcrumb · search · capture · AI   │
│  240px     ├──────────────────────────────────────────────┤
│  (72px     │                                              │
│  collapsed)│  page content, max-width 1440, 32px gutters   │
└────────────┴──────────────────────────────────────────────┘
```

- RTL: `dir="rtl"` on `<html>`, logical CSS properties (`margin-inline-start`, `inset-inline-end`).
- Sidebar groups: COMMAND CENTER / EXECUTION / LIFE MANAGEMENT / INTELLIGENCE / SYSTEM.
- Collapse persists in `localStorage`.
- Below 1024px the sidebar becomes an overlay drawer.

## 5. Components

| Component | Purpose |
|---|---|
| `PageHeader` | title, subtitle, primary action slot |
| `Panel` | bordered surface with optional header/actions |
| `StatTile` | label + value + delta + hint |
| `ProgressBar` | thin, 3px, accent fill, no animation |
| `StatusPill` | status with semantic color, uppercase 11px |
| `EmptyState` | icon, one-line explanation, single action |
| `DataTable` | sticky header, zebra-free, hairline row separators |
| `SidePanel` | right-side drawer for quick create/edit |
| `ConfirmDialog` | destructive confirmation, requires explicit action |
| `Toast` | bottom-inline-start, auto-dismiss, no stacking beyond 3 |
| `AiActionCard` | proposal with payload preview, approve/reject/edit |
| `QuickCapture` | `Ctrl+K` overlay, type selector, AI extraction preview |

## 6. Interaction rules

- Every button does something. No inert controls.
- Primary action is always top-inline-end of the page header.
- Destructive actions are never the default focus.
- Forms: minimal required fields, inline validation, save disabled only while submitting.
- Lists: pagination at 50, filters persist in the URL query string.
- Loading: skeleton rows, not spinners, for tables; spinner only for actions.
- Errors: inline near the field, plus a toast for operation-level failures.
- Keyboard: `Ctrl+K` capture, `Ctrl+J` AI panel, `g d` dashboard, `g t` tasks, `g p` projects,
  `Esc` closes overlays, `Enter` submits in single-field forms.

## 7. Formatting

All display formatting goes through injected services — never `ToString()` in a component.

| Service | Example output |
|---|---|
| `IDateTimeFormatter.ToJalaliDate` | `۲۶ آبان ۱۴۰۵` |
| `IDateTimeFormatter.ToJalaliDateTime` | `۲۶ آبان ۱۴۰۵، ۱۴:۳۰` |
| `IDateTimeFormatter.ToRelative` | `۳ روز پیش` |
| `IMoneyFormatter.Format` | `۱۲٬۵۰۰٬۰۰۰ تومان` |
| `INumberFormatter.Percent` | `۶۷٪` |
| `INumberFormatter.Duration` | `۲ ساعت و ۳۰ دقیقه` |

Jalali conversion is implemented once in `JalaliDateConverter` and covered by unit tests
against known date pairs.

## 8. Accessibility

- Contrast ≥ 4.5:1 for body text, ≥ 3:1 for large text and UI borders.
- Focus ring: 2px `--accent-soft` offset 2px, always visible on keyboard focus.
- All interactive elements reachable by keyboard; overlays trap focus and restore it on close.
- `aria-live="polite"` on toasts; `role="status"` on async result regions.
- Icons always paired with text or `aria-label`.

## 9. Empty and error states

Every list has a purposeful empty state that explains what the section is for and offers the
single most useful next action. No "No data" placeholders.

## 10. What to avoid

Gradients, glassmorphism, neon, emoji as icons, gamified streaks, confetti, animated counters,
charts without a decision attached, and any dashboard number that is not derived from real data.
